# CodeGraphMcp — Usage Guide

`CodeGraphMcp` is a local code knowledge graph and indexer engine designed to parse, index, and query symbol definitions across multiple programming languages (C#, TypeScript, HTML, PowerShell).

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

The executable supports three primary commands: `index`, `query`, and `serve`.

```
CodeGraphMcp — local code knowledge graph engine

Usage:
  CodeGraphMcp [command] [options]

Commands:
  index <repo-path>  Parse and index a repository into the knowledge graph
  query <symbol-name> Query the knowledge graph for a symbol
  serve <repo-path>  Start the MCP stdio server
```

---

## 3. Indexing a Repository

The `index` command scans source files, extracts symbols (classes, interfaces, methods, functions, tags), and stores them in a local SQLite database.

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

### Incremental Delta Indexing
- If the repository is a **Git repository**, `CodeGraphMcp` records the HEAD commit hash.
- Subsequent `index` runs inspect `git diff` against the recorded commit and **only re-parse changed or added files**, resulting in sub-100ms updates.

---

## 4. Querying Symbols

The `query` command searches for symbol definitions by name across the indexed codebase.

### Usage

```powershell
# Query current directory
dotnet run --project src/CodeGraphMcp -- query "IndexOrchestrator" --store-local

# Query a specific project directory from anywhere
dotnet run --project src/CodeGraphMcp -- query "MyClass" --repo "C:\Projects\MyOtherApp" --store-local

# Or using the published binary
.\publish\CodeGraphMcp.exe query "MyClass" -r "C:\Projects\MyOtherApp" -l
```

### Command Options

| Flag | Short | Description |
| :--- | :--- | :--- |
| `<symbol-name>` | — | **Required.** Name of the class, interface, method, or function to look up. |
| `--repo` | `-r` | Target repository directory to query (defaults to current working directory). |
| `--store-local` | `-l` | Uses `.agent-index/codegraph.db` inside the specified target repo root. |
| `--db-dir` | — | Override directory location where SQLite database file was saved. |

### Features
1. **Live Uncommitted Check**: The query tool automatically checks for uncommitted changes on disk (dirty buffer). If a symbol is found in an uncommitted modification, it labels the result as `[LIVE - uncommitted]`.
2. **Database Lookup**: If no uncommitted changes match, it returns matched symbols stored in the SQLite database index.

### Sample Output
```text
[Class] IndexOrchestrator | src\CodeGraphMcp\Indexer\IndexOrchestrator.cs:9-97
```

---

## 5. Supported Languages & Files

`CodeGraphMcp` currently includes AST parsers for:
- **C#**: `.cs` (Classes, Interfaces, Structs, Enums, Methods, Record definitions)
- **TypeScript / JavaScript**: `.ts`, `.tsx`, `.js`, `.jsx` (Functions, Classes, Interfaces, Exports)
- **HTML**: `.html`, `.htm` (IDs, Elements, Form Controls)
- **PowerShell**: `.ps1`, `.psm1` (Functions, Scripts)

---

## 6. MCP Server Mode (`serve`)

Starts the stdio JSON-RPC server for integration with AI assistants and IDE extensions (Model Context Protocol).

```powershell
dotnet run --project src/CodeGraphMcp -- serve "C:\path\to\your\repository" --store-local
```

---

## 7. Storage & Configuration Details

- Default DB Path: `%LOCALAPPDATA%/CodeGraphMcp/codegraph.db` (if `--store-local` or `--db-dir` are not specified).
- Local DB Path (`--store-local`): `<repo-root>/.agent-index/codegraph.db`.
- Database Engine: SQLite (managed via Microsoft.Data.Sqlite and Dapper).
