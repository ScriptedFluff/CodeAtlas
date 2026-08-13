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
        ".git", ".agent-index", "bin", "obj",
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

        var files = EnumerateFiles(repoPath, noIgnore).ToList();

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
                    var source = await File.ReadAllTextAsync(filePath, token);
                    var relPath = WorkspacePathNormalizer.NormalizeRelativePath(repoPath, filePath);
                    var parseResult = parser.ParseFile(relPath, source);

                    var currentIdx = Interlocked.Increment(ref parsedFileCount);
                    if (verbose)
                    {
                        Console.WriteLine($"[{currentIdx} of {totalFiles}] Parsed {relPath} ({parseResult.Symbols.Count} symbols, {parseResult.References.Count} refs)");
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
                if (DefaultIgnoredDirs.Contains(dirName))
                    continue;

                if (noIgnore || !matcher.IsIgnored(sub, isDirectory: true))
                    queue.Enqueue(sub);
            }

            foreach (var file in Directory.GetFiles(dir))
            {
                var ext = Path.GetExtension(file);
                if (_extensionMap.ContainsKey(ext) && (noIgnore || !matcher.IsIgnored(file, isDirectory: false)))
                    yield return file;
            }
        }
    }
}

