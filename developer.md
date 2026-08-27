# CodeAtlas — Developer Technical Specification & Command Architecture

`CodeAtlas` is a high-performance local code knowledge graph indexer, reference graph analysis engine, and Model Context Protocol (MCP) server designed for software architects and senior engineers. It parses multi-language source trees (C#, TypeScript, JavaScript, HTML, PowerShell, JSON, YAML, CSS), extracts syntax trees into relational SQLite graphs, tracks Git deltas for sub-second incremental indexing, performs cross-stack ServiceStack DTO linkage, and surfaces dirty uncommitted file modifications live to AI coding assistants.

---

## 1. Core Architecture & Subsystems Overview

```
CodeAtlas.slnx
├── src/
│   ├── CodeAtlas.Abstractions/ # Class Library (.dll) - Zero-dependency NuGet contracts & models
│   │   ├── Models/                # Symbol, SymbolReference, SymbolType
│   │   └── Parser/                # ILanguageParser, ParserMetadata, ParserMetadataAttribute, FileParseResult
│   │
│   ├── CodeAtlas.Core/         # Class Library (.dll) - Domain logic & indexing engine
│   │   ├── Config/                # AppConfig, StorageConfig, WorkspacePathResolver
│   │   ├── Db/                    # SqliteDbFactory, Schema Migrations, Symbol Repositories, WorktreePruner
│   │   ├── Indexer/               # IndexOrchestrator, SolutionRootResolver, DynamicWorkspaceService
│   │   ├── Parser/                # IParserRegistry, ParserRegistry
│   │   ├── Plugins/               # ParserLoadContext, DirectoryParserPluginLoader, ParserSecurityEvaluator
│   │   ├── Git/                   # GitHelpers, DeltaEngine, DirtyFileBuffer, GitIgnoreMatcher
│   │   ├── CrossStack/            # ServiceStack DTO Contract Resolver & Linkers
│   │   └── Models/                # IndexResult, CallerEntry, CrossStackMapping
│   │
│   ├── Parsers/                   # Standalone Drop-in Language Parser Plugins
│   │   ├── CodeAtlas.Parsers.CSharp/
│   │   ├── CodeAtlas.Parsers.TypeScript/
│   │   ├── CodeAtlas.Parsers.JavaScript/
│   │   ├── CodeAtlas.Parsers.Html/
│   │   ├── CodeAtlas.Parsers.PowerShell/
│   │   ├── CodeAtlas.Parsers.Json/
│   │   ├── CodeAtlas.Parsers.Yaml/
│   │   └── CodeAtlas.Parsers.Css/
│   │
│   ├── CodeAtlas.Mcp/       # Executable (.exe) - MCP Daemon Host (ReadyToRun)
│   │   ├── Mcp/                   # 7 MCP Tool Registrations & JSON-RPC Dispatcher
│   │   └── Program.cs             # Entry point for stdio MCP daemon host
│   │
│   └── CodeAtlas.Cli/          # Executable (.exe) - Command Line Utility (ReadyToRun)
│       ├── Commands/              # Index, Query, Status, Parsers, Prune, Watch, Serve
│       └── Program.cs             # Entry point for CLI command runner
```

### Decoupled Subsystem Responsibilities

1. **Abstractions Library (`CodeAtlas.Abstractions`)**:
    - **Standalone NuGet Package**: Zero-dependency library targeting `.NET 10.0`, `.NET 8.0`, and `.NET Standard 2.0`.
    - **Core Contracts & Models**: Exposes `ILanguageParser`, `Symbol`, `SymbolReference`, `SymbolType`, `ParserMetadata`, `ParserMetadataAttribute`, and `FileParseResult`.
    - Enables 3rd party developers and community contributors to write custom drop-in parsers without referencing engine runtime dependencies.

2. **Core Domain Library (`CodeAtlas.Core`)**:
    - **Dynamic Plugin Engine (`Plugins/`)**: Loads drop-in parser assemblies into collectible `AssemblyLoadContext` instances. Delegates `CodeAtlas.Abstractions`, `CodeAtlas.Core`, and `TreeSitter` to host ALC to preserve type identity for `ILanguageParser` and `TreeSitter.Node`.
    - **Multi-Tier Directory Resolver (`ParserDirectoryResolver`)**: Scans `Environment` (`CODEATLAS_PARSERS_DIR`), `User` (`%LOCALAPPDATA%\CodeAtlas\Parsers`), and `Application` (`{AppBase}\Parsers`) locations.
    - **Security Policy Engine (`ParserSecurityEvaluator`)**: Prevents DLL hijacking and unprivileged shadowing (`Strict`, `AllowAll`, `AllowList`).
    - **Streaming Indexing Pipeline**: Uses a `Channel`-based Producer-Consumer pipeline to stream parsed AST results asynchronously from parallel CPU workers to a background SQLite writer task in 250-file batches without CPU idle time.
    - **Git Delta Tracker**: `git diff` interrogation against `last_indexed_commit` for sub-second incremental indexing.
    - **Relational Knowledge Graph**: SQLite WAL-mode repository layer (`Dapper`) storing symbol definitions, references, and DTO mappings.
    - **Live Dirty Buffer Interceptor**: Merges uncommitted file edits on disk with SQLite records for zero-latency accuracy.
    - **Worktree Pruner**: Orphaned database purge utility reclaiming disk space.

2. **Standalone Parser Plugins (`src/Parsers/`)**:
    - Decoupled projects compiled independently and output into `Parsers/<ParserName>/`.
    - Implements `ILanguageParser` and declares `ParserMetadata` (ID, language, file extensions, version, author).

3. **MCP Daemon Host (`CodeAtlas.Mcp`)**:
    - Implements JSON-RPC 2.0 over standard I/O streams (`stdio`) via `ModelContextProtocol.Server`.
    - Exposes 7 specialized knowledge graph tools directly to AI assistants.

4. **CLI Utility Launcher (`CodeAtlas.Cli` / `atlas`)**:
    - Command-line utility built on `System.CommandLine`.
    - Executes `index`, `query`, `status`, `parsers`, `watch`, `clean`, and `prune` commands. Delegated `serve` launches `CodeAtlas.Mcp`.

---

## 2. Technical Deep-Dive into Commands & Workflows

### 2.1 Index Command (`index`)
Parses, indexes, and builds symbol/reference graphs for a target repository.

- **Developer Context**: Executes file discovery, runs multi-threaded AST parsing via parallel CPU workers, streams parsed AST payloads through an in-memory `Channel` to a background SQLite writer task (250-file batches), resolves internal cross-references against `SymbolLookupCache`, and commits symbol trees to the SQLite store.
- **Incremental Logic**: Performs git commit inspection. If HEAD equals `last_indexed_commit` and no uncommitted files exist, execution terminates instantly (<20ms).
- **Execution Options**:
  - `<repo-path>` *(Required)*: Target repository directory.
  - `--db-dir <path>`: Custom location for the SQLite database (defaults to `<repo-path>/.codeatlas/`).
  - `--force-full`: Forces full parse cycle, bypassing Git delta detection.
  - `--no-ignore`: Ignores `.gitignore` rules to index hidden or build-generated source files.

#### Examples:
```powershell
# Standard local repository index
CodeAtlas index "D:\Projects\C#\CodeAtlas"

# Force complete re-index overriding git deltas and ignore rules
CodeAtlas index "C:\Services\PaymentGateway" --force-full --no-ignore

# Store index in custom central path
CodeAtlas index "C:\Repo" --db-dir "C:\SharedIndexes\RepoDb"
```

---

### 2.2 Query Command (`query`)
Searches for symbol definitions, signatures, line boundaries, and source code directly from the command line.

- **Developer Context**: Combines indexed database definitions with a live filesystem sweep. If an uncommitted file contains modified symbol definitions, it emits `[LIVE - uncommitted]` records.
- **Execution Options**:
  - `<symbol-name>` *(Required)*: Symbol or wildcard pattern.
  - `--repo`, `-r <path>`: Target repository context (defaults to CWD).
  - `--db-dir <path>`: Custom path to SQLite index directory.

#### Examples:
```powershell
# Query symbol definition within the local directory
CodeAtlas query "IndexOrchestrator"

# Query specific project repository from an external directory
CodeAtlas query "PaymentProcessor" --repo "D:\Projects\Services\Billing"
```

---

### 2.3 Status Command (`status`)
Reports current repository health, branch info, committed vs. indexed state, and graph counts.

- **Developer Context**: Queries SQLite metadata alongside local Git HEAD. Flags whether re-indexing is required.
- **Execution Options**:
  - `<repo-path>` *(Required)*: Target directory path.

#### Examples:
```powershell
# Inspect repository index status
CodeAtlas status "D:\Projects\C#\CodeAtlas"
```

---

### 2.4 Watch Command (`watch`)
Runs a background daemon polling Git commit state and updating the index upon commit or branch transitions.

- **Developer Context**: Ideal for local development environments or CI pipelines to keep knowledge graphs continuously synchronized.
- **Execution Options**:
  - `<repo-path>` *(Required)*: Repository path to monitor.
  - `--interval <seconds>`: Polling frequency (default: `30`).

#### Examples:
```powershell
# Watch repository with 15-second polling loop
CodeAtlas watch "D:\Projects\C#\CodeAtlas" --interval 15
```

---

### 2.5 Clean Command (`clean`)
Purges SQLite databases and WAL journal files associated with the repository.

- **Developer Context**: Frees storage or resets corrupt state before re-indexing.
- **Execution Options**:
  - `<repo-path>` *(Required)*: Path to target repo.
  - `--yes`, `-y` / `--force`, `-f`: Bypasses user confirmation prompt.

#### Examples:
```powershell
# Purge local repository index database silently
CodeAtlas clean "D:\Projects\C#\CodeAtlas" --yes
```

---

### 2.6 Serve Command (`serve`)
Launches the Model Context Protocol (MCP) server over `stdio` for AI tool integration.

- **Developer Context**: Establishes standard I/O JSON-RPC server listening for MCP requests. Integrates natively with Antigravity, Claude, Cursor, and Copilot.
- **Execution Options**:
  - `--repo`, `-r <path>`: Binds server to a fixed target directory.
  - `--db-dir <path>`: Custom DB storage location.

#### Examples:
```powershell
# Launch unbound dynamic MCP server
CodeAtlas serve

# Launch repository-bound MCP server using local index
CodeAtlas serve --repo "D:\Projects\C#\CodeAtlas"
```

---

## 3. Developer Use Cases & Workflows

### Use Case A: Refactoring Impact & Blast Radius Analysis
Before renaming or refactoring core methods, developers can use `get_diff_impact` and `find_symbol_callers` to evaluate downstream dependencies.

**Workflow Command Example:**
```powershell
# Index project first
CodeAtlas index "C:\Projects\CoreEngine"

# MCP Tool Request Payload (via JSON-RPC stdio)
# Tool: get_diff_impact
# Params: { "targetDirectory": "C:\\Projects\\CoreEngine" }
```

### Use Case B: ServiceStack Cross-Stack DTO Contract Tracing
Tracing how C# backend Request/Response DTO contracts bind to TypeScript interfaces and Angular frontend components.

**Workflow Command Example:**
```powershell
# MCP Tool Request Payload
# Tool: get_servicestack_dto_contract
# Params: { "dtoName": "GetUserBillingRequest", "targetDirectory": "C:\\Projects\\FullStackApp" }
```

### Use Case C: Live Uncommitted Editing Verification
When editing files in an IDE, AI agents query the MCP server. `CodeAtlas` reads the dirty file buffer directly from disk, delivering up-to-the-second symbol line ranges even before saving to disk.

---

## 4. MCP Server Tool Reference

| Tool Name | Key Parameters | Developer Description |
| :--- | :--- | :--- |
| `get_symbol_definition` | `symbolName`, `symbolType`, `filePath`, `targetDirectory`, `withReferences` | Returns exact source implementation snippet, line boundaries, signature, and doc comments. Supports `%` wildcards and live dirty buffer reading. |
| `find_symbol_callers` | `symbolName`, `targetDirectory` | Performs inverse graph lookup to return all caller invocation sites. |
| `search_codebase_graph` | `pattern`, `symbolType`, `language`, `targetDirectory`, `exactMatch`, `regex`, `limit` | Searches matching symbols across multi-language index using wildcard regex/SQL matching with relevance ordering and configurable result limits. |
| `get_supported_languages` | *None* | Discovers supported file extensions and active AST language parsers loaded on the CodeAtlas server instance. |
| `get_diff_impact` | `sinceCommit`, `targetDirectory` | Computes changed symbol set and downstream callers affected by uncommitted or recent git changes. |
| `get_servicestack_dto_contract` | `dtoName`, `targetDirectory` | Maps end-to-end ServiceStack C# DTOs to TypeScript types and consuming client components. |
| `prune_indexed_worktrees` | `dbDir`, `dryRun` | Scans global index database cache for non-existent repositories or git worktrees, purging obsolete `.db` files to reclaim disk space. |
| `sync_file` | `filePath`, `content`, `delete`, `targetDirectory` | Surgically updates or deletes a single file in the SQLite knowledge graph in milliseconds (< 20ms). |
| `sync_git_diff` | `targetDirectory`, `noIgnore` | Performs fast incremental delta sync of all git-modified and dirty files against the local SQLite index. |

---

## 5. Local Inter-Agent Relay Service Architecture

`CodeAtlas` includes a localized HTTP daemon (`scripts/relay/`) enabling real-time communication between decoupled AI agents (e.g. Antigravity/AGY and GitHub Copilot).

- **Port**: `http://localhost:9123`
- **Endpoints**:
  - `GET /health` — Health check & status.
  - `POST /send` — Send payload: `{"to": "copilot", "sender": "AGY", "body": "..."}`.
  - `GET /receive?client=copilot` — Poll inbox for messages.
- **Service Script Commands**:
  ```powershell
  # Install relay background daemon shortcut
  .\scripts\relay\Install-RelayService.ps1

  # Uninstall relay daemon
  .\scripts\relay\Uninstall-RelayService.ps1
  ```

---

## 6. Building & Dropping In a Custom Language Parser Plugin

To add support for a new programming language (e.g. Rust, Go, Python, SQL) as an independent drop-in plugin:

### Step 1: Create the Parser Project
Create a class library under `src/Parsers/CodeAtlas.Parsers.<Language>/` (or as a standalone third-party repository):

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <EnableDynamicLoading>true</EnableDynamicLoading>
  </PropertyGroup>

  <ItemGroup>
    <!-- Reference the standalone NuGet package or internal project -->
    <PackageReference Include="CodeAtlas.Abstractions" Version="1.0.0" />
  </ItemGroup>
</Project>
```

### Step 2: Implement `ILanguageParser`
```csharp
using CodeAtlas.Models;
using CodeAtlas.Parser;

namespace CodeAtlas.Parsers.Rust;

[ParserMetadata("rust", "Rust Parser", "1.0.0", Description = "Rust language AST symbol parser", Author = "Community Contributor")]
public sealed class RustParser : ILanguageParser
{
    public string Language => "rust";
    public IReadOnlyList<string> FileExtensions => [".rs"];

    public IEnumerable<Symbol> Parse(string filePath, string sourceCode)
    {
        // Parse AST and yield Symbol instances...
        yield break;
    }

    public IEnumerable<SymbolReference> ParseReferences(string filePath, string sourceCode)
    {
        // Extract cross-file references (calls, type usages, imports)...
        yield break;
    }
}
```

### Step 3: Drop into `Parsers/` Directory
Copy the compiled DLL and its dependencies into any of the search locations:
- **Application**: `<AppRoot>/Parsers/Rust/CodeAtlas.Parsers.Rust.dll`
- **User**: `%LOCALAPPDATA%/CodeAtlas/Parsers/Rust/CodeAtlas.Parsers.Rust.dll`
- **Environment**: `$env:CODEATLAS_PARSERS_DIR/Rust/CodeAtlas.Parsers.Rust.dll`

### Step 4: Verify with CLI
```powershell
.\publish\cli\CodeAtlas.Cli.exe parsers list
.\publish\cli\CodeAtlas.Cli.exe parsers info rust
```
The parser is immediately available for indexing across both the CLI utility and the background MCP Server daemon!

