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
        ];
        var map = new Dictionary<string, ILanguageParser>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in parsers)
            foreach (var ext in p.FileExtensions)
                map[ext] = p;
        return map;
    }

    public async Task<IndexResult> IndexAsync(string repoPath, CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        var files = EnumerateFiles(repoPath).ToList();

        logger?.LogInformation("Found {FileCount} files to process in {RepoPath}", files.Count, repoPath);

        int symbolsWritten = 0;
        var fileRefs = new System.Collections.Concurrent.ConcurrentBag<(string RelPath, List<SymbolReference> Refs)>();

        await Parallel.ForEachAsync(files, new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount, CancellationToken = ct }, async (filePath, ct) =>
        {
            var ext = Path.GetExtension(filePath);
            if (!_extensionMap.TryGetValue(ext, out var parser)) return;

            try
            {
                var source = await File.ReadAllTextAsync(filePath, ct);
                var relPath = WorkspacePathNormalizer.NormalizeRelativePath(repoPath, filePath);
                var symbols = parser.Parse(relPath, source).ToList();
                var refs = parser.ParseReferences(relPath, source).ToList();

                lock (db)
                {
                    db.Symbols.UpsertFile(relPath, symbols);
                    Interlocked.Add(ref symbolsWritten, symbols.Count);
                }

                if (refs.Count > 0)
                {
                    fileRefs.Add((relPath, refs));
                }
            }
            catch (Exception ex)
            {
                logger?.LogWarning(ex, "Failed to parse {FilePath} — skipping", filePath);
            }
        });

        foreach (var (relPath, refs) in fileRefs)
        {
            db.References.UpsertFileReferences(
                relPath,
                refs,
                (name, preferredFile) => db.Symbols.ResolveId(name, preferredFile),
                path => db.Symbols.GetPrimarySymbolIdForFile(path));
        }

        sw.Stop();
        logger?.LogInformation("Indexed {Symbols} symbols in {Ms}ms", symbolsWritten, sw.ElapsedMilliseconds);
        return new IndexResult(files.Count, symbolsWritten, sw.ElapsedMilliseconds);
    }

    private IEnumerable<string> EnumerateFiles(string root)
    {
        var matcher = new GitIgnoreMatcher(root);
        var queue = new Queue<string>();
        queue.Enqueue(root);

        while (queue.Count > 0)
        {
            var dir = queue.Dequeue();

            if (!Directory.Exists(dir)) continue;

            // Dynamically load nested .gitignore if present
            matcher.LoadGitIgnore(dir);

            foreach (var sub in Directory.GetDirectories(dir))
            {
                var dirName = Path.GetFileName(sub);
                if (DefaultIgnoredDirs.Contains(dirName))
                    continue;

                if (!matcher.IsIgnored(sub, isDirectory: true))
                    queue.Enqueue(sub);
            }

            foreach (var file in Directory.GetFiles(dir))
            {
                var ext = Path.GetExtension(file);
                if (_extensionMap.ContainsKey(ext) && !matcher.IsIgnored(file, isDirectory: false))
                    yield return file;
            }
        }
    }
}

