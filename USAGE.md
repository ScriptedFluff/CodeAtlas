# CodeGraphMcp — Usage Guide

<!-- Updated 2026-08-10 -->
`CodeGraphMcp` is a local code knowledge graph and indexer engine designed to parse, index, link reference graphs, cross-stack contracts, and query symbol definitions across multiple programming languages (C#, TypeScript, HTML, PowerShell).

---

## 1. Quick Start

### Building the Project
```powershell
# Build debug version
dotnet build

# Publish MCP Server daemon binary to ./publish/server
dotnet publish src/CodeGraphMcp.Server/CodeGraphMcp.Server.csproj -c Release -o ./publish/server

# Publish CLI binary to ./publish/cli
dotnet publish src/CodeGraphMcp.Cli/CodeGraphMcp.Cli.csproj -c Release -o ./publish/cli
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
dotnet run --project src/CodeGraphMcp.Cli -- index "C:\path\to\your\repository"

# Using published binary
.\publish\cli\CodeGraphMcp.Cli.exe index "C:\path\to\your\repository"
```

### Command Options

| Flag | Short | Description |
| :--- | :--- | :--- |
| `<repo-path>` | — | **Required.** Path to the repository root directory. |
| `--db-dir` | — | Override folder location where SQLite database file is created (defaults to `<repo>/.codegraph/`). |
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
dotnet run --project src/CodeGraphMcp.Cli -- query "IndexOrchestrator"

# Query a specific project directory from anywhere with incoming references/call sites
dotnet run --project src/CodeGraphMcp.Cli -- query "Individual" --repo "D:\Work\Clever Services\21959" --referenced

# Or using the published binary with --with-references alias
.\publish\cli\CodeGraphMcp.Cli.exe query "MyClass" -r "C:\Projects\MyOtherApp" --with-references
```

### Command Options

| Flag | Short / Alias | Description |
| :--- | :--- | :--- |
| `<symbol-name>` | — | **Required.** Name of the symbol to look up (supports `%` wildcards). |
| `--repo` | `-r` | Target repository directory to query (defaults to current working directory). |
| `--type` | `-t` | Filter results by symbol type (e.g. `Class`, `Method`, `Interface`, `Property`, `Enum`, `Function`). |
| `--language` | `--lang` | Filter results by programming language (e.g. `csharp`, `typescript`, `html`, `json`, `yaml`, `css`, `powershell`). |
| `--referenced` | `--with-references` | Display incoming callers/reference sites grouped under matched symbols in an indented tree view. |
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
dotnet run --project src/CodeGraphMcp.Cli -- status "C:\path\to\your\repository"
```

### Sample Output
```text
Repository:       C:\Projects\CodeGraphMcp
Database:         C:\Projects\CodeGraphMcp\.codegraph\code_graph.db
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
dotnet run --project src/CodeGraphMcp.Cli -- watch "C:\path\to\your\repository" --interval 30
```

### Options
| Flag | Description |
| :--- | :--- |
| `<repo-path>` | **Required.** Repository root path to watch. |
| `--interval` | Seconds between index checks (default: `30`). |

---

## 7. Cleaning Index Database (`clean`)

Deletes the SQLite database file and associated Write-Ahead Log (WAL) files for a repository index.

### Usage
```powershell
dotnet run --project src/CodeGraphMcp.Cli -- clean "C:\path\to\your\repository" --yes
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
- **JSON / Manifests**: `.json`, `package.json`, `appsettings.json` (Keys, Objects, Dependencies)
- **YAML / Pipelines**: `.yaml`, `.yml` (Keys, Jobs, Pipeline Configurations)
- **CSS / Styles**: `.css` (Rule Sets, Selectors, Classes)

---

## 9. MCP Server Mode (`serve`)

Starts the Model Context Protocol (MCP) server over standard input/output (stdio JSON-RPC). This allows AI coding assistants (such as Claude Desktop, AGY, VSCode, Cursor) to interactively query the local code knowledge graph.

### Usage

```powershell
# Run MCP daemon server directly
dotnet run --project src/CodeGraphMcp.Server

# Run bound to a specific repository
dotnet run --project src/CodeGraphMcp.Server -- --repo "C:\path\to\your\repository"

# Using published server binary
.\publish\server\CodeGraphMcp.Server.exe
```

### Command Options

| Flag | Short | Description |
| :--- | :--- | :--- |
| `--repo` | `-r` | Optional default target repository directory. If omitted, tools accept an explicit `targetDirectory` parameter or default to the active working directory. |
| `--db-dir` | — | Override folder location for SQLite database files. |

### MCP Tools Provided

The MCP server exposes 7 specialized tools to AI clients:

#### 1. `get_symbol_definition`
Retrieves the exact source code snippet, precise line range (StartLine to EndLine), signature, and doc comments for a symbol. Automatically scopes to active workspace project.
- **Parameters:**
  - `symbolName`: Name of the symbol to look up (supports SQL LIKE `%` wildcards).
  - `symbolType`: Optional filter (`Class`, `Method`, `Interface`, `Property`, `Enum`, `Function`).
  - `filePath`: Optional relative path substring filter.
  - `targetDirectory`: Optional workspace root override.
  - `withReferences`: Set `true` to include incoming caller sites.
- **Features:** Reads directly from dirty uncommitted buffer if file has unsaved edits on disk.

#### 2. `find_symbol_callers`
Queries the symbol reference graph to find all caller invocations across the project.
- **Parameters:**
  - `symbolName`: Exact name of symbol.
  - `targetDirectory`: Optional workspace root override.

#### 3. `search_codebase_graph`
Searches indexed symbols matching name patterns with optional filters. Automatically scopes to workspace index without requiring explicit path filtering.
- **Parameters:**
  - `pattern`: Symbol query string (e.g. `UserService` or `User`). Substrings match automatically; `%` wildcards are optional.
  - `symbolType`: Optional type filter.
  - `language`: Optional language filter (`csharp`, `typescript`, `html`, `powershell`, `json`, `yaml`, `css`).
  - `targetDirectory`: Optional workspace root override.
  - `exactMatch`: Optional boolean (`true` to enforce exact case-insensitive symbol name matching).
  - `regex`: Optional regular expression string to post-filter symbol names.
  - `limit`: Optional integer result count cap (default 20, pass higher limit or 0 for expanded/unlimited up to 500 max cap).

#### 4. `get_supported_languages`
Discovers supported file extensions and active AST parsers on the CodeGraphMcp server instance.
- **Parameters:** None.

#### 5. `get_diff_impact`
Analyzes git change impact and dirty files to compute the blast radius before refactoring.
- **Parameters:**
  - `sinceCommit`: Optional commit hash to compare against.
  - `targetDirectory`: Optional workspace root override.

#### 6. `get_servicestack_dto_contract`
Extracts cross-stack mapping between C# request/response DTOs, TypeScript interfaces, and consuming Angular components for ServiceStack endpoints.
- **Parameters:**
  - `dtoName`: C# DTO class name (e.g. `GetUserBillingRequest`).
  - `targetDirectory`: Optional workspace root override.

#### 7. `prune_indexed_worktrees`
Scans the global index storage cache for database files whose repository roots or git worktrees no longer exist on disk, purging obsolete `.db` files and freeing disk space. Runs automatically on startup.
- **Parameters:**
  - `dbDir`: Optional override path to database cache directory.
  - `dryRun`: Set `true` to preview candidate databases to prune without performing actual deletion.

### MCP Configuration Example (`mcp_config.json`)

```json
{
  "mcpServers": {
    "codegraph": {
      "command": "C:\\path\\to\\CodeGraphMcp\\publish\\server\\CodeGraphMcp.Server.exe",
      "args": []
    }
  }
}
```

---

## 10. Storage & Configuration Details

- **Default Database Path**: `<repo-root>/.codegraph/code_graph.db` (unless `--db-dir` override is specified).
- **Database Engine**: SQLite (managed via `Microsoft.Data.Sqlite` and `Dapper`).

### Repository & Custom Ignore / Include Configurations

`CodeGraphMcp` automatically loads ignore and include rules from `.codegraph/settings.json` or `appsettings.json` to customize indexing targets and exclude heavy auto-generated data, test outputs, or lockfiles:

1. **Repository Settings Config (`.codegraph/settings.json`)**:
   Place a `settings.json` in your repository `.codegraph/` directory to declare project-specific include patterns, ignore patterns, or directory exclusions:
   ```json
   {
     "Include": [
       "*"
     ],
     "Ignore": [
       "**/*test*/**/*.json",
       "**/*Test*/**/*.json"
     ],
     "IgnoreDirectories": [
       "test-fixtures"
     ]
   }
   ```

2. **`Include` & `Ignore` Rule Behavior**:
   - **`Include` Array Rules**: If `"Include"` is missing, empty (`[]`), or contains `"*"` anywhere in the list, `CodeGraphMcp` ignores the include filter and processes all supported codebase files. If specific glob patterns are declared (e.g. `["src/**/*.cs", "*.json"]`), indexing will only parse files matching at least one include pattern.
   - **`Ignore` Wildcard Warnings**: If `"Ignore"` contains wildcard `"*"` or `"**"`, `CodeGraphMcp` emits an explicit console warning (`Ignore configuration contains wildcard '*' — all files have been filtered out`) to prevent confusion when 0 files are indexed.

3. **Global Application Config (`appsettings.json`)**:
   Built-in defaults inside `appsettings.json` ignore common package lockfiles and output directories by default:
   - **Default Ignore Patterns**: `package-lock.json`, `pnpm-lock.yaml`, `yarn.lock`, `*.min.json`, `*.min.js`, `*.min.css`, `*.map`.
   - **Default Ignore Directories**: `.codegraph`, `node_modules`, `bin`, `obj`, `dist`, `.next`, `coverage`, `.vs`, `packages`, `publish`.

4. **Config Loading Hierarchy**:
   - `.codegraph/settings.json` (Repository .codegraph directory)
   - `appsettings.json` (Repository root or executable output directory)
   - `.gitignore` rules (Git root and subdirectories)

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


