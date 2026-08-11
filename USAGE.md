# CodeGraphMcp — Usage Guide

<!-- Updated 2026-08-10 -->
`CodeGraphMcp` is a local code knowledge graph and indexer engine designed to parse, index, link reference graphs, cross-stack contracts, and query symbol definitions across multiple programming languages (C#, TypeScript, HTML, PowerShell).

---

## 1. Quick Start

### Building the Project
```powershell
# Build debug version
dotnet build

# Or publish a self-contained release folder
dotnet publish src/CodeGraphMcp/CodeGraphMcp.csproj -c Release -o ./publish
```

---

## 2. CLI Commands Overview

The executable supports six primary commands: `index`, `query`, `status`, `watch`, `clean`, and `serve`.

```
CodeGraphMcp — local code knowledge graph engine

Usage:
  CodeGraphMcp [command] [options]

Commands:
  index <repo-path>  Parse and index a repository into the knowledge graph
  query <symbol-name> Query the knowledge graph for a symbol
  status <repo-path> Show the current index state for a repository
  watch <repo-path>  Continuously re-index on git changes
  clean <repo-path>  Delete the index database for a repository
  serve              Start the MCP stdio server
```

---

## 3. Indexing a Repository (`index`)

The `index` command scans source files, extracts symbols (classes, interfaces, methods, properties, functions, HTML elements) and references, and stores them in a local SQLite database.

### Usage
```powershell
# Using dotnet run
dotnet run --project src/CodeGraphMcp -- index "C:\path\to\your\repository" --store-local

# Using published binary
.\publish\CodeGraphMcp.exe index "C:\path\to\your\repository" --store-local
```

### Command Options

| Flag | Short | Description |
| :--- | :--- | :--- |
| `<repo-path>` | — | **Required.** Path to the repository root directory. |
| `--store-local` | `-l` | Stores SQLite DB inside `<repo-path>/.agent-index/codegraph.db`. |
| `--db-dir` | — | Override folder location where SQLite database file is created. |
| `--force-full` | — | Bypass git delta detection and re-parse/re-index every file. |
| `--no-ignore` | — | Bypass `.gitignore` rules and index all matching source files. |

### Incremental Delta Indexing
- If the repository is a **Git repository**, `CodeGraphMcp` records the HEAD commit hash.
- Subsequent `index` runs inspect `git diff` against the recorded commit and **only re-parse changed or added files**, resulting in sub-100ms updates.

---

## 4. Querying Symbols (`query`)

The `query` command searches for symbol definitions by name across the indexed codebase.

### Usage

```powershell
# Query current directory
dotnet run --project src/CodeGraphMcp -- query "IndexOrchestrator" --store-local

# Query a specific project directory from anywhere with incoming references/call sites
dotnet run --project src/CodeGraphMcp -- query "Individual" --repo "D:\Work\Clever Services\21959" --referenced

# Or using the published binary with --with-references alias
.\publish\CodeGraphMcp.exe query "MyClass" -r "C:\Projects\MyOtherApp" -l --with-references
```

### Command Options

| Flag | Short / Alias | Description |
| :--- | :--- | :--- |
| `<symbol-name>` | — | **Required.** Name of the class, interface, method, or function to look up. |
| `--repo` | `-r` | Target repository directory to query (defaults to current working directory). |
| `--referenced` | `--with-references` | Display incoming callers/reference sites grouped under matched symbols in an indented tree view. |
| `--store-local` | `-l` | Uses `.agent-index/codegraph.db` inside the specified target repo root. |
| `--db-dir` | — | Override directory location where SQLite database file was saved. |

### Features
1. **Live Uncommitted Check**: The query tool automatically checks for uncommitted changes on disk (dirty buffer). If a symbol is found in an uncommitted modification, it labels the result as `[LIVE - uncommitted]`.
2. **Database Lookup**: If no uncommitted changes match, it returns matched symbols stored in the SQLite database index.
3. **Reference Call Site Tree**: Adding `--referenced` (or `--with-references`) prints call sites, parameter usages, and type instantiations directly under the symbol definition.

### Sample Output
```text
[Class] IndexOrchestrator | src\CodeGraphMcp\Indexer\IndexOrchestrator.cs:9-97
```

---

## 5. Repository Status (`status`)

Displays statistics and current state of the knowledge graph database for a repository.

### Usage
```powershell
dotnet run --project src/CodeGraphMcp -- status "C:\path\to\your\repository" --store-local
```

### Sample Output
```text
Repository:       C:\Projects\CodeGraphMcp
Database:         C:\Projects\CodeGraphMcp\.agent-index\codegraph.db
Branch:           main
HEAD:             a1b2c3d4e5f6
Last Indexed:     a1b2c3d4e5f6
Needs Re-index:   no
Files Indexed:    42
Symbols Indexed:  350
References:       1200
```

---

## 6. Continuous Index Watcher (`watch`)

Monitors the repository for git changes at a scheduled interval and incrementally updates the index when changes are detected.

### Usage
```powershell
dotnet run --project src/CodeGraphMcp -- watch "C:\path\to\your\repository" --store-local --interval 30
```

### Options
| Flag | Description |
| :--- | :--- |
| `<repo-path>` | **Required.** Repository root path to watch. |
| `--interval` | Seconds between index checks (default: `30`). |
| `--store-local` | Use `.agent-index/codegraph.db` inside repository root. |

---

## 7. Cleaning Index Database (`clean`)

Deletes the SQLite database file and associated Write-Ahead Log (WAL) files for a repository index.

### Usage
```powershell
dotnet run --project src/CodeGraphMcp -- clean "C:\path\to\your\repository" --store-local --yes
```

### Options
| Flag | Short | Description |
| :--- | :--- | :--- |
| `<repo-path>` | — | **Required.** Repository root path. |
| `--force` | `-f` | Skip prompt confirmation. |
| `--yes` | `-y` | Skip prompt confirmation. |

---

## 8. Supported Languages & Files

`CodeGraphMcp` includes custom AST parsers for:
- **C#**: `.cs` (Classes, Interfaces, Structs, Enums, Methods, Properties, Record definitions)
- **TypeScript / JavaScript**: `.ts`, `.tsx`, `.js`, `.jsx` (Functions, Classes, Interfaces, Exports)
- **HTML**: `.html`, `.htm` (IDs, Elements, Form Controls)
- **PowerShell**: `.ps1`, `.psm1` (Functions, Scripts)

---

## 9. MCP Server Mode (`serve`)

Starts the Model Context Protocol (MCP) server over standard input/output (stdio JSON-RPC). This allows AI coding assistants (such as Claude Desktop, AGY, VSCode, Cursor) to interactively query the local code knowledge graph.

### Usage

```powershell
# Dynamic unbound mode (resolves repositories on-the-fly)
dotnet run --project src/CodeGraphMcp -- serve

# Bound mode targeting a specific repository
dotnet run --project src/CodeGraphMcp -- serve -r "C:\path\to\your\repository" --store-local

# Using published binary
.\publish\CodeGraphMcp.exe serve -r "C:\path\to\your\repository" --store-local
```

### Command Options

| Flag | Short | Description |
| :--- | :--- | :--- |
| `--repo` | `-r` | Optional default target repository directory. If omitted, tools accept an explicit `targetDirectory` parameter or default to the active working directory. |
| `--store-local` | `-l` | Use `.agent-index/codegraph.db` inside the specified repository root. Requires `--repo`. |
| `--db-dir` | — | Override folder location for SQLite database files. |

### MCP Tools Provided

The MCP server exposes 5 specialized tools to AI clients:

#### 1. `get_symbol_definition`
Retrieves the exact source code snippet, line numbers, signature, and doc comments for a symbol.
- **Parameters:**
  - `symbolName`: Name of the symbol to look up (supports SQL LIKE `%` wildcards).
  - `symbolType`: Optional filter (`Class`, `Method`, `Interface`, `Property`, `Enum`, `Function`).
  - `filePath`: Optional relative path substring filter.
  - `targetDirectory`: Optional workspace root override.
- **Features:** Reads directly from dirty uncommitted buffer if file has unsaved edits on disk.

#### 2. `find_symbol_callers`
Queries the symbol reference graph to find all caller invocations across the project.
- **Parameters:**
  - `symbolName`: Exact name of symbol.
  - `targetDirectory`: Optional workspace root override.

#### 3. `search_codebase_graph`
Searches indexed symbols matching name patterns with optional filters.
- **Parameters:**
  - `pattern`: Symbol pattern using `%` wildcards (e.g. `GetUser%`).
  - `symbolType`: Optional type filter.
  - `language`: Optional language filter (`csharp`, `typescript`, `html`, `powershell`).
  - `targetDirectory`: Optional workspace root override.

#### 4. `get_diff_impact`
Analyzes git change impact and dirty files to compute the blast radius before refactoring.
- **Parameters:**
  - `sinceCommit`: Optional commit hash to compare against.
  - `targetDirectory`: Optional workspace root override.

#### 5. `get_servicestack_dto_contract`
Extracts cross-stack mapping between C# request/response DTOs, TypeScript interfaces, and consuming Angular components for ServiceStack endpoints.
- **Parameters:**
  - `dtoName`: C# DTO class name (e.g. `GetUserBillingRequest`).
  - `targetDirectory`: Optional workspace root override.

### MCP Configuration Example (`mcp_config.json`)

```json
{
  "mcpServers": {
    "codegraph": {
      "command": "C:\\path\\to\\CodeGraphMcp\\publish\\CodeGraphMcp.exe",
      "args": [
        "serve"
      ]
    }
  }
}
```

---

## 10. Storage & Configuration Details

- **Default Database Path**: `%LOCALAPPDATA%/CodeGraphMcp/codegraph.db` (if `--store-local` or `--db-dir` are omitted).
- **Local Database Path (`--store-local`)**: `<repo-root>/.agent-index/codegraph.db`.
- **Database Engine**: SQLite (managed via `Microsoft.Data.Sqlite` and `Dapper`).

---

## 11. Local Inter-Agent Relay Service

For seamless communication between **AGY (Antigravity)** and **GitHub Copilot** without triggering repetitive IDE workspace file access security prompts, use the PowerShell relay service located in `scripts/relay/`.

### Installation & Execution
```powershell
# Run the user-level setup script
.\scripts\relay\Install-RelayService.ps1
```
This creates a zero-flash startup shortcut at `%APPDATA%\Microsoft\Windows\Start Menu\Programs\Startup\CodeGraphRelay.lnk` and starts the daemon listening at `http://localhost:9123/`.

### REST Endpoints
- **Health Check**: `GET http://localhost:9123/health`
- **Send Message**: `POST http://localhost:9123/send` (`{"to": "copilot", "sender": "AGY", "body": "..."}`)
- **Receive Message**: `GET http://localhost:9123/receive?client=copilot`

### Uninstallation
```powershell
.\scripts\relay\Uninstall-RelayService.ps1
```


