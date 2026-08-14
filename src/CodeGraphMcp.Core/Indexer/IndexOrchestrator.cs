using System.Diagnostics;
using CodeGraphMcp.Db;
using CodeGraphMcp.Git;
using CodeGraphMcp.Models;
using CodeGraphMcp.Parser;
using Microsoft.Extensions.Logging;

namespace CodeGraphMcp.Indexer;

public sealed class IndexOrchestrator(CodeGraphDb db, ILogger<IndexOrchestrator>? logger = null)
{
    private static readonly HashSet<string> DefaultIgnoredDirs = new(StringComparer.OrdinalIgnoreCase)
    {
        ".git", ".codegraph", "bin", "obj",
        "node_modules", "dist", ".next", "coverage", ".vs",
        "packages", "TestResults", "publish", "out", "temp", "tmp", "vendor", ".idea"
    };

    private readonly Dictionary<string, ILanguageParser> _extensionMap = BuildExtensionMap();

    private static Dictionary<string, ILanguageParser> BuildExtensionMap()
    {
        IReadOnlyList<ILanguageParser> parsers =
        [
            new CSharpParser(),
            new TypeScriptParser(),
            new JavaScriptParser(),
            new HtmlParser(),
            new PowerShellParser(),
            new JsonParser(),
            new YamlParser(),
            new CssParser(),
        ];
        var map = new Dictionary<string, ILanguageParser>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in parsers)
            foreach (var ext in p.FileExtensions)
                map[ext] = p;
        return map;
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
            Console.WriteLine($"[IndexOrchestrator] Warning: Directory '{repoPath}' does not exist.");
            return new IndexResult(0, 0, 0);
        }

        var config = CodeGraphMcp.Config.AppConfig.Load(repoPath);
        if (config.WildcardIgnoreAll)
        {
            Console.WriteLine("[IndexOrchestrator] Warning: Ignore configuration contains wildcard '*' — all files have been filtered out.");
            logger?.LogWarning("Ignore configuration contains wildcard '*' — all files in {RepoPath} are filtered out.", repoPath);
        }

        var files = EnumerateFiles(repoPath, noIgnore).ToList();

        if (files.Count == 0 && config.WildcardIgnoreAll)
        {
            Console.WriteLine("[IndexOrchestrator] Notice: 0 files indexed because 'Ignore' pattern '*' filtered out all repository files.");
        }

        logger?.LogInformation("Found {FileCount} files to process in {RepoPath} (noIgnore: {NoIgnore})", files.Count, repoPath, noIgnore);


        const int BatchSize = 2500;
        int symbolsWritten = 0;
        int parsedFileCount = 0;
        int totalFiles = files.Count;

        foreach (var chunk in files.Chunk(BatchSize))
        {
            var chunkSymbols = new System.Collections.Concurrent.ConcurrentBag<(string RelPath, IReadOnlyList<Symbol> Symbols)>();
            var chunkRefs = new System.Collections.Concurrent.ConcurrentBag<(string RelPath, IReadOnlyList<SymbolReference> Refs)>();

            await Parallel.ForEachAsync(chunk, new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount, CancellationToken = ct }, async (filePath, token) =>
            {
                var ext = Path.GetExtension(filePath);
                if (!_extensionMap.TryGetValue(ext, out var parser)) return;

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
                        chunkSymbols.Add((relPath, parseResult.Symbols));
                        Interlocked.Add(ref symbolsWritten, parseResult.Symbols.Count);
                    }

                    if (parseResult.References.Count > 0)
                    {
                        chunkRefs.Add((relPath, parseResult.References));
                    }
                }
                catch (Exception ex)
                {
                    logger?.LogWarning(ex, "Failed to parse {FilePath} — skipping", filePath);
                }
            });

            var symbolsList = chunkSymbols.ToList();
            var refsList = chunkRefs.ToList();

            lock (db)
            {
                var cache = db.Symbols.UpsertSymbolsBatch(symbolsList);
                db.References.UpsertReferencesBatch(refsList, cache);
            }
        }


        sw.Stop();
        logger?.LogInformation("Indexed {Symbols} symbols in {Ms}ms", symbolsWritten, sw.ElapsedMilliseconds);
        return new IndexResult(files.Count, symbolsWritten, sw.ElapsedMilliseconds);
    }


    private IEnumerable<string> EnumerateFiles(string root, bool noIgnore = false)
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
                if (ignoredDirs.Contains(dirName))
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
                if (_extensionMap.ContainsKey(ext) && (noIgnore || !matcher.IsIgnored(file, isDirectory: false)))
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

