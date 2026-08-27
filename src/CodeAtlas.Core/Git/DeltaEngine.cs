using CodeAtlas.Core.Db;
using CodeAtlas.Models;
using CodeAtlas.Parser;

namespace CodeAtlas.Core.Git;

/// <summary>
/// Determines which files have changed since the last indexed commit
/// and drives re-parsing of only those files.
/// </summary>
public sealed class DeltaEngine
{
    private readonly string repoPath;
    private readonly CodeAtlasDb db;
    private readonly IParserRegistry _parserRegistry;

    public DeltaEngine(
        string repoPath,
        CodeAtlasDb db,
        IParserRegistry parserRegistry)
    {
        this.repoPath = repoPath;
        this.db = db;
        _parserRegistry = parserRegistry;
    }

    public DeltaEngine(
        string repoPath,
        CodeAtlasDb db,
        IReadOnlyList<ILanguageParser> parsers)
        : this(repoPath, db, new ParserRegistry(parsers))
    {
    }

    public sealed record DeltaResult(
        int FilesChanged,
        int FilesReindexed,
        int SymbolsDeleted,
        int SymbolsAdded,
        long ElapsedMs,
        string Mode,           // "full" | "delta" | "noop" | "seeded-delta"
        string? SeedSourcePath = null,
        string? SeedCommitHash = null
    );

    /// <summary>
    /// Runs the appropriate indexing mode:
    ///   - "full"         : no prior index state → delegates to IndexOrchestrator
    ///   - "delta"        : prior index state exists → re-parses only changed files
    ///   - "seeded-delta" : seeded from existing worktree DB → re-parses delta files
    ///   - "noop"         : HEAD matches last_commit_hash → nothing to do
    /// </summary>
    public Task<DeltaResult> RunAsync(CancellationToken ct)
        => RunAsync(noIgnore: false, verbose: false, ct);

    public Task<DeltaResult> RunAsync(bool noIgnore, CancellationToken ct)
        => RunAsync(noIgnore: noIgnore, verbose: false, ct);

    public async Task<DeltaResult> RunAsync(bool noIgnore = false, bool verbose = false, CancellationToken ct = default)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();

        var headHash   = GitHelpers.GetHeadCommitHash(repoPath);
        var lastHash   = db.IndexState.GetLastCommitHash();
        var branch     = GitHelpers.GetCurrentBranch(repoPath) ?? "unknown";
        var dirtyFiles = GitHelpers.GetDirtyFiles(repoPath);

        var activeParsers = _parserRegistry.GetAllParsers();
        var indexedParsers = db.IndexedParsers.GetIndexedParsers();
        var indexedMap = indexedParsers.ToDictionary(p => p.ParserId, StringComparer.OrdinalIgnoreCase);
        var activeMap = activeParsers.ToDictionary(p => p.Metadata.Id, StringComparer.OrdinalIgnoreCase);

        // Find newly added or re-enabled parsers (or updated versions/extensions)
        var addedParsers = activeParsers.Where(p =>
            !indexedMap.TryGetValue(p.Metadata.Id, out var rec) ||
            !string.Equals(rec.Version, p.Metadata.Version, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(rec.Extensions, string.Join(",", p.FileExtensions), StringComparison.OrdinalIgnoreCase)
        ).ToList();

        // Find removed or disabled parsers
        var removedParsers = indexedParsers.Where(p => !activeMap.ContainsKey(p.ParserId)).ToList();

        int symbolsDeleted = 0;

        // Clean up symbols from removed/disabled parsers
        if (removedParsers.Count > 0)
        {
            lock (db)
            {
                foreach (var p in removedParsers)
                {
                    var count = db.Symbols.DeleteByLanguage(p.Language);
                    symbolsDeleted += count;
                    db.IndexedParsers.RemoveIndexedParser(p.ParserId);
                }
            }
        }

        // Discover files for newly added parsers
        var addedParserFiles = new List<string>();
        if (addedParsers.Count > 0)
        {
            var newExtensions = addedParsers.SelectMany(p => p.FileExtensions).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var orchestrator = new Indexer.IndexOrchestrator(db, _parserRegistry);
            var discoveredFiles = orchestrator.EnumerateFiles(repoPath, noIgnore);
            foreach (var file in discoveredFiles)
            {
                var ext = Path.GetExtension(file);
                if (newExtensions.Contains(ext))
                {
                    var relPath = Path.GetRelativePath(repoPath, file).Replace('\\', '/');
                    addedParserFiles.Add(relPath);
                }
            }
        }

        // ── NOOP ──────────────────────────────────────────────────────────────
        if (headHash is not null && headHash == lastHash && dirtyFiles.Count == 0 && addedParserFiles.Count == 0 && removedParsers.Count == 0)
        {
            if (addedParsers.Count > 0)
            {
                lock (db)
                {
                    db.IndexedParsers.RecordAllIndexedParsers(addedParsers);
                }
            }

            sw.Stop();
            return new DeltaResult(0, 0, 0, 0, sw.ElapsedMilliseconds, "noop");
        }

        string mode = addedParserFiles.Count > 0 && headHash == lastHash && dirtyFiles.Count == 0
            ? "parser-delta"
            : "delta";

        string? seedSourcePath = null;
        string? seedCommitHash = null;

        if (lastHash is null && headHash is not null)
        {
            var seedResult = DbSeedingService.TrySeedDatabase(repoPath, db);
            if (seedResult is not null)
            {
                lastHash = seedResult.SourceCommitHash;
                seedSourcePath = seedResult.SourceDbPath;
                seedCommitHash = seedResult.SourceCommitHash;
                mode = "seeded-delta";
            }
        }

        // ── FULL (first run, detached HEAD, or empty repo) ────────────────────
        if (lastHash is null || headHash is null)
        {
            var orchestrator = new Indexer.IndexOrchestrator(db, _parserRegistry);
            var result = await orchestrator.IndexAsync(repoPath, noIgnore, verbose, ct);

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
            .Concat(addedParserFiles)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        int symbolsAdded = 0;
        int reindexed    = 0;

        var fileSymbols = new System.Collections.Concurrent.ConcurrentBag<(string FilePath, IReadOnlyList<Symbol> Symbols)>();
        var fileRefs = new System.Collections.Concurrent.ConcurrentBag<(string FilePath, IReadOnlyList<SymbolReference> Refs)>();

        await Parallel.ForEachAsync(changedFiles, new ParallelOptions
        {
            MaxDegreeOfParallelism = Environment.ProcessorCount,
            CancellationToken      = ct
        }, async (relPath, token) =>
        {
            var ext = Path.GetExtension(relPath);
            var parser = _parserRegistry.GetParserForExtension(ext);
            if (parser == null) return;

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
                var parseResult = parser.ParseFile(normPath, source);

                if (parseResult.Symbols.Count > 0)
                {
                    fileSymbols.Add((normPath, parseResult.Symbols));
                    Interlocked.Add(ref symbolsAdded, parseResult.Symbols.Count);
                }

                if (parseResult.References.Count > 0)
                {
                    fileRefs.Add((normPath, parseResult.References));
                }

                Interlocked.Increment(ref reindexed);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[DeltaEngine] Warning: failed to re-parse {relPath}: {ex.Message}");
            }
        });

        var symbolsList = fileSymbols.ToList();
        var refsList = fileRefs.ToList();

        lock (db)
        {
            var cache = db.Symbols.UpsertSymbolsBatch(symbolsList);
            db.References.UpsertReferencesBatch(refsList, cache);
            db.IndexState.SetIndexState(branch, headHash, 0);
            db.IndexedParsers.RecordAllIndexedParsers(activeParsers);
        }

        sw.Stop();

        return new DeltaResult(
            changedFiles.Count, reindexed,
            symbolsDeleted, symbolsAdded,
            sw.ElapsedMilliseconds, mode,
            seedSourcePath, seedCommitHash);
    }
}
