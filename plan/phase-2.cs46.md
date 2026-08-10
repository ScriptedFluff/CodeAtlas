# Phase 2 — Git Delta Engine & Live File Buffer
## Detailed Implementation Plan — CodeGraphMcp (C# / .NET 10)

**Version:** 1.0.0  
**Phase:** 2 of 4  
**Timeline:** Weeks 3–4  
**Estimated Effort:** 48 hours  
**Tasks:** GIT-01 · GIT-02 · GIT-03  
**Status:** Ready for Execution  
**Depends On:** Phase 1 (✅ Complete — 7/7 tests pass, 81 symbols indexed in 19ms, Native AOT binary working)

---

## Objective

Switch from full re-scan on every `index` call to an intelligent, commit-aware delta engine:

1. **GIT-01** — Detect which files changed since the last indexed commit using `LibGit2Sharp`. Re-parse only those files. Delete stale symbols from the DB before re-inserting.
2. **GIT-02** — Extend `index_state` to track the head commit hash, branch name, and a dirty-files counter so the engine knows *where to resume from* on subsequent calls.
3. **GIT-03** — Add a live in-memory `DirtyFileBuffer` that parses unsaved/uncommitted edits on demand, overlaying the DB result so MCP tools always return the freshest data.

The end result: `CodeGraphMcp.exe index` on a large repo after a small commit should be **~10–50× faster** than a full re-parse, and symbol queries should reflect in-progress edits without requiring a commit.

---

## Phase 2 Exit Criteria (Non-Negotiable)

- [ ] `dotnet test` — all tests pass (≥7 existing + all new GIT-01/02/03 tests)
- [ ] `dotnet publish -r win-x64 -p:PublishAot=true` succeeds with no new trim errors
- [ ] First `index` run on a fresh DB still does a full scan (baseline behaviour preserved)
- [ ] Second `index` run with no git changes completes in < 50ms (no re-parse)
- [ ] Edit one `.cs` file, make a commit, run `index` — only that file's symbols are re-parsed (verified via `indexed_at` column)
- [ ] Edit a `.cs` file without committing, run `query` — returns live symbols from the dirty buffer, not stale DB values
- [ ] `LibGit2Sharp` handles detached HEAD, empty repos (no commits), and repos with no changes gracefully (no exceptions)
- [ ] `index_state` table updated atomically with the new commit hash after successful indexing

---

## Background: What Phase 1 Left Us

### Existing `index_state` Schema (from `MigrationRunner.cs`)

```sql
CREATE TABLE IF NOT EXISTS index_state (
    branch            TEXT NOT NULL,
    last_commit_hash  TEXT PRIMARY KEY,
    indexed_at        TEXT NOT NULL,
    dirty_files_count INTEGER DEFAULT 0
);
```

> [!NOTE]
> The table exists but is **never written to** in Phase 1. The `index` command always does a full walk. Phase 2 wires this up properly.

### Existing Phase 1 Files of Interest

| File | Relevance |
| :--- | :--- |
| [Program.cs](file:///D:/Projects/C%23/repositorydb/src/CodeGraphMcp/Program.cs) | `index` command handler — needs delta mode wired in |
| [IndexOrchestrator.cs](file:///D:/Projects/C%23/repositorydb/src/CodeGraphMcp/Indexer/IndexOrchestrator.cs) | Full-scan walker — delta engine wraps/extends this |
| [CodeGraphDb.cs](file:///D:/Projects/C%23/repositorydb/src/CodeGraphMcp/Db/CodeGraphDb.cs) | Connection factory — gains `IndexState` queries accessor |
| [SymbolQueries.cs](file:///D:/Projects/C%23/repositorydb/src/CodeGraphMcp/Db/Queries/SymbolQueries.cs) | Needs `DeleteByFile(path)` for delta cleanup |
| [MigrationRunner.cs](file:///D:/Projects/C%23/repositorydb/src/CodeGraphMcp/Db/MigrationRunner.cs) | Schema already has `index_state` — no migration changes needed |

---

## Directory Structure Changes (Phase 2 Additions)

```
src/CodeGraphMcp/
├── Git/                                     ← NEW namespace
│   ├── DeltaEngine.cs                       ← [NEW] Git diff → changed file list
│   ├── DirtyFileBuffer.cs                   ← [NEW] In-memory live symbol overlay
│   └── GitHelpers.cs                        ← [NEW] Safe HEAD/branch resolution helpers
├── Db/
│   └── Queries/
│       └── IndexStateQueries.cs             ← [NEW] Read/write index_state table
│   (SymbolQueries.cs)                       ← [MODIFY] Add DeleteByFile + UpsertFileDelta
├── Indexer/
│   └── IndexOrchestrator.cs                 ← [MODIFY] Add delta mode path
└── Program.cs                               ← [MODIFY] Wire --delta flag and dirty buffer
```

```
tests/CodeGraphMcp.Tests/
├── Fixtures/
│   └── SampleClass.cs                       ← Unchanged
├── Git/                                     ← NEW
│   ├── DeltaEngineTests.cs                  ← [NEW] Delta detection tests (uses temp git repo)
│   └── DirtyFileBufferTests.cs              ← [NEW] Live buffer overlay tests
└── ParserTests.cs                           ← Unchanged
```

---

## GIT-01 — Git Diff Delta Reconciliation (18 hours)

### 1.1 — NuGet Package Addition

`LibGit2Sharp` provides managed .NET bindings to `libgit2`. It is **not** AOT-trimming-friendly by default, so it must be added with a native library bundle. The package ships with native DLLs for `win-x64`.

```powershell
cd D:\Projects\C#\repositorydb\src\CodeGraphMcp
dotnet add package LibGit2Sharp --version 0.30.0
```

Also add to the test project:
```powershell
cd D:\Projects\C#\repositorydb\tests\CodeGraphMcp.Tests
dotnet add package LibGit2Sharp --version 0.30.0
```

> [!IMPORTANT]
> After adding LibGit2Sharp, immediately run `dotnet publish -r win-x64 -p:PublishAot=true -o publish\` and confirm it still compiles. LibGit2Sharp uses runtime reflection internally, so we must verify it does not introduce fatal trim errors. If it does, the fallback strategy is to shell out to `git diff --name-only` via `System.Diagnostics.Process` (see § 1.2 fallback).

### 1.2 — Fallback Strategy (if LibGit2Sharp AOT fails)

If LibGit2Sharp produces fatal trim errors (not just warnings), replace with a thin git shell wrapper:

```csharp
// Git/GitHelpers.cs — fallback shell approach
public static class GitHelpers
{
    public static string? GetHeadCommitHash(string repoPath)
    {
        var result = RunGit(repoPath, "rev-parse HEAD");
        return result?.Trim();
    }

    public static IReadOnlyList<string> GetChangedFiles(string repoPath, string sinceCommit)
    {
        var result = RunGit(repoPath, $"diff --name-only {sinceCommit} HEAD");
        if (result is null) return [];
        return result.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    public static IReadOnlyList<string> GetDirtyFiles(string repoPath)
    {
        var result = RunGit(repoPath, "status --porcelain");
        if (result is null) return [];

        return result
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Length > 3 ? line[3..].Trim() : null)
            .Where(f => f is not null)
            .Select(f => f!)
            .ToList();
    }

    private static string? RunGit(string cwd, string args)
    {
        try
        {
            var psi = new ProcessStartInfo("git", args)
            {
                WorkingDirectory     = cwd,
                RedirectStandardOutput = true,
                RedirectStandardError  = true,
                UseShellExecute        = false,
                CreateNoWindow         = true
            };
            using var p = Process.Start(psi);
            if (p is null) return null;
            var output = p.StandardOutput.ReadToEnd();
            p.WaitForExit();
            return p.ExitCode == 0 ? output : null;
        }
        catch
        {
            return null;
        }
    }
}
```

> [!NOTE]
> The shell fallback has slightly more overhead per call (~5ms process spawn) but is completely AOT-safe. Prefer LibGit2Sharp for richer data (tree diffs, commit metadata), but accept the fallback if AOT is broken. The git shell fallback is also more resilient in unusual repo configurations (shallow clones, worktrees, submodules).

### 1.3 — `IndexStateQueries.cs` — New Query Class

```csharp
// Db/Queries/IndexStateQueries.cs
using Microsoft.Data.Sqlite;

namespace CodeGraphMcp.Db.Queries;

public sealed class IndexStateQueries(SqliteConnection connection)
{
    /// <summary>Returns the last successfully indexed commit hash, or null if never indexed.</summary>
    public string? GetLastCommitHash()
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT last_commit_hash FROM index_state ORDER BY indexed_at DESC LIMIT 1;";
        return cmd.ExecuteScalar() as string;
    }

    /// <summary>Returns the current branch name from the last index run.</summary>
    public string? GetLastBranch()
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT branch FROM index_state ORDER BY indexed_at DESC LIMIT 1;";
        return cmd.ExecuteScalar() as string;
    }

    /// <summary>
    /// Upserts the index state entry.
    /// Uses last_commit_hash as primary key so each commit is one row.
    /// </summary>
    public void SetIndexState(string branch, string commitHash, int dirtyFilesCount = 0)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            INSERT INTO index_state (branch, last_commit_hash, indexed_at, dirty_files_count)
            VALUES (@branch, @hash, @now, @dirty)
            ON CONFLICT(last_commit_hash) DO UPDATE SET
                branch            = excluded.branch,
                indexed_at        = excluded.indexed_at,
                dirty_files_count = excluded.dirty_files_count;
            """;
        cmd.Parameters.AddWithValue("@branch", branch);
        cmd.Parameters.AddWithValue("@hash",   commitHash);
        cmd.Parameters.AddWithValue("@now",    DateTimeOffset.UtcNow.ToString("O"));
        cmd.Parameters.AddWithValue("@dirty",  dirtyFilesCount);
        cmd.ExecuteNonQuery();
    }

    /// <summary>True if any index state row exists for this repo.</summary>
    public bool HasBeenIndexed()
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM index_state;";
        return (long)(cmd.ExecuteScalar() ?? 0L) > 0;
    }
}
```

### 1.4 — `SymbolQueries.cs` — Add Delta Methods

Add the following methods to the existing `SymbolQueries` class in [SymbolQueries.cs](file:///D:/Projects/C%23/repositorydb/src/CodeGraphMcp/Db/Queries/SymbolQueries.cs):

```csharp
/// <summary>
/// Deletes all symbols for a given relative file path.
/// Called before re-parsing a file in delta mode so stale symbols are removed.
/// </summary>
public void DeleteByFile(string relFilePath)
{
    using var cmd = _connection.CreateCommand();
    cmd.CommandText = "DELETE FROM symbols WHERE file_path = @path;";
    cmd.Parameters.AddWithValue("@path", relFilePath);
    cmd.ExecuteNonQuery();
}

/// <summary>
/// Upserts only the symbols for a single changed file.
/// Wraps DeleteByFile + bulk insert in a transaction for atomicity.
/// </summary>
public void UpsertFileDelta(string relFilePath, IEnumerable<Models.Symbol> symbols)
{
    using var tx = _connection.BeginTransaction();
    try
    {
        DeleteByFile(relFilePath);
        foreach (var sym in symbols)
            InsertSymbol(sym, tx);
        tx.Commit();
    }
    catch
    {
        tx.Rollback();
        throw;
    }
}
```

> [!NOTE]
> `InsertSymbol(sym, tx)` is a private helper that already exists (or is trivially extractable from `UpsertFile`). If the current implementation uses a transaction-less bulk insert, refactor to accept an optional transaction parameter so it can be reused.

### 1.5 — `DeltaEngine.cs` — Core Delta Logic

```csharp
// Git/DeltaEngine.cs
using CodeGraphMcp.Db;
using CodeGraphMcp.Db.Queries;
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
    public async Task<DeltaResult> RunAsync(CancellationToken ct = default)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();

        var headHash = GitHelpers.GetHeadCommitHash(repoPath);
        var lastHash = db.IndexState.GetLastCommitHash();
        var branch   = GitHelpers.GetCurrentBranch(repoPath) ?? "unknown";

        // ── NOOP ──────────────────────────────────────────────────────────────
        if (headHash is not null && headHash == lastHash)
        {
            sw.Stop();
            return new DeltaResult(0, 0, 0, 0, sw.ElapsedMilliseconds, "noop");
        }

        // ── FULL (first run, detached HEAD, or empty repo) ────────────────────
        if (lastHash is null || headHash is null)
        {
            // Full scan — delegate back to IndexOrchestrator
            var orchestrator = new Indexer.IndexOrchestrator(db);
            var result = await orchestrator.IndexAsync(repoPath, ct);

            var finalHash = headHash ?? "unknown";
            db.IndexState.SetIndexState(branch, finalHash, 0);

            sw.Stop();
            return new DeltaResult(
                result.FilesProcessed, result.FilesProcessed,
                0, result.SymbolsWritten, sw.ElapsedMilliseconds, "full");
        }

        // ── DELTA ─────────────────────────────────────────────────────────────
        var changedFiles = GitHelpers.GetChangedFiles(repoPath, lastHash);

        int symbolsDeleted = 0;
        int symbolsAdded   = 0;
        int reindexed      = 0;

        foreach (var relPath in changedFiles)
        {
            var ext = Path.GetExtension(relPath);
            if (!_extensionMap.TryGetValue(ext, out var parser)) continue;

            var fullPath = Path.Combine(repoPath, relPath);
            if (!File.Exists(fullPath))
            {
                // File was deleted — remove its symbols
                symbolsDeleted += CountAndDelete(relPath);
                continue;
            }

            try
            {
                var source  = await File.ReadAllTextAsync(fullPath, ct);
                var symbols = parser.Parse(relPath, source).ToList();

                lock (db)
                {
                    // Count existing symbols before delete
                    symbolsDeleted += db.Symbols.CountByFile(relPath);
                    db.Symbols.UpsertFileDelta(relPath, symbols);
                    symbolsAdded += symbols.Count;
                }

                reindexed++;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[DeltaEngine] Warning: failed to re-parse {relPath}: {ex.Message}");
            }
        }

        db.IndexState.SetIndexState(branch, headHash, 0);
        sw.Stop();

        return new DeltaResult(
            changedFiles.Count, reindexed,
            symbolsDeleted, symbolsAdded,
            sw.ElapsedMilliseconds, "delta");
    }

    private int CountAndDelete(string relPath)
    {
        var count = db.Symbols.CountByFile(relPath);
        db.Symbols.DeleteByFile(relPath);
        return count;
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
```

### 1.6 — `GitHelpers.cs` — Safe Git Operations

```csharp
// Git/GitHelpers.cs
using System.Diagnostics;

namespace CodeGraphMcp.Git;

/// <summary>
/// Thin shell-based git helpers.
/// Prefer these over LibGit2Sharp to guarantee Native AOT compatibility.
/// All methods return null/empty on failure — never throw.
/// </summary>
public static class GitHelpers
{
    public static string? GetHeadCommitHash(string repoPath) =>
        RunGit(repoPath, "rev-parse HEAD")?.Trim();

    public static string? GetCurrentBranch(string repoPath) =>
        RunGit(repoPath, "rev-parse --abbrev-ref HEAD")?.Trim();

    /// <summary>
    /// Returns relative file paths that changed between sinceCommit and HEAD.
    /// Includes renames (both old and new path) and deletions.
    /// </summary>
    public static IReadOnlyList<string> GetChangedFiles(string repoPath, string sinceCommit)
    {
        // --diff-filter=d excludes files removed from the tree (handled separately by caller)
        var result = RunGit(repoPath, $"diff --name-only {sinceCommit} HEAD");
        if (result is null) return [];

        return result
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(f => !string.IsNullOrWhiteSpace(f))
            .ToList();
    }

    /// <summary>
    /// Returns relative paths of files that have uncommitted changes (modified or new in working tree).
    /// Uses `git status --porcelain` for machine-readable output.
    /// </summary>
    public static IReadOnlyList<string> GetDirtyFiles(string repoPath)
    {
        var result = RunGit(repoPath, "status --porcelain");
        if (result is null) return [];

        return result
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Where(line => line.Length >= 3)
            .Select(line =>
            {
                // Porcelain format: "XY filename" or "XY old -> new"
                var path = line[3..].Trim();
                // Handle renamed files: "old.cs -> new.cs" — take the new path
                var arrowIdx = path.IndexOf(" -> ", StringComparison.Ordinal);
                return arrowIdx >= 0 ? path[(arrowIdx + 4)..].Trim() : path;
            })
            .Where(f => !string.IsNullOrWhiteSpace(f))
            .ToList();
    }

    /// <summary>True if the given path is inside a git repository.</summary>
    public static bool IsGitRepo(string repoPath) =>
        RunGit(repoPath, "rev-parse --git-dir") is not null;

    private static string? RunGit(string cwd, string args)
    {
        try
        {
            var psi = new ProcessStartInfo("git", args)
            {
                WorkingDirectory      = cwd,
                RedirectStandardOutput = true,
                RedirectStandardError  = true,
                UseShellExecute        = false,
                CreateNoWindow         = true
            };
            using var p = Process.Start(psi);
            if (p is null) return null;
            var output = p.StandardOutput.ReadToEnd();
            p.WaitForExit();
            return p.ExitCode == 0 ? output : null;
        }
        catch
        {
            return null;
        }
    }
}
```

> [!TIP]
> Using `git` shell calls instead of LibGit2Sharp gives us AOT compatibility for free. The 3–5ms process-spawn overhead is negligible compared to file I/O and parse time. This also avoids bundling the `git2-*.dll` native library in the publish output.

### 1.7 — `CodeGraphDb.cs` Changes

Add an `IndexState` accessor alongside `Symbols` and `References`:

```csharp
// Add to CodeGraphDb constructor and property declarations:
public IndexStateQueries IndexState { get; }

// In constructor, after existing query setup:
IndexState = new IndexStateQueries(_connection);
```

### 1.8 — GIT-01 Stability Gate

```powershell
# Fresh index (full mode)
.\publish\CodeGraphMcp.exe index D:\Projects\C#\repositorydb --store-local
# Expected: "Indexed 19 files, 81 symbols (Xms) [full]"

# Immediate re-index (noop mode — HEAD hasn't changed)
.\publish\CodeGraphMcp.exe index D:\Projects\C#\repositorydb --store-local
# Expected: "Nothing to index — HEAD unchanged (Xms) [noop]"

# Make a change, commit, then delta index
git -C D:\Projects\C#\repositorydb add . && git commit -m "test delta"
.\publish\CodeGraphMcp.exe index D:\Projects\C#\repositorydb --store-local
# Expected: "1 file changed, N symbols updated (Xms) [delta]"

# Verify index_state
sqlite3 "D:\Projects\C#\repositorydb\.agent-index\code_graph.db" "SELECT * FROM index_state ORDER BY indexed_at DESC LIMIT 3;"
```

---

## GIT-02 — Storage Router Completion (10 hours)

> [!NOTE]
> `StorageConfig.cs` was fully implemented in Phase 1 (all three modes: `--db-dir`, `--store-local`, default `%LOCALAPPDATA%`). GIT-02 completes the remaining CLI wiring and adds a `--force-full` flag for emergency re-index.

### 2.1 — CLI Changes (`Program.cs`)

Add the following to the `index` command handler:

```csharp
// New option — add alongside existing options
var forceFull = new Option<bool>("--force-full", "Skip delta detection and re-index everything");
indexCommand.AddOption(forceFull);

// Updated handler signature
indexCommand.SetHandler(async (repo, dbDir, storeLocal, force) =>
{
    var dbPath = StorageConfig.ResolveDbPath(dbDir?.FullName, storeLocal, repo.FullName);
    using var db = new CodeGraphDb(dbPath);
    db.Migrate();

    if (force || !GitHelpers.IsGitRepo(repo.FullName))
    {
        // Non-git repo or forced: always full scan
        var orchestrator = new IndexOrchestrator(db);
        var result = await orchestrator.IndexAsync(repo.FullName);
        Console.WriteLine($"Indexed {result.FilesProcessed} files, {result.SymbolsWritten} symbols ({result.ElapsedMs}ms) [full]");
    }
    else
    {
        var parsers = GetParsers(); // Factory method extracting parser list
        var delta   = new DeltaEngine(repo.FullName, db, parsers);
        var result  = await delta.RunAsync();

        Console.WriteLine(result.Mode switch
        {
            "noop"  => $"Nothing to index — HEAD unchanged ({result.ElapsedMs}ms) [noop]",
            "full"  => $"Indexed {result.FilesReindexed} files, {result.SymbolsAdded} symbols ({result.ElapsedMs}ms) [full]",
            "delta" => $"{result.FilesChanged} changed, {result.FilesReindexed} re-indexed, {result.SymbolsAdded} symbols added ({result.ElapsedMs}ms) [delta]",
            _       => $"Done ({result.ElapsedMs}ms)"
        });
    }
}, repoArgument, dbDirOption, storeLocalOption, forceFull);
```

### 2.2 — Storage Mode Integration Test

```powershell
# Mode 1: --store-local
.\publish\CodeGraphMcp.exe index D:\Projects\C#\repositorydb --store-local
# DB at: D:\Projects\C#\repositorydb\.agent-index\code_graph.db

# Mode 2: --db-dir
.\publish\CodeGraphMcp.exe index D:\Projects\C#\repositorydb --db-dir D:\Temp\CodeGraph
# DB at: D:\Temp\CodeGraph\<hash>.db

# Mode 3: Default LOCALAPPDATA
.\publish\CodeGraphMcp.exe index D:\Projects\C#\repositorydb
# DB at: %LOCALAPPDATA%\CodeGraph\dbs\<hash>.db
```

### 2.3 — GIT-02 Stability Gate

All three storage modes produce a working DB. Verified by running `query CSharpParser` against each:

```powershell
.\publish\CodeGraphMcp.exe query CSharpParser --store-local
# Expected: [Class] CSharpParser | src\CodeGraphMcp\Parser\CSharpParser.cs:7-192
```

---

## GIT-03 — Live In-Memory Dirty File Buffer (20 hours)

### 3.1 — Concept

The `DirtyFileBuffer` is a transient in-memory overlay. When the MCP `query` command (and later, MCP tool calls) asks for a symbol, the resolution order is:

```
DirtyFileBuffer (hot, in-memory)  →  SQLite (cold, committed)
```

This means a developer can open a file, add a new method, and immediately ask the AI "what does `MyNewMethod` do?" without needing to commit or re-index.

### 3.2 — `DirtyFileBuffer.cs`

```csharp
// Git/DirtyFileBuffer.cs
using CodeGraphMcp.Models;
using CodeGraphMcp.Parser;

namespace CodeGraphMcp.Git;

/// <summary>
/// Parses uncommitted (dirty) files on demand and caches the results in memory.
/// Results overlay the SQLite DB — dirty symbols take precedence.
/// Thread-safe for concurrent read access; refresh is single-threaded.
/// </summary>
public sealed class DirtyFileBuffer(string repoPath, IReadOnlyList<ILanguageParser> parsers)
{
    // relPath (forward-slash normalised) → parsed symbols
    private volatile IReadOnlyDictionary<string, IReadOnlyList<Symbol>> _cache
        = new Dictionary<string, IReadOnlyList<Symbol>>();

    private readonly Dictionary<string, ILanguageParser> _extensionMap = BuildMap(parsers);
    private readonly object _refreshLock = new();

    /// <summary>
    /// Refreshes the buffer by querying git status and re-parsing all dirty files.
    /// Safe to call from any thread; only one refresh runs at a time.
    /// </summary>
    public void Refresh()
    {
        lock (_refreshLock)
        {
            var dirtyFiles = GitHelpers.GetDirtyFiles(repoPath);
            var newCache   = new Dictionary<string, IReadOnlyList<Symbol>>(
                StringComparer.OrdinalIgnoreCase);

            foreach (var relPath in dirtyFiles)
            {
                var ext = Path.GetExtension(relPath);
                if (!_extensionMap.TryGetValue(ext, out var parser)) continue;

                var fullPath = Path.Combine(repoPath, relPath);
                if (!File.Exists(fullPath)) continue;

                try
                {
                    var source    = File.ReadAllText(fullPath);
                    var normPath  = relPath.Replace('\\', '/');
                    var symbols   = parser.Parse(normPath, source).ToList();
                    newCache[normPath] = symbols;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[DirtyBuffer] Warning: failed to parse {relPath}: {ex.Message}");
                }
            }

            _cache = newCache;
        }
    }

    /// <summary>
    /// Returns live symbols for the given relative file path if the file is dirty,
    /// or null if the file has no dirty state (fall back to SQLite).
    /// </summary>
    public IReadOnlyList<Symbol>? GetDirtySymbols(string relFilePath)
    {
        var norm = relFilePath.Replace('\\', '/');
        return _cache.TryGetValue(norm, out var syms) ? syms : null;
    }

    /// <summary>
    /// Searches all dirty symbols by name, optionally filtered by language.
    /// Returns null if the buffer has no dirty state for any file.
    /// </summary>
    public IReadOnlyList<Symbol>? FindByName(string symbolName, string? language = null)
    {
        var allDirty = _cache.Values
            .SelectMany(s => s)
            .Where(s => string.Equals(s.SymbolName, symbolName, StringComparison.OrdinalIgnoreCase))
            .Where(s => language is null || string.Equals(s.Language, language, StringComparison.OrdinalIgnoreCase))
            .ToList();

        return allDirty.Count > 0 ? allDirty : null;
    }

    /// <summary>Total number of files currently tracked as dirty.</summary>
    public int DirtyFileCount => _cache.Count;

    private static Dictionary<string, ILanguageParser> BuildMap(IReadOnlyList<ILanguageParser> parsers)
    {
        var map = new Dictionary<string, ILanguageParser>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in parsers)
            foreach (var ext in p.FileExtensions)
                map[ext] = p;
        return map;
    }
}
```

### 3.3 — Wiring the Buffer into `query` Command

Update the `query` command handler in [Program.cs](file:///D:/Projects/C%23/repositorydb/src/CodeGraphMcp/Program.cs) to check the dirty buffer first:

```csharp
queryCommand.SetHandler((symbol, dbDir, storeLocal) =>
{
    var dbPath = StorageConfig.ResolveDbPath(dbDir?.FullName, storeLocal, Directory.GetCurrentDirectory());
    var repoRoot = FindGitRoot(Directory.GetCurrentDirectory()) ?? Directory.GetCurrentDirectory();
    using var db = new CodeGraphDb(dbPath);
    db.Migrate();

    // 1. Check dirty buffer first
    var parsers = GetParsers();
    var buffer  = new DirtyFileBuffer(repoRoot, parsers);
    buffer.Refresh();

    var dirtyResults = buffer.FindByName(symbol);
    if (dirtyResults is not null && dirtyResults.Count > 0)
    {
        Console.WriteLine($"[LIVE - uncommitted]");
        foreach (var sym in dirtyResults)
            Console.WriteLine($"  [{sym.SymbolType}] {sym.SymbolName} | {sym.FilePath}:{sym.StartLine}-{sym.EndLine}");
        return;
    }

    // 2. Fall back to SQLite
    var results = db.Symbols.FindByName(symbol);
    foreach (var sym in results)
        Console.WriteLine($"[{sym.SymbolType}] {sym.SymbolName} | {sym.FilePath}:{sym.StartLine}-{sym.EndLine}");

}, symbolArgument, dbDirOption, storeLocalOption);

// Helper: walk up to find git root
static string? FindGitRoot(string start)
{
    var dir = new DirectoryInfo(start);
    while (dir is not null)
    {
        if (Directory.Exists(Path.Combine(dir.FullName, ".git")))
            return dir.FullName;
        dir = dir.Parent;
    }
    return null;
}
```

### 3.4 — `CountByFile` Addition to `SymbolQueries.cs`

```csharp
/// <summary>Returns the count of symbols for a file path (used by delta engine for pre-delete accounting).</summary>
public int CountByFile(string relFilePath)
{
    using var cmd = _connection.CreateCommand();
    cmd.CommandText = "SELECT COUNT(*) FROM symbols WHERE file_path = @path;";
    cmd.Parameters.AddWithValue("@path", relFilePath);
    return (int)(long)(cmd.ExecuteScalar() ?? 0L);
}
```

### 3.5 — GIT-03 Stability Gate

```powershell
# 1. Edit a .cs file WITHOUT committing
#    (e.g. add a new method to CSharpParser.cs)

# 2. Query for the new symbol
.\publish\CodeGraphMcp.exe query MyNewMethod --store-local
# Expected output:
#   [LIVE - uncommitted]
#     [Method] MyNewMethod | src/CodeGraphMcp/Parser/CSharpParser.cs:X-Y

# 3. Commit the file and re-index
git -C D:\Projects\C#\repositorydb add . && git commit -m "add MyNewMethod"
.\publish\CodeGraphMcp.exe index D:\Projects\C#\repositorydb --store-local

# 4. Query again — should come from DB now (no [LIVE] prefix)
.\publish\CodeGraphMcp.exe query MyNewMethod --store-local
# Expected output:
#   [Method] MyNewMethod | src/CodeGraphMcp/Parser/CSharpParser.cs:X-Y
```

---

## Unit Tests (Phase 2 Additions)

### `DeltaEngineTests.cs`

Create a temporary git repo using `git init`, make commits, and verify delta behaviour:

```csharp
// tests/CodeGraphMcp.Tests/Git/DeltaEngineTests.cs
namespace CodeGraphMcp.Tests.Git;

public sealed class DeltaEngineTests : IDisposable
{
    private readonly string _tempRepo;

    public DeltaEngineTests()
    {
        _tempRepo = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempRepo);
        RunGit(_tempRepo, "init");
        RunGit(_tempRepo, "config user.email test@test.com");
        RunGit(_tempRepo, "config user.name Test");
    }

    [Fact]
    public async Task RunAsync_FirstRun_ReturnsFull()
    {
        // Write a file, commit it
        WriteAndCommit("Foo.cs", "namespace A; public class Foo {}");

        using var db    = CreateInMemoryDb();
        var engine      = new DeltaEngine(_tempRepo, db, [new CSharpParser()]);
        var result      = await engine.RunAsync();

        Assert.Equal("full", result.Mode);
        Assert.Equal(1, result.FilesReindexed);
        Assert.True(result.SymbolsAdded > 0);
    }

    [Fact]
    public async Task RunAsync_NoChanges_ReturnsNoop()
    {
        WriteAndCommit("Foo.cs", "namespace A; public class Foo {}");

        using var db = CreateInMemoryDb();
        var engine   = new DeltaEngine(_tempRepo, db, [new CSharpParser()]);
        await engine.RunAsync(); // First run (full)

        var result = await engine.RunAsync(); // Second run (same HEAD)
        Assert.Equal("noop", result.Mode);
    }

    [Fact]
    public async Task RunAsync_FileChanged_ReturnsDelta()
    {
        WriteAndCommit("Foo.cs", "namespace A; public class Foo {}");

        using var db = CreateInMemoryDb();
        var engine   = new DeltaEngine(_tempRepo, db, [new CSharpParser()]);
        await engine.RunAsync(); // Full

        WriteAndCommit("Foo.cs", "namespace A; public class Foo { public void Bar() {} }");
        var result = await engine.RunAsync(); // Delta
        Assert.Equal("delta", result.Mode);
        Assert.Equal(1, result.FilesChanged);
    }

    [Fact]
    public async Task RunAsync_FileDeleted_SymbolsRemoved()
    {
        WriteAndCommit("Foo.cs", "namespace A; public class Foo {}");

        using var db = CreateInMemoryDb();
        var engine   = new DeltaEngine(_tempRepo, db, [new CSharpParser()]);
        await engine.RunAsync(); // Full

        File.Delete(Path.Combine(_tempRepo, "Foo.cs"));
        RunGit(_tempRepo, "add -A");
        RunGit(_tempRepo, "commit -m del");

        var result = await engine.RunAsync(); // Delta
        Assert.Equal(0, db.Symbols.FindByName("Foo").Count);
    }

    // --- Helpers ---
    private void WriteAndCommit(string name, string content)
    {
        File.WriteAllText(Path.Combine(_tempRepo, name), content);
        RunGit(_tempRepo, "add -A");
        RunGit(_tempRepo, $"commit -m \"{name}\"");
    }

    private CodeGraphDb CreateInMemoryDb()
    {
        // Use :memory: or a per-test temp file
        var db = new CodeGraphDb(Path.Combine(_tempRepo, "test.db"));
        db.Migrate();
        return db;
    }

    private static void RunGit(string cwd, string args)
    {
        var psi = new ProcessStartInfo("git", args)
        {
            WorkingDirectory      = cwd,
            RedirectStandardOutput = true,
            RedirectStandardError  = true,
            UseShellExecute        = false,
            CreateNoWindow         = true
        };
        using var p = Process.Start(psi)!;
        p.WaitForExit();
    }

    public void Dispose() => Directory.Delete(_tempRepo, recursive: true);
}
```

### `DirtyFileBufferTests.cs`

```csharp
// tests/CodeGraphMcp.Tests/Git/DirtyFileBufferTests.cs
namespace CodeGraphMcp.Tests.Git;

public sealed class DirtyFileBufferTests : IDisposable
{
    private readonly string _tempRepo;

    public DirtyFileBufferTests()
    {
        _tempRepo = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempRepo);
        RunGit(_tempRepo, "init");
        RunGit(_tempRepo, "config user.email test@test.com");
        RunGit(_tempRepo, "config user.name Test");
    }

    [Fact]
    public void Refresh_UncommittedFile_ReturnsSymbols()
    {
        File.WriteAllText(
            Path.Combine(_tempRepo, "Live.cs"),
            "namespace A; public class LiveClass { public void LiveMethod() {} }");

        var buffer = new DirtyFileBuffer(_tempRepo, [new CSharpParser()]);
        buffer.Refresh();

        var result = buffer.FindByName("LiveClass");
        Assert.NotNull(result);
        Assert.Single(result);
    }

    [Fact]
    public void Refresh_CommittedFile_NotTracked()
    {
        File.WriteAllText(Path.Combine(_tempRepo, "Committed.cs"), "namespace A; public class Committed {}");
        RunGit(_tempRepo, "add -A");
        RunGit(_tempRepo, "commit -m init");

        var buffer = new DirtyFileBuffer(_tempRepo, [new CSharpParser()]);
        buffer.Refresh();

        Assert.Equal(0, buffer.DirtyFileCount);
    }

    [Fact]
    public void FindByName_ReturnsNull_WhenNoDirtyFiles()
    {
        var buffer = new DirtyFileBuffer(_tempRepo, [new CSharpParser()]);
        buffer.Refresh();
        Assert.Null(buffer.FindByName("Anything"));
    }

    private static void RunGit(string cwd, string args)
    {
        var psi = new ProcessStartInfo("git", args)
        {
            WorkingDirectory      = cwd,
            RedirectStandardOutput = true,
            RedirectStandardError  = true,
            UseShellExecute        = false,
            CreateNoWindow         = true
        };
        using var p = Process.Start(psi)!;
        p.WaitForExit();
    }

    public void Dispose() => Directory.Delete(_tempRepo, recursive: true);
}
```

---

## NuGet Package Summary (Phase 2)

| Package | Version | Used In | AOT Risk |
| :--- | :--- | :--- | :--- |
| *(no new packages — git via shell)* | — | — | None |

> [!TIP]
> Because we shell out to `git` rather than using `LibGit2Sharp`, Phase 2 adds **zero new NuGet packages**. This keeps the AOT publish clean and avoids bundling the `git2-*.dll` native library. The only prerequisite is that `git` is on `PATH` (safe to assume on any developer machine).

---

## Schema Changes (Phase 2)

None. The `index_state` table was created in Phase 1's `MigrationRunner.cs` and already contains all required columns. Phase 2 merely starts writing to it.

> [!NOTE]
> If a future need arises (e.g. tracking per-file `indexed_at` timestamps or dirty file counts per file), a `M002_...` migration can be added to `MigrationRunner.cs`. For now, no schema changes are needed.

---

## Execution Order

```
1. Add git shell helpers     → Git/GitHelpers.cs
2. Add IndexStateQueries     → Db/Queries/IndexStateQueries.cs
3. Wire IndexState into DB   → Db/CodeGraphDb.cs (add accessor)
4. Add SymbolQueries methods → DeleteByFile, CountByFile, UpsertFileDelta
5. Implement DeltaEngine     → Git/DeltaEngine.cs
6. Implement DirtyFileBuffer → Git/DirtyFileBuffer.cs
7. Update Program.cs         → --force-full flag, query dirty buffer, FindGitRoot helper
8. Write unit tests          → DeltaEngineTests.cs, DirtyFileBufferTests.cs
9. Run dotnet test           → all pass
10. Publish AOT              → verify no new trim errors
11. Manual smoke test        → noop, delta, dirty buffer scenarios
```

---

## Risk Register (Phase 2-Specific)

| Risk | Likelihood | Impact | Mitigation |
| :--- | :---: | :---: | :--- |
| `git` not on PATH on target machine | Low | High | Detect at startup and degrade gracefully to full-scan with a warning |
| Detached HEAD causes `rev-parse HEAD` to fail | Low | Medium | `GitHelpers` returns null → triggers full scan (safe fallback) |
| Very large diffs (e.g. mass rename/reformat) slow delta | Low | Low | Delta becomes a full re-scan of changed files — still faster than indexing unchanged files |
| Concurrent `index` runs corrupt `index_state` | Low | Medium | Wrap `SetIndexState` write in a SQLite transaction; `index` is CLI-only for now |
| Symbol count drift (delete+insert mismatch) | Medium | Low | `UpsertFileDelta` wraps delete+insert in a single transaction — atomic or nothing |

---

## Phase 2 Exit Criteria (Checklist)

- [ ] `dotnet test` — all tests pass (≥ 7 existing + new Git tests)
- [ ] `dotnet publish -r win-x64 -p:PublishAot=true` — succeeds, no new fatal trim errors
- [ ] `index` with no git changes: **< 50ms**, output says `[noop]`
- [ ] `index` after one file changed: only that file re-parsed, output says `[delta]`
- [ ] `index` on first run / `--force-full`: full scan, output says `[full]`
- [ ] `query` on uncommitted symbol: shows `[LIVE - uncommitted]` prefix
- [ ] All three storage modes (`--store-local`, `--db-dir`, default) work correctly
- [ ] Detached HEAD, empty repo, non-git directory all handled without exceptions
- [ ] `index_state` table populated with correct branch + commit hash after each run
