using System.Diagnostics;
using System.Threading.Channels;
using CodeGraphMcp.Db;
using CodeGraphMcp.Git;
using CodeGraphMcp.Models;
using CodeGraphMcp.Parser;
using Microsoft.Extensions.Logging;

namespace CodeGraphMcp.Indexer;

public sealed class IndexOrchestrator
{
    private static readonly HashSet<string> DefaultIgnoredDirs = new(StringComparer.OrdinalIgnoreCase)
    {
        ".git", ".codegraph", "bin", "obj",
        "node_modules", "dist", ".next", "coverage", ".vs",
        "packages", "TestResults", "publish", "out", "temp", "tmp", "vendor", ".idea",
        "lib", "wwwroot/lib"
    };

    private readonly CodeGraphDb db;
    private readonly IParserRegistry _parserRegistry;
    private readonly ILogger<IndexOrchestrator>? logger;

    public IndexOrchestrator(
        CodeGraphDb db,
        IParserRegistry? parserRegistry = null,
        ILogger<IndexOrchestrator>? logger = null)
    {
        this.db = db;
        _parserRegistry = parserRegistry ?? ParserRegistry.CreateDefault();
        this.logger = logger;
    }

    public IndexOrchestrator(
        CodeGraphDb db,
        ILogger<IndexOrchestrator>? logger)
        : this(db, null, logger)
    {
    }

    public Task<IndexResult> IndexAsync(string repoPath, CancellationToken ct)
        => IndexAsync(repoPath, noIgnore: false, verbose: false, ct);

    public Task<IndexResult> IndexAsync(string repoPath, bool noIgnore, CancellationToken ct)
        => IndexAsync(repoPath, noIgnore: noIgnore, verbose: false, ct);

    public async Task<IndexResult> IndexAsync(string repoPath, bool noIgnore = false, bool verbose = false, CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();

        if (!Directory.Exists(repoPath))
        {
            Console.Error.WriteLine($"[IndexOrchestrator] Warning: Directory '{repoPath}' does not exist.");
            return new IndexResult(0, 0, 0);
        }

        var config = CodeGraphMcp.Config.AppConfig.Load(repoPath);
        if (config.WildcardIgnoreAll)
        {
            Console.Error.WriteLine("[IndexOrchestrator] Warning: Ignore configuration contains wildcard '*' — all files have been filtered out.");
            logger?.LogWarning("Ignore configuration contains wildcard '*' — all files in {RepoPath} are filtered out.", repoPath);
        }

        var files = EnumerateFiles(repoPath, noIgnore).ToList();

        if (files.Count == 0 && config.WildcardIgnoreAll)
        {
            Console.Error.WriteLine("[IndexOrchestrator] Notice: 0 files indexed because 'Ignore' pattern '*' filtered out all repository files.");
        }

        logger?.LogInformation("Found {FileCount} files to process in {RepoPath} (noIgnore: {NoIgnore})", files.Count, repoPath, noIgnore);

        if (files.Count == 0)
        {
            return new IndexResult(0, 0, 0);
        }

        int symbolsWritten = 0;
        int parsedFileCount = 0;
        int totalFiles = files.Count;

        var masterCache = new SymbolLookupCache();
        var allRefsList = new System.Collections.Concurrent.ConcurrentBag<(string RelPath, IReadOnlyList<SymbolReference> Refs)>();

        var channel = Channel.CreateUnbounded<(string RelPath, FileParseResult Result)>(new UnboundedChannelOptions
        {
            SingleWriter = false,
            SingleReader = true
        });

        var dbConsumerTask = Task.Run(async () =>
        {
            var symbolBatch = new List<(string RelPath, IReadOnlyList<Symbol> Symbols)>();
            int batchCount = 0;

            await foreach (var item in channel.Reader.ReadAllAsync(ct))
            {
                if (item.Result.Symbols.Count > 0)
                {
                    symbolBatch.Add((item.RelPath, item.Result.Symbols));
                }

                if (item.Result.References.Count > 0)
                {
                    allRefsList.Add((item.RelPath, item.Result.References));
                }

                if (symbolBatch.Count >= 250)
                {
                    batchCount++;
                    FlushSymbolBatch(db, symbolBatch, masterCache, batchCount, verbose);
                    symbolBatch.Clear();
                }
            }

            if (symbolBatch.Count > 0)
            {
                batchCount++;
                FlushSymbolBatch(db, symbolBatch, masterCache, batchCount, verbose);
                symbolBatch.Clear();
            }
        }, ct);

        await Parallel.ForEachAsync(files, new ParallelOptions
        {
            MaxDegreeOfParallelism = Environment.ProcessorCount,
            CancellationToken = ct
        }, async (filePath, token) =>
        {
            var ext = Path.GetExtension(filePath);
            var parser = _parserRegistry.GetParserForExtension(ext);
            if (parser == null) return;

            try
            {
                var relPath = WorkspacePathNormalizer.NormalizeRelativePath(repoPath, filePath);
                var currentIdx = Interlocked.Increment(ref parsedFileCount);
                if (verbose)
                {
                    Console.WriteLine($"[{currentIdx} of {totalFiles}] Starting [{parser.Language}] {relPath}...");
                }

                var fileSw = Stopwatch.StartNew();
                var source = await File.ReadAllTextAsync(filePath, token);
                var parseResult = parser.ParseFile(relPath, source);
                fileSw.Stop();

                if (verbose)
                {
                    Console.WriteLine($"[{currentIdx} of {totalFiles}] Finished [{parser.Language}] {relPath} in {fileSw.ElapsedMilliseconds}ms ({parseResult.Symbols.Count} symbols, {parseResult.References.Count} refs)");
                }

                if (parseResult.Symbols.Count > 0)
                {
                    Interlocked.Add(ref symbolsWritten, parseResult.Symbols.Count);
                }

                await channel.Writer.WriteAsync((relPath, parseResult), token);
            }
            catch (Exception ex)
            {
                logger?.LogWarning(ex, "Failed to parse {FilePath} — skipping", filePath);
            }
        });

        channel.Writer.Complete();
        await dbConsumerTask;

        var allRefs = allRefsList.ToList();
        if (allRefs.Count > 0)
        {
            var totalRefsCount = allRefs.Sum(r => r.Refs.Count);
            if (verbose)
            {
                Console.WriteLine($"[Database References] Resolving and writing {totalRefsCount} references across repository graph...");
            }

            var refSw = Stopwatch.StartNew();
            lock (db)
            {
                db.References.UpsertReferencesBatch(allRefs, masterCache);
            }
            refSw.Stop();

            if (verbose)
            {
                Console.WriteLine($"[Database References] Completed writing {totalRefsCount} references in {refSw.ElapsedMilliseconds}ms.");
            }
        }

        lock (db)
        {
            db.IndexedParsers.RecordAllIndexedParsers(_parserRegistry.GetAllParsers());
        }

        sw.Stop();
        logger?.LogInformation("Indexed {Symbols} symbols in {Ms}ms", symbolsWritten, sw.ElapsedMilliseconds);
        return new IndexResult(files.Count, symbolsWritten, sw.ElapsedMilliseconds);
    }

    private static void FlushSymbolBatch(
        CodeGraphDb db,
        List<(string RelPath, IReadOnlyList<Symbol> Symbols)> symbolBatch,
        SymbolLookupCache masterCache,
        int batchNumber,
        bool verbose)
    {
        var count = symbolBatch.Sum(s => s.Symbols.Count);
        if (verbose)
        {
            Console.WriteLine($"[Background DB Writer Batch #{batchNumber}] Writing {count} symbols to database...");
        }

        var dbSw = Stopwatch.StartNew();
        lock (db)
        {
            var chunkCache = db.Symbols.UpsertSymbolsBatch(symbolBatch);
            masterCache.MergeFrom(chunkCache);
        }
        dbSw.Stop();

        if (verbose)
        {
            Console.WriteLine($"[Background DB Writer Batch #{batchNumber}] Wrote {count} symbols in {dbSw.ElapsedMilliseconds}ms.");
        }
    }


    internal IEnumerable<string> EnumerateFiles(string root, bool noIgnore = false)
    {
        var config = CodeGraphMcp.Config.AppConfig.Load(root);
        var ignoredDirs = new HashSet<string>(config.IgnoreDirectories, StringComparer.OrdinalIgnoreCase);

        // Pre-compile ignore patterns once before scanning files
        var simpleNameIgnores = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var extensionIgnores = new List<string>();
        var globRules = new List<GitIgnoreMatcher.IgnoreRule>();

        foreach (var p in config.IgnorePatterns)
        {
            var normP = p.Replace('\\', '/').Trim();
            if (string.IsNullOrWhiteSpace(normP)) continue;

            if (!normP.Contains('/'))
            {
                if (normP.StartsWith('*') && normP.IndexOf('*', 1) < 0)
                {
                    extensionIgnores.Add(normP[1..].ToLowerInvariant());
                }
                else
                {
                    simpleNameIgnores.Add(normP);
                }
            }
            else
            {
                var rule = GitIgnoreMatcher.IgnoreRule.Parse(normP, "");
                if (rule is not null)
                {
                    globRules.Add(rule);
                }
            }
        }

        // Pre-compile include patterns if specified and not containing "*"
        var includeSimpleNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var includeExtensions = new List<string>();
        var includeGlobRules = new List<GitIgnoreMatcher.IgnoreRule>();

        foreach (var p in config.IncludePatterns)
        {
            var normP = p.Replace('\\', '/').Trim();
            if (string.IsNullOrWhiteSpace(normP)) continue;

            if (!normP.Contains('/'))
            {
                if (normP.StartsWith('*') && normP.IndexOf('*', 1) < 0)
                {
                    includeExtensions.Add(normP[1..].ToLowerInvariant());
                }
                else
                {
                    includeSimpleNames.Add(normP);
                }
            }
            else
            {
                var rule = GitIgnoreMatcher.IgnoreRule.Parse(normP, "");
                if (rule is not null)
                {
                    includeGlobRules.Add(rule);
                }
            }
        }

        var matcher = new GitIgnoreMatcher(root, noIgnore);
        var queue = new Queue<string>();
        queue.Enqueue(root);

        while (queue.Count > 0)
        {
            var dir = queue.Dequeue();

            if (!Directory.Exists(dir)) continue;

            // Dynamically load nested .gitignore if present
            if (!noIgnore)
            {
                matcher.LoadGitIgnore(dir);
            }

            foreach (var sub in Directory.GetDirectories(dir))
            {
                var dirName = Path.GetFileName(sub);
                var relSub = WorkspacePathNormalizer.NormalizeRelativePath(root, sub).Replace('\\', '/');

                if (ignoredDirs.Contains(dirName) ||
                    relSub.Equals("wwwroot/lib", StringComparison.OrdinalIgnoreCase) ||
                    relSub.EndsWith("/wwwroot/lib", StringComparison.OrdinalIgnoreCase))
                    continue;

                if (noIgnore || !matcher.IsIgnored(sub, isDirectory: true))
                    queue.Enqueue(sub);
            }

            bool hasIncludeFilter = config.IncludePatterns.Count > 0;

            foreach (var file in Directory.GetFiles(dir))
            {
                var relPath = WorkspacePathNormalizer.NormalizeRelativePath(root, file);
                if (IsFileIgnored(relPath, file, simpleNameIgnores, extensionIgnores, globRules))
                    continue;

                if (hasIncludeFilter && !IsFileIncluded(relPath, file, includeSimpleNames, includeExtensions, includeGlobRules))
                    continue;

                var ext = Path.GetExtension(file);
                if (_parserRegistry.GetParserForExtension(ext) != null && (noIgnore || !matcher.IsIgnored(file, isDirectory: false)))
                    yield return file;
            }
        }
    }

    private static bool IsFileIncluded(
        string relPath,
        string fullPath,
        HashSet<string> simpleNames,
        List<string> extensions,
        List<GitIgnoreMatcher.IgnoreRule> globRules)
    {
        var fileName = Path.GetFileName(fullPath);

        if (simpleNames.Contains(fileName))
            return true;

        var lowerName = fileName.ToLowerInvariant();
        foreach (var ext in extensions)
        {
            if (lowerName.EndsWith(ext))
                return true;
        }

        if (globRules.Count > 0)
        {
            var normRelPath = relPath.Replace('\\', '/');
            foreach (var rule in globRules)
            {
                if (rule.IsMatch(normRelPath, isDirectory: false))
                    return true;
            }
        }

        return false;
    }

    private static bool IsFileIgnored(
        string relPath,
        string fullPath,
        HashSet<string> simpleNameIgnores,
        List<string> extensionIgnores,
        List<GitIgnoreMatcher.IgnoreRule> globRules)
    {
        var fileName = Path.GetFileName(fullPath);

        // 1. Fast O(1) filename check
        if (simpleNameIgnores.Contains(fileName))
            return true;

        // 2. Fast extension check
        var lowerName = fileName.ToLowerInvariant();
        foreach (var ext in extensionIgnores)
        {
            if (lowerName.EndsWith(ext))
                return true;
        }

        // 3. Pre-compiled glob regex check (zero Regex creation cost)
        if (globRules.Count > 0)
        {
            var normRelPath = relPath.Replace('\\', '/');
            foreach (var rule in globRules)
            {
                if (rule.IsMatch(normRelPath, isDirectory: false))
                    return true;
            }
        }

        return false;
    }
}

