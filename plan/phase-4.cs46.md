# Phase 4 — Reference Graph, File Watcher & CLI Utilities
## Implementation Plan — REF-01 · WATCH-01 · CLI-01

**Depends On:** Phase 3 (✅ Complete — MCP server + 5 tools, 15/15 tests, AOT clean)  
**Status:** Ready for Review

---

## Objective

Complete the knowledge graph by populating the empty `symbol_references` table with real cross-file call/usage edges extracted during AST parsing, add a `FileSystemWatcher` so the MCP server automatically updates its dirty buffer on file saves, and ship three new CLI utility commands (`status`, `watch`, `clean`).

---

## Problem Statement

### 1. `find_symbol_callers` Returns Empty Results
The `symbol_references` table (schema exists since Migration 001) has **zero rows**. The `FindCallersTool` runs a SQL join against it and always returns an empty list with a "not yet populated" note. This is the single most valuable query for AI agents — "who calls this method?" — and it produces nothing.

**Root cause:** Neither `IndexOrchestrator` nor `DeltaEngine` extract invocation references from the AST. They only extract **symbol definitions** (classes, methods, properties, interfaces). The tree-sitter AST contains `invocation_expression`, `member_access_expression`, `call_expression` etc. but nobody walks those nodes.

### 2. Dirty Buffer Goes Stale During MCP Sessions
`DirtyFileBuffer.Refresh()` runs once at startup (eager pre-warm). During a long MCP session, files are saved, created, and deleted — but the buffer never updates. The `get_symbol_definition` and `search_codebase_graph` tools serve increasingly stale "live" data as the session progresses.

### 3. No Utility Commands
There is no `status` command to inspect the index state. There is no `clean` command to wipe the database. There is no `watch` command to run continuous delta re-indexing. These are standard ergonomic expectations for a CLI tool.

---

## Available Infrastructure (Already Built)

| Class | Location | Relevant APIs |
| :--- | :--- | :--- |
| `ILanguageParser` | `Parser/ILanguageParser.cs` | `Parse(filePath, source) → IEnumerable<Symbol>` — **definition extraction only** |
| `ReferenceQueries` | `Db/Queries/ReferenceQueries.cs` | `FindCallers(symbolName)`, `Count()` — queries exist, table is empty |
| `MigrationRunner` | `Db/MigrationRunner.cs` | Hand-written SQL migrations, currently at version 3 |
| `DirtyFileBuffer` | `Git/DirtyFileBuffer.cs` | `UpdateFile(relPath)`, `Invalidate(relPath)` — **already prepared for FileWatcher events** |
| `DeltaEngine` | `Git/DeltaEngine.cs` | `RunAsync()` — full/delta/noop modes |
| `IndexOrchestrator` | `Indexer/IndexOrchestrator.cs` | `IndexAsync()` — full parse with `Parallel.ForEachAsync` |
| `McpServer` | `Mcp/McpServer.cs` | DI host, `DirtyFileBuffer` registered as singleton |
| `Program.cs` | Root | `index`, `query`, `serve` commands |
| `SymbolType` enum | `Models/SymbolType.cs` | Class, Interface, Struct, Enum, Method, Property, Field, Function, TypeAlias, Variable |

**Key insight:** `DirtyFileBuffer` already has `UpdateFile(relPath)` and `Invalidate(relPath)` methods prepared for exactly this scenario. The `FileSystemWatcher` just needs to call them.

---

## Files to Create / Modify

| # | File | Action | Work Unit |
| :--- | :--- | :--- | :--- |
| 1 | `Parser/ILanguageParser.cs` | **MODIFY** — add `ParseReferences` method | REF-01 |
| 2 | `Parser/CSharpParser.cs` | **MODIFY** — implement `ParseReferences` | REF-01 |
| 3 | `Parser/TypeScriptParser.cs` | **MODIFY** — implement `ParseReferences` | REF-01 |
| 4 | `Models/SymbolReference.cs` | **NEW** — reference model record | REF-01 |
| 5 | `Db/Queries/ReferenceQueries.cs` | **MODIFY** — add `UpsertFileReferences`, `DeleteByFile` | REF-01 |
| 6 | `Indexer/IndexOrchestrator.cs` | **MODIFY** — call `ParseReferences` after symbol extraction | REF-01 |
| 7 | `Git/DeltaEngine.cs` | **MODIFY** — call `ParseReferences` during delta re-index | REF-01 |
| 8 | `Git/FileWatcher.cs` | **NEW** — `FileSystemWatcher` wrapper | WATCH-01 |
| 9 | `Mcp/McpServer.cs` | **MODIFY** — register and start `FileWatcher` | WATCH-01 |
| 10 | `Program.cs` | **MODIFY** — add `status`, `watch`, `clean` commands | CLI-01 |
| 11 | `Db/MigrationRunner.cs` | **MODIFY** — add Migration 004 (reference index on file_path) | REF-01 |

---

## REF-01 — Reference Extraction Pipeline

### Purpose
Walk the tree-sitter AST a second time (same parse, same tree) to extract **invocation references** — method calls, type usages, property accesses — and write them as edges in `symbol_references`.

### New Model: `Models/SymbolReference.cs`

```csharp
namespace CodeGraphMcp.Models;

/// <summary>
/// A single reference edge: "caller at line N references target by name".
/// Caller and target are resolved to symbol IDs during DB insertion.
/// </summary>
public sealed record SymbolReference(
    string CallerSymbolName,
    string TargetSymbolName,
    string ReferenceType,     // "Call", "TypeUsage", "Inheritance", "Import"
    string FilePath,
    int LineNumber);
```

### Parser Interface Extension: `ILanguageParser.cs`

Add a default-implemented method so existing parsers don't break:

```csharp
/// <summary>
/// Extract cross-symbol references (method calls, type usages, inheritance).
/// Default returns empty — opt-in per language.
/// </summary>
IEnumerable<SymbolReference> ParseReferences(string filePath, string source) => [];
```

Using a default interface method means `HtmlParser` and `PowerShellParser` (which don't have meaningful cross-file references) work unchanged.

### C# Reference Extraction: `CSharpParser.ParseReferences`

Walk the same tree-sitter AST. Target node types:

| Tree-sitter Node Type | Reference Type | What It Captures |
| :--- | :--- | :--- |
| `invocation_expression` → child `member_access_expression` → `name` field | `Call` | `service.GetUser()` → target = `GetUser` |
| `invocation_expression` → child `identifier` | `Call` | `DoWork()` → target = `DoWork` |
| `object_creation_expression` → `type` field | `TypeUsage` | `new UserService()` → target = `UserService` |
| `base_list` → `identifier` children | `Inheritance` | `class Foo : Bar` → target = `Bar` |
| `type_argument_list` → `identifier` children | `TypeUsage` | `List<User>` → target = `User` |

**Caller resolution strategy:**
- Track the enclosing method/property name while walking.
- If the reference is found inside `method_declaration` → caller = that method's name.
- If found at class level (field initialiser) → caller = class name.
- If found at top level → caller = file name (as fallback).

**Expected output per file:** 10–50 references for a typical 200-line C# class.

```csharp
public IEnumerable<SymbolReference> ParseReferences(string filePath, string source)
{
    var refs = new List<SymbolReference>();
    try
    {
        using var parser = new TreeSitter.Parser();
        parser.Language = CSharpLanguage;
        using var tree = parser.Parse(source);
        if (tree == null || tree.RootNode.Id == IntPtr.Zero) return refs;

        WalkForReferences(tree.RootNode, filePath, refs, enclosingSymbol: null);
    }
    catch { }
    return refs;
}

private static void WalkForReferences(
    Node node, string filePath, List<SymbolReference> refs, string? enclosingSymbol)
{
    if (node.Id == IntPtr.Zero) return;
    var type = node.Type;

    // Track enclosing scope
    if (type is "method_declaration" or "property_declaration"
             or "constructor_declaration")
    {
        var nameNode = GetNameNode(node);
        if (nameNode.Id != IntPtr.Zero)
            enclosingSymbol = nameNode.Text;
    }
    else if (type is "class_declaration" or "struct_declaration"
                  or "interface_declaration")
    {
        var nameNode = GetNameNode(node);
        if (nameNode.Id != IntPtr.Zero)
            enclosingSymbol = nameNode.Text;
    }

    // Extract references
    if (type == "invocation_expression")
    {
        var targetName = ExtractInvocationTarget(node);
        if (targetName is not null)
        {
            refs.Add(new SymbolReference(
                enclosingSymbol ?? Path.GetFileNameWithoutExtension(filePath),
                targetName, "Call", filePath,
                (int)node.StartPosition.Row + 1));
        }
    }
    else if (type == "object_creation_expression")
    {
        var typeNode = GetFieldByName(node, "type");
        if (typeNode.Id != IntPtr.Zero)
        {
            var typeName = ExtractTypeName(typeNode);
            if (typeName is not null)
            {
                refs.Add(new SymbolReference(
                    enclosingSymbol ?? Path.GetFileNameWithoutExtension(filePath),
                    typeName, "TypeUsage", filePath,
                    (int)node.StartPosition.Row + 1));
            }
        }
    }
    else if (type == "base_list")
    {
        foreach (var child in node.NamedChildren)
        {
            var baseName = ExtractTypeName(child);
            if (baseName is not null)
            {
                refs.Add(new SymbolReference(
                    enclosingSymbol ?? Path.GetFileNameWithoutExtension(filePath),
                    baseName, "Inheritance", filePath,
                    (int)child.StartPosition.Row + 1));
            }
        }
    }

    foreach (var child in node.Children)
        WalkForReferences(child, filePath, refs, enclosingSymbol);
}
```

**Helper methods:**
```csharp
private static string? ExtractInvocationTarget(Node invocation)
{
    // invocation_expression → first child is the "function" being called
    foreach (var child in invocation.NamedChildren)
    {
        if (child.Type == "member_access_expression")
        {
            // service.GetUser() → we want "GetUser"
            var nameField = GetFieldByName(child, "name");
            return nameField.Id != IntPtr.Zero ? nameField.Text : null;
        }
        if (child.Type == "identifier")
        {
            return child.Text;  // simple: DoWork()
        }
    }
    return null;
}

private static string? ExtractTypeName(Node typeNode)
{
    if (typeNode.Type == "identifier") return typeNode.Text;
    if (typeNode.Type == "qualified_name" || typeNode.Type == "generic_name")
    {
        var nameNode = GetNameNode(typeNode);
        return nameNode.Id != IntPtr.Zero ? nameNode.Text : typeNode.Text;
    }
    // For generic_name, strip type args: List<User> → List
    foreach (var child in typeNode.NamedChildren)
    {
        if (child.Type == "identifier") return child.Text;
    }
    return null;
}

private static Node GetFieldByName(Node node, string fieldName)
{
    try
    {
        foreach (var field in node.Fields)
        {
            if (field.Key == fieldName && field.Value.Id != IntPtr.Zero)
                return field.Value;
        }
    }
    catch { }
    return default;
}
```

### TypeScript Reference Extraction: `TypeScriptParser.ParseReferences`

Same pattern, different node types:

| Tree-sitter Node Type | Reference Type | Example |
| :--- | :--- | :--- |
| `call_expression` → `function` field → `member_expression` → `property` | `Call` | `this.userService.getUser()` → `getUser` |
| `call_expression` → `function` field → `identifier` | `Call` | `doWork()` → `doWork` |
| `import_statement` → `import_clause` → `named_imports` | `Import` | `import { UserService } from '...'` |
| `new_expression` → `constructor` → `identifier` | `TypeUsage` | `new UserService()` |

### Database Changes: `ReferenceQueries.cs` Additions

```csharp
/// <summary>Delete all references originating from a given file.</summary>
public void DeleteByFile(string filePath)
{
    using var cmd = connection.CreateCommand();
    cmd.CommandText = "DELETE FROM symbol_references WHERE file_path = @filePath;";
    cmd.Parameters.AddWithValue("@filePath", filePath);
    cmd.ExecuteNonQuery();
}

/// <summary>
/// Insert references for a file. Resolves caller/target names to symbol IDs
/// via lookup. Unresolved references are silently skipped (the target symbol
/// may be in an un-indexed dependency or external library).
/// </summary>
public void UpsertFileReferences(
    string filePath,
    IReadOnlyList<SymbolReference> references,
    Func<string, long?> resolveSymbolId)
{
    DeleteByFile(filePath);

    using var tx = connection.BeginTransaction();
    using var insert = connection.CreateCommand();
    insert.CommandText = """
        INSERT INTO symbol_references
            (caller_symbol_id, target_symbol_id, reference_type, file_path, line_number)
        VALUES (@callerId, @targetId, @refType, @filePath, @lineNumber);
        """;
    insert.Transaction = tx;

    var pCallerId   = insert.Parameters.Add("@callerId",   SqliteType.Integer);
    var pTargetId   = insert.Parameters.Add("@targetId",   SqliteType.Integer);
    var pRefType    = insert.Parameters.Add("@refType",    SqliteType.Text);
    var pFilePath   = insert.Parameters.Add("@filePath",   SqliteType.Text);
    var pLineNumber = insert.Parameters.Add("@lineNumber", SqliteType.Integer);

    foreach (var r in references)
    {
        var callerId = resolveSymbolId(r.CallerSymbolName);
        var targetId = resolveSymbolId(r.TargetSymbolName);
        if (callerId is null || targetId is null) continue;

        pCallerId.Value   = callerId.Value;
        pTargetId.Value   = targetId.Value;
        pRefType.Value    = r.ReferenceType;
        pFilePath.Value   = r.FilePath;
        pLineNumber.Value = r.LineNumber;
        insert.ExecuteNonQuery();
    }
    tx.Commit();
}
```

### Symbol ID Resolution Helper: `SymbolQueries.cs` Addition

```csharp
/// <summary>Resolve a symbol name to its ID. Returns null if not found.</summary>
public long? ResolveId(string symbolName)
{
    using var cmd = connection.CreateCommand();
    cmd.CommandText = """
        SELECT id FROM symbols
        WHERE symbol_name = @name COLLATE NOCASE
        LIMIT 1;
        """;
    cmd.Parameters.AddWithValue("@name", symbolName);
    var result = cmd.ExecuteScalar();
    return result is DBNull or null ? null : (long?)Convert.ToInt64(result);
}
```

### Migration 004: Reference File Path Index

```csharp
private const string M004_ReferenceFilePathIndex = """
    CREATE INDEX IF NOT EXISTS idx_refs_file ON symbol_references(file_path);
    """;
```

This enables fast `DELETE FROM symbol_references WHERE file_path = ?` during delta re-indexing.

### Integration into IndexOrchestrator

After the existing `db.Symbols.UpsertFile(relPath, symbols)` call (line 57):

```csharp
// Extract and store cross-symbol references
var refs = parser.ParseReferences(relPath, source).ToList();
if (refs.Count > 0)
{
    db.References.UpsertFileReferences(relPath, refs, name => db.Symbols.ResolveId(name));
}
```

### Integration into DeltaEngine

After the existing `db.Symbols.UpsertFileDelta(normPath, symbols)` call (line 100):

```csharp
var refs = parser.ParseReferences(normPath, source).ToList();
if (refs.Count > 0)
{
    db.References.UpsertFileReferences(normPath, refs, name => db.Symbols.ResolveId(name));
}
```

And in the delete path (line 86), add:
```csharp
db.References.DeleteByFile(normPath);
```

---

## WATCH-01 — FileSystemWatcher for Live MCP Sessions

### Purpose
Automatically update `DirtyFileBuffer` when files change on disk during a live MCP session. The buffer already has `UpdateFile(relPath)` and `Invalidate(relPath)` methods designed for this.

### New File: `Git/FileWatcher.cs`

```csharp
namespace CodeGraphMcp.Git;

/// <summary>
/// Watches the repository directory for file changes and updates
/// the DirtyFileBuffer automatically. Designed for long-running MCP sessions.
/// </summary>
public sealed class FileWatcher : IDisposable
{
    private readonly FileSystemWatcher _watcher;
    private readonly DirtyFileBuffer _buffer;
    private readonly string _repoPath;
    private readonly HashSet<string> _supportedExtensions;

    // Debounce: coalesce rapid saves (IDE auto-save, format-on-save)
    private readonly Dictionary<string, CancellationTokenSource> _pending = new();
    private readonly object _debounceLock = new();
    private const int DebounceMs = 300;

    private static readonly HashSet<string> IgnoredDirs = new(StringComparer.OrdinalIgnoreCase)
    {
        ".git", ".agent-index", "bin", "obj", "publish",
        "node_modules", "dist", ".next", "coverage", ".vs"
    };

    public FileWatcher(
        string repoPath,
        DirtyFileBuffer buffer,
        IReadOnlyList<ILanguageParser> parsers)
    {
        _repoPath = repoPath;
        _buffer   = buffer;
        _supportedExtensions = parsers
            .SelectMany(p => p.FileExtensions)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        _watcher = new FileSystemWatcher(repoPath)
        {
            IncludeSubdirectories = true,
            NotifyFilter = NotifyFilters.FileName
                         | NotifyFilters.LastWrite
                         | NotifyFilters.CreationTime,
            EnableRaisingEvents = false  // started explicitly via Start()
        };

        _watcher.Changed += OnChanged;
        _watcher.Created += OnChanged;
        _watcher.Deleted += OnDeleted;
        _watcher.Renamed += OnRenamed;
    }

    public void Start() => _watcher.EnableRaisingEvents = true;
    public void Stop()  => _watcher.EnableRaisingEvents = false;

    private void OnChanged(object sender, FileSystemEventArgs e)
    {
        if (!ShouldProcess(e.FullPath)) return;
        DebouncedUpdate(e.FullPath);
    }

    private void OnDeleted(object sender, FileSystemEventArgs e)
    {
        if (!ShouldProcess(e.FullPath)) return;
        var relPath = Path.GetRelativePath(_repoPath, e.FullPath);
        _buffer.Invalidate(relPath);
    }

    private void OnRenamed(object sender, RenamedEventArgs e)
    {
        if (ShouldProcess(e.OldFullPath))
        {
            var oldRel = Path.GetRelativePath(_repoPath, e.OldFullPath);
            _buffer.Invalidate(oldRel);
        }
        if (ShouldProcess(e.FullPath))
        {
            DebouncedUpdate(e.FullPath);
        }
    }

    private void DebouncedUpdate(string fullPath)
    {
        lock (_debounceLock)
        {
            if (_pending.TryGetValue(fullPath, out var existing))
                existing.Cancel();

            var cts = new CancellationTokenSource();
            _pending[fullPath] = cts;

            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(DebounceMs, cts.Token);
                    var relPath = Path.GetRelativePath(_repoPath, fullPath);
                    _buffer.UpdateFile(relPath);
                }
                catch (TaskCanceledException) { }
                finally
                {
                    lock (_debounceLock) { _pending.Remove(fullPath); }
                }
            }, cts.Token);
        }
    }

    private bool ShouldProcess(string fullPath)
    {
        var ext = Path.GetExtension(fullPath);
        if (!_supportedExtensions.Contains(ext)) return false;

        // Check ignored directories
        var relPath = Path.GetRelativePath(_repoPath, fullPath);
        var parts   = relPath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return !parts.Any(p => IgnoredDirs.Contains(p));
    }

    public void Dispose()
    {
        Stop();
        _watcher.Dispose();
        lock (_debounceLock)
        {
            foreach (var cts in _pending.Values) cts.Cancel();
            _pending.Clear();
        }
    }
}
```

### Integration into `McpServer.cs`

After the existing `DirtyFileBuffer` pre-warm line:

```csharp
// Start file watcher for automatic dirty buffer updates during the session
var watcher = new FileWatcher(
    repoPath,
    host.Services.GetRequiredService<DirtyFileBuffer>(),
    host.Services.GetRequiredService<IReadOnlyList<ILanguageParser>>());
watcher.Start();
```

**Lifecycle:** The `FileWatcher` starts after the host is built but before `RunAsync`. It runs alongside the MCP server. When stdin closes and the host shuts down, the `FileWatcher` is garbage-collected (or we can hook `IHostApplicationLifetime.ApplicationStopping` to call `watcher.Dispose()`).

### AOT Safety
`FileSystemWatcher` is in `System.IO` — fully AOT-compatible, no reflection, no trim warnings.

---

## CLI-01 — Utility Commands

### `status` Command

Displays the current index state: last indexed commit, symbol count, reference count, dirty file count.

```csharp
var statusCommand = new Command("status", "Show the current index state for a repository");
statusCommand.AddArgument(repoArgument);
statusCommand.AddOption(dbDirOption);
statusCommand.AddOption(storeLocalOption);
statusCommand.SetHandler((repo, dbDir, storeLocal) =>
{
    var dbPath = StorageConfig.ResolveDbPath(dbDir?.FullName, storeLocal, repo.FullName);
    using var db = new CodeGraphDb(dbPath);
    db.Migrate();

    var lastHash = db.IndexState.GetLastCommitHash();
    var headHash = GitHelpers.GetHeadCommitHash(repo.FullName) ?? "unknown";
    var branch   = GitHelpers.GetCurrentBranch(repo.FullName) ?? "unknown";
    var symbolCount = db.Symbols.CountPerFile().Values.Sum();
    var fileCount   = db.Symbols.CountPerFile().Count;
    var refCount    = db.References.Count();

    Console.WriteLine($"Repository:       {repo.FullName}");
    Console.WriteLine($"Database:         {dbPath}");
    Console.WriteLine($"Branch:           {branch}");
    Console.WriteLine($"HEAD:             {headHash}");
    Console.WriteLine($"Last Indexed:     {lastHash ?? "never"}");
    Console.WriteLine($"Needs Re-index:   {(headHash != lastHash ? "yes" : "no")}");
    Console.WriteLine($"Files Indexed:    {fileCount}");
    Console.WriteLine($"Symbols Indexed:  {symbolCount}");
    Console.WriteLine($"References:       {refCount}");
}, repoArgument, dbDirOption, storeLocalOption);
```

### `clean` Command

Deletes the SQLite database file. Requires confirmation via `--yes` flag.

```csharp
var yesOption = new Option<bool>("--yes", "Skip confirmation prompt");
var cleanCommand = new Command("clean", "Delete the index database for a repository");
cleanCommand.AddArgument(repoArgument);
cleanCommand.AddOption(dbDirOption);
cleanCommand.AddOption(storeLocalOption);
cleanCommand.AddOption(yesOption);
cleanCommand.SetHandler((repo, dbDir, storeLocal, yes) =>
{
    var dbPath = StorageConfig.ResolveDbPath(dbDir?.FullName, storeLocal, repo.FullName);
    if (!File.Exists(dbPath))
    {
        Console.WriteLine($"No database found at {dbPath}");
        return;
    }

    if (!yes)
    {
        Console.Write($"Delete {dbPath}? [y/N] ");
        var answer = Console.ReadLine();
        if (!string.Equals(answer, "y", StringComparison.OrdinalIgnoreCase)) return;
    }

    File.Delete(dbPath);
    // Also delete WAL and SHM files if present
    var walPath = dbPath + "-wal";
    var shmPath = dbPath + "-shm";
    if (File.Exists(walPath)) File.Delete(walPath);
    if (File.Exists(shmPath)) File.Delete(shmPath);

    Console.WriteLine($"Deleted {dbPath}");
}, repoArgument, dbDirOption, storeLocalOption, yesOption);
```

### `watch` Command

Runs a continuous delta re-index loop, polling git for changes every N seconds.

```csharp
var intervalOption = new Option<int>("--interval", () => 30, "Seconds between index checks");
var watchCommand = new Command("watch", "Continuously re-index on git changes");
watchCommand.AddArgument(repoArgument);
watchCommand.AddOption(dbDirOption);
watchCommand.AddOption(storeLocalOption);
watchCommand.AddOption(intervalOption);
watchCommand.SetHandler(async (repo, dbDir, storeLocal, interval) =>
{
    var dbPath  = StorageConfig.ResolveDbPath(dbDir?.FullName, storeLocal, repo.FullName);
    using var db = new CodeGraphDb(dbPath);
    db.Migrate();
    var parsers = GetParsers();
    var delta   = new DeltaEngine(repo.FullName, db, parsers);

    Console.WriteLine($"Watching {repo.FullName} every {interval}s. Press Ctrl+C to stop.");

    using var cts = new CancellationTokenSource();
    Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

    while (!cts.IsCancellationRequested)
    {
        try
        {
            var result = await delta.RunAsync(cts.Token);
            if (result.Mode != "noop")
            {
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] {result.Mode}: " +
                    $"{result.FilesReindexed} files, {result.SymbolsAdded} symbols ({result.ElapsedMs}ms)");
            }
            await Task.Delay(TimeSpan.FromSeconds(interval), cts.Token);
        }
        catch (OperationCanceledException) { break; }
    }
    Console.WriteLine("Stopped.");
}, repoArgument, dbDirOption, storeLocalOption, intervalOption);
```

### Program.cs Registration

Add after existing `rootCommand.AddCommand(serveCommand)`:
```csharp
rootCommand.AddCommand(statusCommand);
rootCommand.AddCommand(cleanCommand);
rootCommand.AddCommand(watchCommand);
```

---

## Execution Order

1. **REF-01a** — Create `Models/SymbolReference.cs`
2. **REF-01b** — Add `ILanguageParser.ParseReferences` default interface method
3. **REF-01c** — Implement `CSharpParser.ParseReferences`
4. **REF-01d** — Implement `TypeScriptParser.ParseReferences`
5. **REF-01e** — Add `ReferenceQueries.DeleteByFile` + `UpsertFileReferences`
6. **REF-01f** — Add `SymbolQueries.ResolveId`
7. **REF-01g** — Add Migration 004 (`idx_refs_file`)
8. **REF-01h** — Integrate into `IndexOrchestrator.IndexAsync`
9. **REF-01i** — Integrate into `DeltaEngine.RunAsync`
10. **WATCH-01a** — Create `Git/FileWatcher.cs`
11. **WATCH-01b** — Integrate `FileWatcher` into `McpServer.cs`
12. **CLI-01** — Add `status`, `clean`, `watch` commands to `Program.cs`
13. `dotnet build` — confirm clean
14. `dotnet test` — 15/15 existing tests pass (no regressions)
15. `dotnet publish` AOT — confirm clean
16. Smoke test: `index` → verify `symbol_references` table has rows → `find_symbol_callers CSharpParser` returns results

---

## New Tests to Write

| Test | Validates |
| :--- | :--- |
| `CSharpParser_ParseReferences_ExtractsMethodCalls` | `invocation_expression` → `Call` references |
| `CSharpParser_ParseReferences_ExtractsInheritance` | `base_list` → `Inheritance` references |
| `CSharpParser_ParseReferences_ExtractsObjectCreation` | `object_creation_expression` → `TypeUsage` references |
| `CSharpParser_ParseReferences_EmptyFile_ReturnsEmpty` | No crash on empty input |
| `ReferenceQueries_UpsertFileReferences_RoundTrips` | Insert then FindCallers returns correct results |
| `FileWatcher_Debounce_CoalescesRapidChanges` | Only one `UpdateFile` call per debounce window |

---

## Verification Plan

### Stability Gate 1 — Build + Tests
```powershell
dotnet build src\CodeGraphMcp
dotnet test
```

### Stability Gate 2 — Reference Population
```powershell
.\publish\CodeGraphMcp.exe index D:\Projects\C#\CodeGraphMcp --store-local --force-full
sqlite3 .agent-index\code_graph.db "SELECT COUNT(*) FROM symbol_references;"
```
Expected: > 0 rows (likely 200+ for this repo).

### Stability Gate 3 — MCP `find_symbol_callers`
Use the MCP stdio smoke test to call `find_symbol_callers` with `symbolName: "FindByName"`. Expected: multiple callers across tool files.

### Stability Gate 4 — File Watcher
1. Start MCP server: `.\publish\CodeGraphMcp.exe serve D:\Projects\C#\CodeGraphMcp --store-local`
2. In another terminal, modify a `.cs` file and save.
3. Call `search_codebase_graph` — verify `isLive: true` result reflects the edit without restarting the server.

### Stability Gate 5 — CLI Utilities
```powershell
.\publish\CodeGraphMcp.exe status D:\Projects\C#\CodeGraphMcp --store-local
.\publish\CodeGraphMcp.exe clean D:\Projects\C#\CodeGraphMcp --store-local --yes
.\publish\CodeGraphMcp.exe status D:\Projects\C#\CodeGraphMcp --store-local   # should show 0 symbols
```

### AOT Publish
```powershell
dotnet publish src\CodeGraphMcp -r win-x64 -p:PublishAot=true -p:StripSymbols=true -o publish\
```
Expected: No new `IL2xxx`/`IL3xxx` warnings.
