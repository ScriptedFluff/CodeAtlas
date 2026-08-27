# MCP Code Knowledge Graph Engine
## Implementation Plan — C# / .NET 9 Edition

**Version:** 1.0.0  
**Language:** C# (.NET 9, Native AOT)  
**Status:** Ready for Execution  
**Total Estimated Effort:** 178 hours (~8 weeks, part-time)

---

> [!IMPORTANT]
> **Language Decision: C# with .NET 9 Native AOT**
> C# is chosen because this tool backs **AI production workflows** and must be reliable from day one. .NET 9 Native AOT compilation produces a single-file native executable with near-zero startup time and no JIT overhead — eliminating the traditional "C# is heavy" objection. You already know C# deeply, so development velocity is 2-3x faster than learning Rust mid-project.

> [!WARNING]
> **Production Safety Strategy**
> Keep your existing AI tooling working in parallel throughout. The MCP server is additive — agents fall back to raw file reads if the server is unavailable. Build Phase 1-2 first as a CLI-only tool with zero MCP exposure. Only wire MCP in Phase 3 once the index is proven reliable.

---

## .NET Ecosystem: Package Selections

Before writing a line of code, these are the definitive NuGet package choices.

| Concern | Package | Reason |
| :--- | :--- | :--- |
| Tree-sitter core | `TreeSitter` (NuGet) | .NET bindings wrapping the C library |
| C# grammar | `TreeSitter.Language.CSharp` | Official C# grammar bindings |
| TypeScript grammar | `TreeSitter.Language.TypeScript` | Official TS grammar bindings |
| PowerShell grammar | `TreeSitter.Language.PowerShell` | Community grammar bindings |
| HTML grammar | `TreeSitter.Language.Html` | Official HTML grammar bindings |
| SQLite | `Microsoft.Data.Sqlite` | Official Microsoft SQLite driver |
| DB migrations | `SimpleMigrations` or `FluentMigrator` | Versioned schema migration |
| MCP protocol | `ModelContextProtocol` (.NET SDK) | Official Microsoft MCP SDK |
| Git operations | `LibGit2Sharp` | .NET bindings to libgit2 — avoids shelling out |
| CLI arguments | `System.CommandLine` | Official .NET CLI library |
| JSON serialization | `System.Text.Json` | Built-in, AOT-compatible |
| Logging | `Microsoft.Extensions.Logging` + `Serilog` | Structured logging, file sink |
| DI / Hosting | `Microsoft.Extensions.Hosting` | Background service hosting |
| File watching | `System.IO.FileSystemWatcher` | Built-in |
| Config / paths | `System.Environment.GetFolderPath` | Built-in, cross-platform |

> [!TIP]
> **AOT Compatibility Note:**
> All packages above are AOT-compatible. Avoid packages that use runtime reflection heavily (e.g. Newtonsoft.Json — use `System.Text.Json` instead). The `ModelContextProtocol` SDK is designed to work with AOT. Test AOT builds from Phase 1 onwards with `dotnet publish -r win-x64 -p:PublishAot=true`.

---

## Project Structure

```
CodeAtlas/
├── CodeAtlas.sln
├── src/
│   └── CodeAtlas/
│       ├── CodeAtlas.csproj
│       ├── Program.cs                      # CLI entrypoint (System.CommandLine)
│       ├── Config/
│       │   └── StorageConfig.cs            # DB path resolution logic
│       ├── Db/
│       │   ├── CodeAtlasDb.cs              # Connection factory + migration runner
│       │   ├── Migrations/                 # Versioned schema migration files
│       │   │   ├── M001_InitialSchema.cs
│       │   │   └── M002_Indices.cs
│       │   └── Queries/
│       │       ├── SymbolQueries.cs        # Typed SQL query methods
│       │       └── ReferenceQueries.cs
│       ├── Parser/
│       │   ├── ILanguageParser.cs          # Parser abstraction interface
│       │   ├── CSharpParser.cs
│       │   ├── TypeScriptParser.cs
│       │   ├── PowerShellParser.cs
│       │   └── HtmlParser.cs
│       ├── Git/
│       │   ├── DeltaEngine.cs              # git diff delta reconciliation
│       │   └── DirtyFileBuffer.cs          # git status live buffer
│       ├── Indexer/
│       │   └── IndexOrchestrator.cs        # Orchestrates parse -> DB write
│       ├── Mcp/
│       │   ├── McpServer.cs                # MCP server startup
│       │   └── Tools/
│       │       ├── GetSymbolDefinition.cs
│       │       ├── FindSymbolCallers.cs
│       │       ├── GetDiffImpact.cs
│       │       ├── GetServiceStackDtoContract.cs
│       │       └── SearchCodebaseGraph.cs
│       └── CrossStack/
│           └── ServiceStackLinker.cs       # clevernpm DTO auto-linker
└── tests/
    ├── CodeAtlas.Tests/
    │   ├── Fixtures/                       # Sample .cs, .ts, .ps1 files
    │   └── IntegrationTests.cs
```

---

## Phase 1 — Core Engine & Tree-sitter Parsers
**Weeks 1-2 | Est. 52 hours**

### Objective
A working CLI binary that can index a codebase into SQLite. No MCP, no git delta — just full parse-and-store.

### Tasks

#### ENG-01 — Project Scaffold (12h)
```
Priority: High | Status: Planned
```

- Create solution: `dotnet new console -n CodeAtlas`
- Add all NuGet packages above
- Set up `System.CommandLine` with subcommands: `index <repo-path>`, `query <symbol>`, `serve`
- Implement `StorageConfig.cs` storage resolution (priority: `--db-dir` > `--store-local` > `%LOCALAPPDATA%`)

```csharp
public static class StorageConfig
{
    public static string ResolveDbPath(string? dbDir, bool storeLocal, string repoPath)
    {
        if (dbDir is not null)
            return Path.Combine(dbDir, $"{HashRepoPath(repoPath)}.db");

        if (storeLocal)
            return Path.Combine(repoPath, ".agent-index", "atlas.db");

        // Default: %LOCALAPPDATA%\CodeGraph\dbs\<hash>.db
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(localAppData, "CodeGraph", "dbs", $"{HashRepoPath(repoPath)}.db");
    }

    private static string HashRepoPath(string repoPath) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(repoPath)))[..16];
}
```

Configure `CodeAtlas.csproj` for AOT from the start:
```xml
<PropertyGroup>
  <TargetFramework>net9.0</TargetFramework>
  <Nullable>enable</Nullable>
  <ImplicitUsings>enable</ImplicitUsings>
  <PublishAot>true</PublishAot>
  <InvariantGlobalization>true</InvariantGlobalization>
</PropertyGroup>
```

**Stability gate:** `dotnet run -- index --help` prints correct usage. `dotnet publish -r win-x64 -p:PublishAot=true` produces a single executable.

---

#### ENG-02 — Tree-sitter Parser Integration (24h)
```
Priority: High | Status: Planned
```

Define a clean parser abstraction:

```csharp
public interface ILanguageParser
{
    string[] FileExtensions { get; }
    IEnumerable<Symbol> Parse(string filePath, string source);
}
```

Implement for each language using Tree-sitter S-expression queries:

```csharp
public class CSharpParser : ILanguageParser
{
    public string[] FileExtensions => [".cs"];

    // Tree-sitter query capturing classes, interfaces, and methods
    private const string SymbolQuery = """
        (class_declaration name: (identifier) @class.name) @class.def
        (interface_declaration name: (identifier) @interface.name) @interface.def
        (method_declaration name: (identifier) @method.name) @method.def
        """;

    public IEnumerable<Symbol> Parse(string filePath, string source)
    {
        using var parser = new Parser();
        parser.SetLanguage(CSharp.Language);
        using var tree = parser.Parse(source);
        var query = new Query(CSharp.Language, SymbolQuery);

        foreach (var match in query.Matches(tree.RootNode))
        {
            // Extract captures -> yield Symbol records
            yield return ExtractSymbol(match, filePath, source);
        }
    }
}
```

Register all parsers in DI and dispatch by file extension in `IndexOrchestrator`.

**Stability gate:** `dotnet run -- index D:\Projects\C#\repositorydb` populates the `symbols` table. Verify with:
```powershell
sqlite3 "%LOCALAPPDATA%\CodeGraph\dbs\<hash>.db" "SELECT symbol_name, start_line FROM symbols LIMIT 20"
```

---

#### ENG-03 — SQLite Schema & Migrations (16h)
```
Priority: High | Status: Planned
```

Schema via `SimpleMigrations`:

```csharp
[Migration(1, "Initial schema")]
public class M001_InitialSchema : Migration
{
    protected override void Up()
    {
        Execute(@"
            CREATE TABLE index_state (
                branch TEXT NOT NULL,
                last_commit_hash TEXT PRIMARY KEY,
                indexed_at TEXT NOT NULL,
                dirty_files_count INTEGER DEFAULT 0
            );
            CREATE TABLE namespaces (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                name TEXT UNIQUE NOT NULL,
                language TEXT NOT NULL
            );
            CREATE TABLE symbols (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                namespace_id INTEGER REFERENCES namespaces(id),
                class_name TEXT,
                symbol_type TEXT NOT NULL,
                symbol_name TEXT NOT NULL,
                file_path TEXT NOT NULL,
                start_line INTEGER NOT NULL,
                end_line INTEGER NOT NULL,
                start_byte INTEGER NOT NULL,
                end_byte INTEGER NOT NULL,
                signature TEXT,
                doc_comment TEXT
            );
            CREATE TABLE symbol_references (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                caller_symbol_id INTEGER REFERENCES symbols(id),
                target_symbol_id INTEGER REFERENCES symbols(id),
                reference_type TEXT NOT NULL,
                file_path TEXT NOT NULL,
                line_number INTEGER NOT NULL
            );
            CREATE TABLE cross_stack_mappings (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                dto_name TEXT NOT NULL,
                cs_class_id INTEGER REFERENCES symbols(id),
                ts_interface_id INTEGER REFERENCES symbols(id),
                angular_component_path TEXT
            );
        ");
    }
}

[Migration(2, "Performance indices")]
public class M002_Indices : Migration
{
    protected override void Up()
    {
        Execute(@"
            CREATE INDEX idx_symbols_name ON symbols(symbol_name);
            CREATE INDEX idx_symbols_file ON symbols(file_path);
            CREATE INDEX idx_refs_caller ON symbol_references(caller_symbol_id);
            CREATE INDEX idx_refs_target ON symbol_references(target_symbol_id);
        ");
    }
}
```

All SQL lives in `Queries/` — no inline SQL scattered in business logic.

**Stability gate:** Schema creates on fresh DB. Running index twice doesn't duplicate rows (upsert via `INSERT OR REPLACE INTO`).

---

### Phase 1 Exit Criteria
- [ ] `dotnet publish -r win-x64 -p:PublishAot=true` produces a working single-file binary
- [ ] Indexing `repositorydb` populates symbols with correct line numbers
- [ ] Symbol count per file sanity-checked against manual inspection
- [ ] All exceptions handled — no unhandled `NullReferenceException` paths

---

## Phase 2 — Git Delta Engine & Storage Router
**Weeks 3-4 | Est. 48 hours**

### Objective
Switch from full re-index to incremental delta indexing. Add live in-memory parser for dirty/uncommitted files.

### Tasks

#### GIT-01 — Git Diff Delta Reconciliation (18h)
```
Priority: High | Status: Planned
```

Use `LibGit2Sharp` (avoids shelling out to git):

```csharp
public class DeltaEngine(string repoPath, CodeAtlasDb db)
{
    public IReadOnlyList<string> GetChangedFiles(string sinceCommit)
    {
        using var repo = new Repository(repoPath);
        var oldCommit = repo.Lookup<Commit>(sinceCommit);
        var newCommit = repo.Head.Tip;

        var diff = repo.Diff.Compare<TreeChanges>(
            oldCommit.Tree,
            newCommit.Tree);

        return diff.Select(c => c.Path).ToList();
    }

    public async Task RunDeltaIndexAsync(CancellationToken ct = default)
    {
        var lastHash = db.GetLastCommitHash();
        var changed = GetChangedFiles(lastHash);

        foreach (var file in changed)
        {
            db.DeleteSymbolsForFile(file);
            // re-parse and insert
        }

        db.UpdateLastCommitHash(GetHeadHash());
    }
}
```

**Stability gate:** Modify one `.cs` file, commit, run delta index. Only that file's symbols are updated (verify via `indexed_at` in DB).

---

#### GIT-02 — Storage Router & Configuration (10h)
```
Priority: Medium | Status: Planned
```

`StorageConfig.cs` already scaffolded in ENG-01. This task wires it to the full CLI:

- `--db-dir <path>` flag
- `--store-local` / `-l` flag
- Default to `%LOCALAPPDATA%\CodeGraph\dbs\<hash>.db`
- Create target directories if they don't exist (`Directory.CreateDirectory`)
- Add `.gitignore` entry for `.agent-index/` when `--store-local` is used

**Stability gate:** All three storage modes tested with correct path output on Windows.

---

#### GIT-03 — Live In-Memory Buffer for Dirty Files (20h)
```
Priority: High | Status: Planned
```

For files modified but not yet committed:

```csharp
public class DirtyFileBuffer(string repoPath, IReadOnlyList<ILanguageParser> parsers)
{
    private readonly Dictionary<string, List<Symbol>> _dirty = new();

    public void Refresh()
    {
        _dirty.Clear();
        using var repo = new Repository(repoPath);

        foreach (var entry in repo.RetrieveStatus())
        {
            if (entry.State is FileStatus.ModifiedInWorkdir or FileStatus.NewInWorkdir)
            {
                var fullPath = Path.Combine(repoPath, entry.FilePath);
                var parser = parsers.FirstOrDefault(p =>
                    p.FileExtensions.Contains(Path.GetExtension(fullPath)));

                if (parser is not null)
                {
                    var source = File.ReadAllText(fullPath);
                    _dirty[entry.FilePath] = parser.Parse(fullPath, source).ToList();
                }
            }
        }
    }

    public IReadOnlyList<Symbol>? GetDirtySymbols(string relPath) =>
        _dirty.TryGetValue(relPath, out var syms) ? syms : null;
}
```

MCP tools check `DirtyFileBuffer` first, then fall back to the SQLite index.

**Stability gate:** Edit a `.cs` file without committing. Query for a symbol in that file — returns the live version without re-indexing.

---

### Phase 2 Exit Criteria
- [ ] Delta re-index runs in < 500ms on a typical commit
- [ ] Live buffer correctly serves symbols from uncommitted files
- [ ] `--store-local` and `--db-dir` flags work correctly on Windows paths
- [ ] `LibGit2Sharp` handles detached HEAD and new (no commits) repos gracefully

---

## Phase 3 — MCP Server Protocol Implementation
**Weeks 5-6 | Est. 58 hours**

### Objective
Expose the knowledge graph as a live MCP server that AI agents connect to.

> [!CAUTION]
> **Do not connect to your production AI workflow until integration tests pass.** Run in parallel with the existing raw-file workflow until Phase 4 verification is complete.

### Tasks

#### MCP-01 — MCP stdio Server (20h)
```
Priority: High | Status: Planned
```

Using the `ModelContextProtocol` .NET SDK:

```csharp
// Program.cs — serve subcommand
var builder = Host.CreateApplicationBuilder();

builder.Services
    .AddSingleton<CodeAtlasDb>()
    .AddSingleton<DirtyFileBuffer>()
    .AddMcpServer()
    .WithStdioServerTransport()
    .WithTools<GetSymbolDefinitionTool>()
    .WithTools<FindSymbolCallersTool>()
    .WithTools<GetDiffImpactTool>()
    .WithTools<GetServiceStackDtoContractTool>()
    .WithTools<SearchCodebaseGraphTool>();

await builder.Build().RunAsync(ct);
```

Each tool class is decorated with `[McpServerTool]`:

```csharp
[McpServerTool, Description("Get the exact code definition of a symbol by name")]
public class GetSymbolDefinitionTool(CodeAtlasDb db, DirtyFileBuffer buffer)
{
    [McpServerTool]
    public SymbolResult GetSymbolDefinition(
        string symbolName,
        string? namespaceName = null,
        string? filePath = null)
    {
        // 1. Check dirty buffer first
        // 2. Query SQLite for symbol
        // 3. Read byte range from file
        // 4. Return code snippet + metadata
    }
}
```

**Stability gate:**
```powershell
echo '{"jsonrpc":"2.0","method":"tools/list","id":1}' | .\CodeAtlas.exe serve
```
Returns all 5 tools in the response.

---

#### MCP-02 — Symbol Tools Implementation (16h)
```
Priority: High | Status: Planned
```

Implement `GetSymbolDefinition` and `FindSymbolCallers` first (highest value):

**`FindSymbolCallers` — SQL JOIN across the graph:**
```csharp
public IReadOnlyList<CallerResult> FindCallers(string symbolName)
{
    const string sql = """
        SELECT s_caller.symbol_name, s_caller.file_path, sr.line_number, sr.reference_type
        FROM symbol_references sr
        JOIN symbols s_target ON sr.target_symbol_id = s_target.id
        JOIN symbols s_caller ON sr.caller_symbol_id = s_caller.id
        WHERE s_target.symbol_name = @symbolName
        ORDER BY s_caller.file_path, sr.line_number
        """;

    // Execute via Microsoft.Data.Sqlite, map to CallerResult records
}
```

Also implement `GetDiffImpact` and `SearchCodebaseGraph`.

**Stability gate:** Register server in Antigravity MCP config. Verify `get_symbol_definition` returns the correct C# method body for a known symbol in `repositorydb`.

---

#### MCP-03 — ServiceStack clevernpm Cross-Stack Connector (22h)
```
Priority: Medium | Status: Planned
```

Implement `GetServiceStackDtoContract` with these auto-linking heuristics during indexing:

- **C# side:** Classes in namespaces matching `*.Requests` / `*.Responses`, or decorated with `[Route(...)]` / `[Api(...)]`
- **TS side:** Interfaces in `clevernpm` with matching names (`GetUserBillingRequest` -> `IGetUserBillingRequest`)
- **Angular side:** Components importing from `clevernpm` that consume the TS interface

```csharp
public class ServiceStackLinker(CodeAtlasDb db)
{
    public void LinkDtos()
    {
        var csRequests = db.GetSymbolsByNamespacePattern("%.Requests")
            .Concat(db.GetSymbolsByNamespacePattern("%.Responses"));

        foreach (var csDto in csRequests)
        {
            // Match to TS interface by name convention
            var tsName = $"I{csDto.SymbolName}"; // or same name
            var tsInterface = db.FindSymbol(tsName, language: "typescript");

            if (tsInterface is not null)
                db.UpsertCrossStackMapping(csDto, tsInterface);
        }
    }
}
```

**Stability gate:** `get_servicestack_dto_contract("GetUserBillingRequest")` returns the C# DTO, matching TS interface, and at least one Angular component path.

---

### Phase 3 Exit Criteria
- [ ] MCP server registers in Antigravity/VS Code MCP config successfully
- [ ] All 5 tools respond correctly to test queries
- [ ] Server handles concurrent tool calls (thread-safe SQLite connection)
- [ ] Memory usage stays < 50MB during active indexing

---

## Phase 4 — Agent Instruction Sync & Verification
**Weeks 7-8 | Est. 20 hours**

### Objective
Wire AI agents to prefer MCP tools over raw file reads. Measure real-world token savings.

### Tasks

#### DOC-01 — Agent Instruction Updates (8h)
```
Priority: High | Status: Planned
```

Update/create the following:
- `copilot-instructions.md` — MCP tool preference rules
- `.agents/AGENTS.md` — Workspace-scoped rules for Antigravity
- MCP server registration in Antigravity / VS Code `mcp.json` config

Example instruction block to add:
```markdown
## Code Lookup Protocol
ALWAYS prefer MCP tools over reading full source files:
1. Use `get_symbol_definition` to retrieve method/class bodies
2. Use `find_symbol_callers` before assuming a function is unused
3. Use `get_diff_impact` before recommending refactors
4. Use `get_servicestack_dto_contract` for any ServiceStack DTO work
Only fall back to reading full files if the MCP server returns no result.
```

Example `mcp.json` registration:
```json
{
  "servers": {
    "code-graph": {
      "type": "stdio",
      "command": "C:\\Tools\\CodeAtlas\\CodeAtlas.exe",
      "args": ["serve", "--repo", "${workspaceFolder}"]
    }
  }
}
```

---

#### VER-01 — Token & Latency Benchmarking (12h)
```
Priority: Medium | Status: Planned
```

Run the 4 test scenarios from `test-scenarios.md` against both environments and record:
- Input tokens consumed (API usage dashboard)
- Time to first useful response
- Hallucination incidents (symbol names that don't exist)

| Target Metric | Goal |
| :--- | :--- |
| Token reduction | > 90% vs. raw file baseline |
| Response latency | < 2 seconds for symbol lookups |
| Delta re-index time | < 500ms |
| False symbol results | 0 (exact DB match only) |

The 4 test scenarios (from `test-scenarios.md`):
- **Scenario A:** Single-function surgical edit (`Get-UsageStats`)
- **Scenario B:** Cross-file caller identification (TS/Angular impact from C# change)
- **Scenario C:** Git diff impact analysis (`UserService.cs`)
- **Scenario D:** Uncommitted local buffer interception (dirty file test)

---

### Phase 4 Exit Criteria
- [ ] AI agents consistently use MCP tools (visible in tool call logs)
- [ ] Token reduction > 90% measured across 10 real sessions
- [ ] All 4 test scenarios produce correct results from MCP vs. raw file baseline
- [ ] Server auto-starts on repo open (VS Code task or Windows startup task)

---

## Effort Summary

| Phase | Tasks | Est. Hours | Key Deliverable |
| :--- | :--- | :---: | :--- |
| Phase 1 | ENG-01, ENG-02, ENG-03 | 52h | Working CLI indexer |
| Phase 2 | GIT-01, GIT-02, GIT-03 | 48h | Delta + live buffer engine |
| Phase 3 | MCP-01, MCP-02, MCP-03 | 58h | Live MCP server |
| Phase 4 | DOC-01, VER-01 | 20h | Production-wired agents |
| **Total** | **11 tasks** | **178h** | **Production MCP server** |

---

## Technology Stack Summary

```
.NET 9 + Native AOT (single-file win-x64 binary)
├── TreeSitter NuGet bindings         -> AST parsing (C#, TS, PS1, HTML)
├── Microsoft.Data.Sqlite             -> Knowledge graph DB
├── SimpleMigrations                  -> Versioned schema migrations
├── ModelContextProtocol .NET SDK     -> MCP stdio/HTTP transport
├── LibGit2Sharp                      -> git diff / status (no shell-out)
├── System.CommandLine                -> CLI (index / query / serve)
└── Microsoft.Extensions.Hosting      -> DI + background service model
```

---

## Risk Register

| Risk | Likelihood | Impact | Mitigation |
| :--- | :---: | :---: | :--- |
| Tree-sitter NuGet bindings lag behind official grammars | Medium | Medium | Pin to tested version; fall back to regex for PS1 if needed |
| `ModelContextProtocol` SDK API changes pre-1.0 | Medium | Medium | Pin exact version; watch GitHub releases |
| AOT trim warnings breaking `Microsoft.Data.Sqlite` | Low | Medium | Add `<TrimmerRootDescriptor>` for SQLite if needed; test AOT early |
| `LibGit2Sharp` native lib missing on target machine | Low | Medium | Bundle native `git2-*.dll` alongside the executable in publish output |
| Production AI breaks during MCP switchover | Low | High | Agents only *prefer* MCP, never require it. Raw file fallback always active |

---

## Day 1 Quickstart

```powershell
# Create solution and project
dotnet new sln -n CodeAtlas
dotnet new console -n CodeAtlas -o src/CodeAtlas
dotnet sln add src/CodeAtlas/CodeAtlas.csproj

# Add NuGet packages
cd src/CodeAtlas
dotnet add package System.CommandLine --prerelease
dotnet add package Microsoft.Data.Sqlite
dotnet add package LibGit2Sharp
dotnet add package ModelContextProtocol --prerelease
dotnet add package TreeSitter
dotnet add package TreeSitter.Language.CSharp
dotnet add package TreeSitter.Language.TypeScript
dotnet add package TreeSitter.Language.Html
dotnet add package SimpleMigrations
dotnet add package Serilog.Extensions.Hosting
dotnet add package Serilog.Sinks.Console
dotnet add package Serilog.Sinks.File

# Verify build
dotnet build

# Test AOT publish early (catch issues before Phase 3)
dotnet publish -r win-x64 -p:PublishAot=true -p:StripSymbols=true
```

> [!TIP]
> Run `dotnet publish -r win-x64 -p:PublishAot=true` after every major phase, not just at the end. AOT trim warnings are far easier to fix when you're close to the code that caused them.
