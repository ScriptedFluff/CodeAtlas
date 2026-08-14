# CodeGraphMcp — Developer Technical Specification & Command Architecture

`CodeGraphMcp` is a high-performance local code knowledge graph indexer, reference graph analysis engine, and Model Context Protocol (MCP) server designed for software architects and senior engineers. It parses multi-language source trees (C#, TypeScript, JavaScript, HTML, PowerShell), extracts syntax trees into relational SQLite graphs, tracks Git deltas for sub-second incremental indexing, performs cross-stack ServiceStack DTO linkage, and surfaces dirty uncommitted file modifications live to AI coding assistants.

---

## 1. Core Architecture & Subsystems Overview

```
                          ┌──────────────────────────┐
                          │   CLI / MCP Interface    │
                          └─────────────┬────────────┘
                                        │
             ┌──────────────────────────┼──────────────────────────┐
             ▼                          ▼                          ▼
   ┌──────────────────┐       ┌──────────────────┐       ┌──────────────────┐
   │ CLI Command Loop │       │ Stdio MCP Server │       │  Watcher Service │
   └─────────┬────────┘       └─────────┬────────┘       └─────────┬────────┘
             │                          │                          │
             └──────────────────────────┼──────────────────────────┘
                                        ▼
                           ┌────────────────────────┐
                           │   Index Orchestrator   │
                           └───────────┬────────────┘
                                       │
            ┌──────────────────────────┼──────────────────────────┐
            ▼                          ▼                          ▼
 ┌─────────────────────┐    ┌─────────────────────┐    ┌─────────────────────┐
 │ Git Delta Analyzer  │    │ AST Parser Pipeline │    │ Uncommitted Buffer  │
 │ (HEAD tracking)     │    │ (Roslyn/TreeSitter) │    │  (Dirty file sync)  │
 └──────────┬──────────┘    └──────────┬──────────┘    └──────────┬──────────┘
            │                          │                          │
            └──────────────────────────┼──────────────────────────┘
                                       ▼
                         ┌──────────────────────────┐
                         │  SQLite Knowledge Graph  │
                         │    (WAL + Dapper DB)     │
                         └──────────────────────────┘
```

### Core Architecture Components

1. **AST Parsing Pipeline (`CodeGraphMcp.Parser`)**:
   - **C#**: Roslyn-backed syntax walker extracting classes, interfaces, structs, enums, methods, properties, and invocation expressions.
   - **TypeScript/JavaScript**: Tree-sitter / custom AST lexer extracting functions, classes, exports, interfaces, and call graphs.
   - **HTML**: DOM node scanner indexing element IDs, component tags, and input controls.
   - **PowerShell**: Abstract Syntax Tree (`System.Management.Automation.Language`) visitor extracting functions, parameters, and invocation chains.
2. **Git Delta Tracker (`CodeGraphMcp.Git`)**:
   - Interrogates `git diff` against saved index commit hash (`last_indexed_commit`).
   - Limits re-indexing to modified, added, or deleted files, avoiding full re-scans.
3. **Relational Knowledge Graph (`CodeGraphMcp.Db`)**:
   - SQLite engine operating with Write-Ahead Logging (`WAL` mode) and pragma optimizations via `Dapper`.
   - Indexed tables for `files`, `symbols`, `references`, and `dto_mappings`.
4. **Live Dirty Buffer Interceptor**:
   - When executing symbol or reference queries via MCP or CLI, uncommitted edits on disk override stale SQLite records in memory for zero-latency accuracy.
5. **Model Context Protocol Server (`CodeGraphMcp.Mcp`)**:
   - Implements JSON-RPC 2.0 over standard I/O streams (`stdio`).
   - Exposes specialized knowledge graph tools directly to LLMs (Claude, AGY/Antigravity, Cursor, Copilot).

---

## 2. Technical Deep-Dive into Commands & Workflows

### 2.1 Index Command (`index`)
Parses, indexes, and builds symbol/reference graphs for a target repository.

- **Developer Context**: Executes file discovery, runs multi-threaded AST parsing, calculates content hash fingerprints, resolves internal cross-references, and commits symbol trees to the SQLite store.
- **Incremental Logic**: Performs git commit inspection. If HEAD equals `last_indexed_commit` and no uncommitted files exist, execution terminates instantly (<20ms).
- **Execution Options**:
  - `<repo-path>` *(Required)*: Target repository directory.
  - `--db-dir <path>`: Custom location for the SQLite database (defaults to `<repo-path>/.codegraph/`).
  - `--force-full`: Forces full parse cycle, bypassing Git delta detection.
  - `--no-ignore`: Ignores `.gitignore` rules to index hidden or build-generated source files.

#### Examples:
```powershell
# Standard local repository index
CodeGraphMcp index "D:\Projects\C#\CodeGraphMcp"

# Force complete re-index overriding git deltas and ignore rules
CodeGraphMcp index "C:\Services\PaymentGateway" --force-full --no-ignore

# Store index in custom central path
CodeGraphMcp index "C:\Repo" --db-dir "C:\SharedIndexes\RepoDb"
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
CodeGraphMcp query "IndexOrchestrator"

# Query specific project repository from an external directory
CodeGraphMcp query "PaymentProcessor" --repo "D:\Projects\Services\Billing"
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
CodeGraphMcp status "D:\Projects\C#\CodeGraphMcp"
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
CodeGraphMcp watch "D:\Projects\C#\CodeGraphMcp" --interval 15
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
CodeGraphMcp clean "D:\Projects\C#\CodeGraphMcp" --yes
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
CodeGraphMcp serve

# Launch repository-bound MCP server using local index
CodeGraphMcp serve --repo "D:\Projects\C#\CodeGraphMcp"
```

---

## 3. Developer Use Cases & Workflows

### Use Case A: Refactoring Impact & Blast Radius Analysis
Before renaming or refactoring core methods, developers can use `get_diff_impact` and `find_symbol_callers` to evaluate downstream dependencies.

**Workflow Command Example:**
```powershell
# Index project first
CodeGraphMcp index "C:\Projects\CoreEngine"

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
When editing files in an IDE, AI agents query the MCP server. `CodeGraphMcp` reads the dirty file buffer directly from disk, delivering up-to-the-second symbol line ranges even before saving to disk.

---

## 4. MCP Server Tool Reference

| Tool Name | Key Parameters | Developer Description |
| :--- | :--- | :--- |
| `get_symbol_definition` | `symbolName`, `symbolType`, `filePath`, `targetDirectory` | Returns exact source implementation snippet, line boundaries, signature, and doc comments. Supports `%` wildcards. |
| `find_symbol_callers` | `symbolName`, `targetDirectory` | Performs inverse graph lookup to return all caller invocation sites. |
| `search_codebase_graph` | `pattern`, `symbolType`, `language`, `targetDirectory`, `exactMatch`, `regex`, `limit` | Searches matching symbols across multi-language index using wildcard regex/SQL matching with relevance ordering and configurable result limits. |
| `get_diff_impact` | `sinceCommit`, `targetDirectory` | Computes changed symbol set and downstream callers affected by uncommitted or recent git changes. |
| `get_servicestack_dto_contract` | `dtoName`, `targetDirectory` | Maps end-to-end ServiceStack C# DTOs to TypeScript types and consuming client components. |

---

## 5. Local Inter-Agent Relay Service Architecture

`CodeGraphMcp` includes a localized HTTP daemon (`scripts/relay/`) enabling real-time communication between decoupled AI agents (e.g. Antigravity/AGY and GitHub Copilot).

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
