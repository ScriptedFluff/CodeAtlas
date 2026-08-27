using System.Diagnostics;
using CodeGraphMcp.Config;
using CodeGraphMcp.Db;
using CodeGraphMcp.Git;
using CodeGraphMcp.Parser;

namespace CodeGraphMcp.Indexer;

/// <summary>
/// Result summary of a glob-based batch synchronization or purge operation.
/// </summary>
public sealed record GlobSyncResult(
    string Pattern,
    int FilesProcessed,
    int FilesDeleted,
    int SymbolsAdded,
    int SymbolsUpdated,
    int SymbolsDeleted,
    int ReferencesUpdated,
    int ReferencesDeleted,
    long ElapsedMs,
    string? Message = null);

public interface IGlobFileIndexer
{
    Task<GlobSyncResult> SyncGlobAsync(
        string workspaceRoot,
        CodeGraphDb db,
        string pathOrGlob,
        bool isDelete = false,
        bool noIgnore = false,
        bool verbose = false,
        CancellationToken ct = default);

    Task<GlobSyncResult> PurgeGlobAsync(
        string workspaceRoot,
        CodeGraphDb db,
        string globPattern,
        CancellationToken ct = default);
}

/// <summary>
/// High-performance engine for syncing and purging files matching a glob expression or single file path.
/// </summary>
public sealed class GlobFileIndexer : IGlobFileIndexer
{
    private readonly ISingleFileIndexer _singleFileIndexer;
    private readonly IParserRegistry _parserRegistry;

    public GlobFileIndexer(
        ISingleFileIndexer? singleFileIndexer = null,
        IParserRegistry? parserRegistry = null)
    {
        _singleFileIndexer = singleFileIndexer ?? new SingleFileIndexer();
        _parserRegistry = parserRegistry ?? ParserRegistry.CreateDefault();
    }

    public static bool IsGlobPattern(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return false;
        return input.Contains('*') || input.Contains('?') || input.Contains('[') || input.Contains('{');
    }

    public async Task<GlobSyncResult> PurgeGlobAsync(
        string workspaceRoot,
        CodeGraphDb db,
        string globPattern,
        CancellationToken ct = default)
    {
        return await SyncGlobAsync(workspaceRoot, db, globPattern, isDelete: true, ct: ct);
    }

    public async Task<GlobSyncResult> SyncGlobAsync(
        string workspaceRoot,
        CodeGraphDb db,
        string pathOrGlob,
        bool isDelete = false,
        bool noIgnore = false,
        bool verbose = false,
        CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        var normWorkspace = WorkspacePathResolver.NormalizePath(workspaceRoot);
        var normPattern = pathOrGlob.Replace('\\', '/').Trim();
        if (normPattern.StartsWith("./", StringComparison.Ordinal))
        {
            normPattern = normPattern[2..];
        }

        int filesProcessed = 0;
        int filesDeleted = 0;
        int symbolsAdded = 0;
        int symbolsUpdated = 0;
        int symbolsDeleted = 0;
        int refsUpdated = 0;
        int refsDeleted = 0;

        bool isGlob = IsGlobPattern(normPattern);

        // ── 1. Single-file direct path handling ─────────────────────────────────────
        if (!isGlob && !isDelete && (File.Exists(Path.Combine(normWorkspace, normPattern)) || File.Exists(normPattern)))
        {
            var singleResult = await _singleFileIndexer.UpsertFileAsync(normWorkspace, db, normPattern, null, ct);
            sw.Stop();
            return new GlobSyncResult(
                Pattern: normPattern,
                FilesProcessed: 1,
                FilesDeleted: 0,
                SymbolsAdded: singleResult.SymbolsAdded,
                SymbolsUpdated: singleResult.SymbolsUpdated,
                SymbolsDeleted: singleResult.SymbolsDeleted,
                ReferencesUpdated: singleResult.ReferencesUpdated,
                ReferencesDeleted: 0,
                ElapsedMs: sw.ElapsedMilliseconds,
                Message: singleResult.Message);
        }

        if (!isGlob && isDelete)
        {
            var delResult = await _singleFileIndexer.DeleteFileAsync(normWorkspace, db, normPattern, ct);
            sw.Stop();
            return new GlobSyncResult(
                Pattern: normPattern,
                FilesProcessed: 0,
                FilesDeleted: 1,
                SymbolsAdded: 0,
                SymbolsUpdated: 0,
                SymbolsDeleted: delResult.SymbolsDeleted,
                ReferencesUpdated: 0,
                ReferencesDeleted: delResult.ReferencesDeleted,
                ElapsedMs: sw.ElapsedMilliseconds,
                Message: delResult.Message);
        }

        // ── 2. Glob / Batch handling ────────────────────────────────────────────────
        var globRule = GitIgnoreMatcher.IgnoreRule.Parse(normPattern, "");

        // Fetch all distinct file_paths currently tracked in the database
        var dbFiles = new List<string>();
        lock (db)
        {
            using var cmd = db.Connection.CreateCommand();
            cmd.CommandText = "SELECT DISTINCT file_path FROM symbols;";
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                dbFiles.Add(reader.GetString(0));
            }
        }

        var matchingDbFiles = dbFiles
            .Where(p => MatchesPattern(p, normPattern, globRule))
            .ToList();

        if (isDelete)
        {
            // Purge all matching files from DB
            foreach (var delPath in matchingDbFiles)
            {
                var delRes = await _singleFileIndexer.DeleteFileAsync(normWorkspace, db, delPath, ct);
                filesDeleted++;
                symbolsDeleted += delRes.SymbolsDeleted;
                refsDeleted += delRes.ReferencesDeleted;
            }

            sw.Stop();
            return new GlobSyncResult(
                Pattern: normPattern,
                FilesProcessed: 0,
                FilesDeleted: filesDeleted,
                SymbolsAdded: 0,
                SymbolsUpdated: 0,
                SymbolsDeleted: symbolsDeleted,
                ReferencesUpdated: 0,
                ReferencesDeleted: refsDeleted,
                ElapsedMs: sw.ElapsedMilliseconds,
                Message: $"Purged {filesDeleted} files ({symbolsDeleted} symbols, {refsDeleted} refs) matching '{normPattern}'.");
        }

        // Upsert Mode:
        // A. Enumerate matching files on disk
        var orchestrator = new IndexOrchestrator(db, _parserRegistry);
        var allCandidates = orchestrator.EnumerateFiles(normWorkspace, noIgnore);

        var matchingDiskFiles = allCandidates
            .Where(f =>
            {
                var rel = WorkspacePathNormalizer.NormalizeRelativePath(normWorkspace, f);
                return MatchesPattern(rel, normPattern, globRule);
            })
            .ToList();

        var diskRelSet = matchingDiskFiles
            .Select(f => WorkspacePathNormalizer.NormalizeRelativePath(normWorkspace, f))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        // B. Reconcile deleted files (tracked in DB matching glob, but missing on disk)
        var staleDbFiles = matchingDbFiles.Where(p => !diskRelSet.Contains(p)).ToList();
        foreach (var stalePath in staleDbFiles)
        {
            var delRes = await _singleFileIndexer.DeleteFileAsync(normWorkspace, db, stalePath, ct);
            filesDeleted++;
            symbolsDeleted += delRes.SymbolsDeleted;
            refsDeleted += delRes.ReferencesDeleted;
        }

        // C. Upsert matching files on disk
        foreach (var diskFile in matchingDiskFiles)
        {
            var upRes = await _singleFileIndexer.UpsertFileAsync(normWorkspace, db, diskFile, null, ct);
            filesProcessed++;
            symbolsAdded += upRes.SymbolsAdded;
            symbolsUpdated += upRes.SymbolsUpdated;
            symbolsDeleted += upRes.SymbolsDeleted;
            refsUpdated += upRes.ReferencesUpdated;
        }

        sw.Stop();
        return new GlobSyncResult(
            Pattern: normPattern,
            FilesProcessed: filesProcessed,
            FilesDeleted: filesDeleted,
            SymbolsAdded: symbolsAdded,
            SymbolsUpdated: symbolsUpdated,
            SymbolsDeleted: symbolsDeleted,
            ReferencesUpdated: refsUpdated,
            ReferencesDeleted: refsDeleted,
            ElapsedMs: sw.ElapsedMilliseconds,
            Message: $"Synced {filesProcessed} files ({symbolsAdded} added, {symbolsUpdated} updated), purged {filesDeleted} missing files.");
    }

    private static bool MatchesPattern(string relPath, string rawPattern, GitIgnoreMatcher.IgnoreRule? parsedRule)
    {
        var normRel = relPath.Replace('\\', '/').Trim();
        if (normRel.Equals(rawPattern, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (parsedRule != null && parsedRule.IsMatch(normRel, false))
        {
            return true;
        }

        return false;
    }
}
