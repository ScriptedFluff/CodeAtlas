using CodeGraphMcp.Db;
using CodeGraphMcp.Parser;

namespace CodeGraphMcp.Git;

/// <summary>
/// Determines which files have changed since the last indexed commit
/// and drives re-parsing of only those files.
/// </summary>
public sealed class DeltaEngine(
    string repoPath,
    CodeGraphDb db,
    IReadOnlyList<ILanguageParser> parsers)
{
    private readonly Dictionary<string, ILanguageParser> _extensionMap = BuildMap(parsers);

    public sealed record DeltaResult(
        int FilesChanged,
        int FilesReindexed,
        int SymbolsDeleted,
        int SymbolsAdded,
        long ElapsedMs,
        string Mode    // "full" | "delta" | "noop"
    );

    /// <summary>
    /// Runs the appropriate indexing mode:
    ///   - "full"  : no prior index state → delegates to IndexOrchestrator
    ///   - "delta" : prior index state exists → re-parses only changed files
    ///   - "noop"  : HEAD matches last_commit_hash → nothing to do
    /// </summary>
    public Task<DeltaResult> RunAsync(CancellationToken ct)
        => RunAsync(noIgnore: false, ct);

    public async Task<DeltaResult> RunAsync(bool noIgnore = false, CancellationToken ct = default)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();

        var headHash   = GitHelpers.GetHeadCommitHash(repoPath);
        var lastHash   = db.IndexState.GetLastCommitHash();
        var branch     = GitHelpers.GetCurrentBranch(repoPath) ?? "unknown";
        var dirtyFiles = GitHelpers.GetDirtyFiles(repoPath);

        // ── NOOP ──────────────────────────────────────────────────────────────
        if (headHash is not null && headHash == lastHash && dirtyFiles.Count == 0)
        {
            sw.Stop();
            return new DeltaResult(0, 0, 0, 0, sw.ElapsedMilliseconds, "noop");
        }

        // ── FULL (first run, detached HEAD, or empty repo) ────────────────────
        if (lastHash is null || headHash is null)
        {
            var orchestrator = new Indexer.IndexOrchestrator(db);
            var result = await orchestrator.IndexAsync(repoPath, noIgnore, ct);

            var finalHash = headHash ?? "unknown";
            db.IndexState.SetIndexState(branch, finalHash, 0);

            sw.Stop();
            return new DeltaResult(
                result.FilesProcessed, result.FilesProcessed,
                0, result.SymbolsWritten, sw.ElapsedMilliseconds, "full");
        }

        // ── DELTA ─────────────────────────────────────────────────────────────
        var committedChanged = headHash != lastHash
            ? GitHelpers.GetChangedFiles(repoPath, lastHash)
            : (IReadOnlyList<string>)[];

        var changedFiles = committedChanged
            .Concat(dirtyFiles)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        int symbolsDeleted = 0;
        int symbolsAdded   = 0;
        int reindexed      = 0;

        await Parallel.ForEachAsync(changedFiles, new ParallelOptions
        {
            MaxDegreeOfParallelism = Environment.ProcessorCount,
            CancellationToken      = ct
        }, async (relPath, token) =>
        {
            var ext = Path.GetExtension(relPath);
            if (!_extensionMap.TryGetValue(ext, out var parser)) return;

            var normPath = relPath.Replace('\\', '/');
            var fullPath = Path.Combine(repoPath, relPath);

            if (!File.Exists(fullPath))
            {
                lock (db)
                {
                    var count = db.Symbols.CountByFile(normPath);
                    db.Symbols.DeleteByFile(normPath);
                    db.References.DeleteByFile(normPath);
                    Interlocked.Add(ref symbolsDeleted, count);
                }
                return;
            }

            try
            {
                var source  = await File.ReadAllTextAsync(fullPath, token);
                var symbols = parser.Parse(normPath, source).ToList();
                var refs    = parser.ParseReferences(normPath, source).ToList();

                lock (db)
                {
                    var existingCount = db.Symbols.CountByFile(normPath);
                    db.Symbols.UpsertFileDelta(normPath, symbols);
                    db.References.UpsertFileReferences(
                        normPath,
                        refs,
                        (name, preferredFile) => db.Symbols.ResolveId(name, preferredFile),
                        path => db.Symbols.GetPrimarySymbolIdForFile(path));
                    Interlocked.Add(ref symbolsDeleted, existingCount);
                    Interlocked.Add(ref symbolsAdded, symbols.Count);
                }

                Interlocked.Increment(ref reindexed);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[DeltaEngine] Warning: failed to re-parse {relPath}: {ex.Message}");
            }
        });

        db.IndexState.SetIndexState(branch, headHash, 0);
        sw.Stop();

        return new DeltaResult(
            changedFiles.Count, reindexed,
            symbolsDeleted, symbolsAdded,
            sw.ElapsedMilliseconds, "delta");
    }

    private static Dictionary<string, ILanguageParser> BuildMap(IReadOnlyList<ILanguageParser> parsers)
    {
        var map = new Dictionary<string, ILanguageParser>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in parsers)
            foreach (var ext in p.FileExtensions)
                map[ext] = p;
        return map;
    }
}
