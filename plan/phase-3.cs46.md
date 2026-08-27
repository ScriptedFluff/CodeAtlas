# Phase 3 — MCP Server & 5 Tools
## Implementation Plan — MCP-01 · MCP-02 · MCP-03

**Depends On:** Phase 3 Infrastructure (✅ Complete — 15/15 tests, AOT clean)  
**Status:** Ready for Execution

---

## Objective

Wire `CodeAtlas.exe serve <repo-path>` into a live **MCP stdio server** exposing 5 tools to AI agents. Uses `ModelContextProtocol` v2.1.0 (already installed).

---

## Available Infrastructure (Already Built)

| Class | Location | Used By |
| :--- | :--- | :--- |
| `CodeAtlasDb` | `Db/CodeAtlasDb.cs` | All tools — `Symbols`, `References`, `IndexState`, `CrossStack` |
| `DirtyFileBuffer` | `Git/DirtyFileBuffer.cs` | `GetSymbolDefinition`, `SearchCodebaseGraph` — `FindByName(name, lang?)` |
| `GitHelpers` | `Git/GitHelpers.cs` | `GetDiffImpact` — `GetChangedFiles`, `GetDirtyFiles`, `GetHeadCommitHash`, `GetCurrentBranch` |
| `ServiceStackLinker` | `CrossStack/ServiceStackLinker.cs` | `GetServiceStackDtoContract` — `LinkDtos()` |
| `StorageConfig` | `Config/StorageConfig.cs` | `serve` command — `ResolveDbPath(...)` |
| `GetParsers()` | `Program.cs` (local fn) | `McpServer.cs` bootstrap |

**Critical constraint:** `DirtyFileBuffer.FindByName` performs an **exact name match** (`StringComparison.OrdinalIgnoreCase`), not a LIKE/wildcard match. The search tool must use separate logic for wildcard patterns vs exact buffer lookup.

---

## Files to Create / Modify

| # | File | Action |
| :--- | :--- | :--- |
| 1 | `Mcp/McpServer.cs` | **NEW** — Host bootstrap |
| 2 | `Mcp/Tools/GetSymbolDefinitionTool.cs` | **NEW** |
| 3 | `Mcp/Tools/FindSymbolCallersTool.cs` | **NEW** |
| 4 | `Mcp/Tools/SearchCodebaseGraphTool.cs` | **NEW** |
| 5 | `Mcp/Tools/GetDiffImpactTool.cs` | **NEW** |
| 6 | `Mcp/Tools/GetServiceStackDtoContractTool.cs` | **NEW** |
| 7 | `Program.cs` | **MODIFY** — replace stub `serve` command |

**Note:** `Console.WriteLine` in `DirtyFileBuffer.Refresh()` will corrupt the stdio stream. Must suppress it in MCP context. See § DirtyFileBuffer Warning below.

---

## MCP-01 — `McpServer.cs` Bootstrap

### Purpose
Creates the `IHost` with `ModelContextProtocol` DI wiring. Scanning tools via `WithToolsFromAssembly()` avoids manually listing each tool class — the SDK discovers all `[McpServerToolType]`-decorated classes automatically.

### Critical Design Points

**1. Logging must NOT write to stdout.**  
The stdio JSON-RPC stream and stdout are the same pipe. Any `Console.WriteLine` or framework log sent to stdout will produce malformed JSON and hang the client. Solution: suppress all logging below `Warning`, and redirect remaining logs to `stderr` only.

**2. `CodeAtlasDb` is a singleton with a single `SqliteConnection`.**  
SQLite in WAL mode supports concurrent readers. All tool queries are reads. `ServiceStackLinker.LinkDtos()` writes — it must only run once, guarded by `lock` or `Lazy<T>`.

**3. `DirtyFileBuffer` must `Refresh()` on startup** so the first tool call has a warm cache, not a cold empty one.

### Code

```csharp
// Mcp/McpServer.cs
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using CodeAtlas.Core.Config;
using CodeAtlas.Core.CrossStack;
using CodeAtlas.Core.Db;
using CodeAtlas.Core.Git;
using CodeAtlas.Parser;
using ModelContextProtocol.Server;

namespace CodeAtlas.Mcp;

/// <summary>Carries repo path into DI without requiring string-keyed services.</summary>
public sealed record RepoContext(string RepoPath);

public static class McpServer
{
    public static async Task RunAsync(
        string repoPath,
        string dbPath,
        CancellationToken ct = default)
    {
        var parsers = GetParsers();

        var builder = Host.CreateApplicationBuilder();

        // Suppress all framework logs below Warning to protect the stdio JSON-RPC stream.
        // Remaining Warning/Error logs go to stderr via the default console sink
        // (the SDK configures stderr automatically for stdio transport).
        builder.Logging.SetMinimumLevel(LogLevel.Warning);

        builder.Services
            .AddSingleton<RepoContext>(_ => new RepoContext(repoPath))
            .AddSingleton(_ => parsers)
            .AddSingleton<CodeAtlasDb>(_ =>
            {
                var db = new CodeAtlasDb(dbPath);
                db.Migrate();
                return db;
            })
            .AddSingleton(sp =>
            {
                var db      = sp.GetRequiredService<CodeAtlasDb>();
                var ps      = sp.GetRequiredService<IReadOnlyList<ILanguageParser>>();
                var buf     = new DirtyFileBuffer(repoPath, ps);
                buf.Refresh();   // warm the cache before first tool call
                return buf;
            })
            .AddSingleton(sp => new ServiceStackLinker(sp.GetRequiredService<CodeAtlasDb>()))
            .AddMcpServer()
            .WithStdioServerTransport()
            .WithToolsFromAssembly();   // discovers all [McpServerToolType] classes

        await builder.Build().RunAsync(ct);
    }

    private static IReadOnlyList<ILanguageParser> GetParsers() =>
    [
        new CSharpParser(),
        new TypeScriptParser(),
        new HtmlParser(),
        new PowerShellParser()
    ];
}
```

### `Program.cs` — Replace Stub `serve` Command

Replace lines 89–91:
```csharp
// OLD:
var serveCommand = new Command("serve", "Start the MCP server (Phase 3)");
serveCommand.SetHandler(() => Console.WriteLine("MCP server is not yet implemented (Phase 3)."));

// NEW:
var serveRepoArgument     = new Argument<DirectoryInfo>("repo-path", "Repository root to serve");
var serveDbDirOption      = new Option<DirectoryInfo?>("--db-dir", "Override SQLite database directory");
var serveStoreLocalOption = new Option<bool>(new[] { "--store-local", "-l" }, "Use .agent-index/ in repo");

var serveCommand = new Command("serve", "Start the MCP stdio server");
serveCommand.AddArgument(serveRepoArgument);
serveCommand.AddOption(serveDbDirOption);
serveCommand.AddOption(serveStoreLocalOption);

serveCommand.SetHandler(async (repo, dbDir, storeLocal) =>
{
    var dbPath = StorageConfig.ResolveDbPath(dbDir?.FullName, storeLocal, repo.FullName);
    await Mcp.McpServer.RunAsync(repo.FullName, dbPath);
}, serveRepoArgument, serveDbDirOption, serveStoreLocalOption);
```

### ⚠️ DirtyFileBuffer `Console.WriteLine` Warning

`DirtyFileBuffer.Refresh()` currently has:
```csharp
Console.WriteLine($"[DirtyBuffer] Warning: failed to parse {relPath}: {ex.Message}");
```
and similarly in `UpdateFile`. These will corrupt stdout in `serve` mode. Must be changed to write to `Console.Error` instead.

**Fix `DirtyFileBuffer.cs`:** Replace both `Console.WriteLine(...)` calls in the `catch` blocks with:
```csharp
Console.Error.WriteLine($"[DirtyBuffer] Warning: failed to parse {relFilePath}: {ex.Message}");
```

---

## MCP-02 — Symbol Tools (4 tools)

All tool classes follow this skeleton:
- Class attribute: `[McpServerToolType]`  
- Method attribute: `[McpServerTool]` + `[Description("...")]`  
- Constructor injection: whatever DI services are needed  

### Tool 1 — `GetSymbolDefinitionTool.cs`

**Purpose:** Looks up a symbol by name, returns its source code + metadata. Checks dirty buffer first, falls back to SQLite.

**Result type (defined in same file):**
```csharp
public sealed record SymbolDefinitionResult(
    string SymbolName,
    string SymbolType,
    string FilePath,
    int StartLine,
    int EndLine,
    string? Signature,
    string? DocComment,
    string? SourceCode,
    bool IsLive);
```

**Logic:**
1. `buffer.FindByName(symbolName)` — returns exact-name matches from dirty files
   - Apply optional `symbolType` / `filePath` filters
   - If match: read source lines from disk (`File.ReadAllLines`) using `StartLine`/`EndLine` (1-indexed → 0-indexed), return with `IsLive: true`
2. `db.Symbols.FindByName(symbolName)` — LIKE match (already includes `%` if caller passes it)
   - Apply optional filters
   - If match: read source lines, return with `IsLive: false`
3. Return `null` if not found

**Private helper:**
```csharp
private string? ReadSourceRange(Symbol sym)
{
    try
    {
        var fullPath = Path.Combine(repo.RepoPath, sym.FilePath);
        if (!File.Exists(fullPath)) return null;
        var lines = File.ReadAllLines(fullPath);
        var start = Math.Max(0, sym.StartLine - 1);  // 1-indexed → 0-indexed
        var end   = Math.Min(lines.Length - 1, sym.EndLine - 1);
        return string.Join('\n', lines[start..(end + 1)]);
    }
    catch { return null; }
}
```

**Tool method signature:**
```csharp
public SymbolDefinitionResult? GetSymbolDefinition(
    [Description("Exact or partial symbol name. Use % as SQL LIKE wildcard.")] string symbolName,
    [Description("Optional: Class, Method, Interface, Property, Enum, Function")] string? symbolType = null,
    [Description("Optional: filter by relative file path substring")] string? filePath = null)
```

---

### Tool 2 — `FindSymbolCallersTool.cs`

**Purpose:** Queries the `symbol_references` graph table for callers of a named symbol.

**Result types (defined in same file):**
```csharp
public sealed record FindCallersResult(
    string TargetSymbol,
    IReadOnlyList<CallerEntry> Callers,
    string? Note);
```

**Logic:**
1. `db.References.FindCallers(symbolName)` — SQL join (returns empty list if table empty)
2. If empty: return `FindCallersResult` with a `Note` explaining that `symbol_references` is not yet populated
3. If found: return result with the caller list

**Tool method signature:**
```csharp
public FindCallersResult FindSymbolCallers(
    [Description("Symbol name to find callers of (exact, case-insensitive)")] string symbolName)
```

---

### Tool 3 — `SearchCodebaseGraphTool.cs`

**Purpose:** Wildcard symbol search across the full index and live dirty buffer.

**Result types (defined in same file):**
```csharp
public sealed record SearchMatch(
    string SymbolName,
    string SymbolType,
    string? Language,
    string FilePath,
    int StartLine,
    int EndLine,
    string? DocComment,
    bool IsLive);

public sealed record SearchResult(
    string Pattern,
    IReadOnlyList<SearchMatch> Matches);
```

**Logic:**
1. `db.Symbols.FindByName(pattern)` — already uses SQL `LIKE` so `%` wildcards work
   - Apply `symbolType` and `language` filters
2. For dirty buffer: `buffer.FindByName` only does **exact** match, not wildcard
   - If `pattern` contains no `%`, call `buffer.FindByName(pattern, language)` for exact dirty hits
   - If `pattern` has `%`, scan `buffer` entries manually with `string.Contains` on the non-wildcard parts (or skip buffer for wildcard — simpler and correct because dirty files are re-indexed on next `index` run anyway)
3. Merge: dirty buffer matches take precedence; remove any SQLite results shadowed by a dirty entry with the same `(FilePath, SymbolName)` pair
4. Return up to 20 matches total

**Tool method signature:**
```csharp
public SearchResult SearchCodebaseGraph(
    [Description("Symbol name pattern. Use % as wildcard (e.g. 'User%' for all User* symbols)")] string pattern,
    [Description("Optional: Class, Method, Interface, Property, Enum, Function")] string? symbolType = null,
    [Description("Optional: csharp, typescript, html, powershell")] string? language = null)
```

---

### Tool 4 — `GetDiffImpactTool.cs`

**Purpose:** Reports git changes since last index run — which files changed, how many symbols each contains, and what's currently dirty.

**Result types (defined in same file):**
```csharp
public sealed record FileImpact(
    string RelativePath,
    int SymbolCount,
    bool IsDirty);

public sealed record DiffImpactResult(
    string Branch,
    string HeadCommit,
    string LastIndexedCommit,
    IReadOnlyList<FileImpact> ChangedFiles,
    IReadOnlyList<FileImpact> DirtyFiles,
    int TotalSymbolsAffected);
```

**Logic:**
1. `db.IndexState.GetLastCommitHash()` — may return null if never indexed
2. `GitHelpers.GetHeadCommitHash(repo.RepoPath)` + `GetCurrentBranch`
3. If optional `sinceCommit` param provided, use it; otherwise use last indexed hash
4. If a baseline hash is available: `GitHelpers.GetChangedFiles(repo.RepoPath, hash)` → list of relative paths
5. `db.Symbols.CountPerFile()` → `Dictionary<string, int>` for all files — look up each changed file
6. `GitHelpers.GetDirtyFiles(repo.RepoPath)` → dirty files not already in changed list (deduplicated)
7. Build and return `DiffImpactResult`

**Tool method signature:**
```csharp
public DiffImpactResult GetDiffImpact(
    [Description("Optional: compare from this specific commit hash instead of the last indexed commit")] string? sinceCommit = null)
```

---

## MCP-03 — `GetServiceStackDtoContractTool.cs`

**Purpose:** Returns a cross-stack contract record linking a C# DTO to its TypeScript interface and Angular component.

**Result type (defined in same file):**
```csharp
public sealed record DtoContractResult(
    string DtoName,
    string? CsFilePath,
    string? CsLineRange,
    string? TsFilePath,
    string? TsLineRange,
    string? AngularComponentFile,
    string? Note);
```

**Logic:**
1. Try `db.CrossStack.GetMapping(dtoName)` — fast path if already linked
2. If not found: call `linker.LinkDtos()` once (runs DTO scan and writes mappings), then `db.CrossStack.GetMapping(dtoName)` again
3. If still not found: return result with `Note` = `"'{dtoName}' not found. Run 'index' first."`
4. Map `CrossStackMapping` → `DtoContractResult`, formatting line ranges as `"10–25"` strings

**`ServiceStackLinker.LinkDtos()` concurrency note:** This method writes to the DB. To prevent concurrent calls from two simultaneous MCP requests, wrap in a field-level `SemaphoreSlim(1)` or `lock` inside the tool class.

**Tool method signature:**
```csharp
public DtoContractResult GetServiceStackDtoContract(
    [Description("C# DTO class name, e.g. 'GetUserBillingRequest'")] string dtoName)
```

---

## `IndexStateQueries` — Verify `GetLastCommitHash`

`GetDiffImpactTool` calls `db.IndexState.GetLastCommitHash()`. Verify the method signature exists:
```csharp
public string? GetLastCommitHash()
```
If the method is named differently in the existing file, use the actual name.

---

## Verification Plan

### Stability Gate 1 — `tools/list`
```powershell
echo '{"jsonrpc":"2.0","method":"tools/list","id":1}' | `
    .\publish\CodeAtlas.exe serve D:\Projects\C#\repositorydb --store-local
```
Expected: JSON with 5 tools — `get_symbol_definition`, `find_symbol_callers`, `search_codebase_graph`, `get_diff_impact`, `get_servicestack_dto_contract`.

### Stability Gate 2 — `get_symbol_definition`
```powershell
$req = '{"jsonrpc":"2.0","method":"tools/call","id":2,"params":{"name":"get_symbol_definition","arguments":{"symbolName":"CSharpParser"}}}'
echo $req | .\publish\CodeAtlas.exe serve D:\Projects\C#\repositorydb --store-local
```
Expected: Response with `FilePath`, `StartLine`, `EndLine`, and `SourceCode` containing the `CSharpParser` class body.

### Stability Gate 3 — `search_codebase_graph`
```powershell
$req = '{"jsonrpc":"2.0","method":"tools/call","id":3,"params":{"name":"search_codebase_graph","arguments":{"pattern":"Index%"}}}'
echo $req | .\publish\CodeAtlas.exe serve D:\Projects\C#\repositorydb --store-local
```
Expected: Multiple matches including `IndexOrchestrator`, `IndexResult`, etc.

### Automated Tests
Run `dotnet test` after implementation — all 15 existing tests must still pass.

### AOT Publish
```powershell
dotnet publish src\CodeAtlas -r win-x64 -p:PublishAot=true -p:StripSymbols=true -o publish\
```
Expected: No new `IL2xxx` errors introduced by the tool classes (SDK is AOT-compatible).

---

## Execution Order

1. Fix `DirtyFileBuffer.cs` — `Console.Error.WriteLine` (2 catch blocks)
2. Create `Mcp/McpServer.cs`
3. Modify `Program.cs` — replace stub serve command
4. Create `Mcp/Tools/GetSymbolDefinitionTool.cs`
5. Create `Mcp/Tools/FindSymbolCallersTool.cs`
6. Create `Mcp/Tools/SearchCodebaseGraphTool.cs`
7. Create `Mcp/Tools/GetDiffImpactTool.cs`
8. Create `Mcp/Tools/GetServiceStackDtoContractTool.cs`
9. `dotnet build` — confirm clean
10. `dotnet test` — 15/15 pass
11. `dotnet publish` AOT — confirm clean
12. Smoke test `tools/list` and `get_symbol_definition`
