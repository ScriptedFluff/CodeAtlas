# CodeGraphMcp — MCP Configuration Guide

This guide details how to configure and integrate `CodeGraphMcp` as a Model Context Protocol (MCP) server across various AI assistants, IDEs, and client applications.

---

## 1. Prerequisites & Setup

Before configuring your AI client, publish a release binary of `CodeGraphMcp` for fast startup times:

```powershell
# Publish self-contained executable to ./publish
dotnet publish src/CodeGraphMcp/CodeGraphMcp.csproj -c Release -o ./publish
```

The compiled executable will be located at:
`D:\Projects\C#\CodeGraphMcp\publish\CodeGraphMcp.exe` (or `dotnet run --project ...` during development).

---

## 2. Server Execution Modes

`CodeGraphMcp` supports two primary execution modes in MCP server mode (`serve`):

### Mode A: Dynamic / Unbound Mode (Recommended)
In this mode, no target repository is hardcoded at server launch. The server will resolve repository paths dynamically when tools are invoked, using either an explicit `targetDirectory` argument or defaulting to the client's current active workspace directory.

```powershell
CodeGraphMcp.exe serve
```

### Mode B: Bound Repository Mode
In this mode, the server is locked to a specific repository path. Every tool query automatically targets this index unless explicitly overridden.

```powershell
CodeGraphMcp.exe serve -r "C:\Projects\MyRepo" --store-local
```

---

## 3. Client Configuration Examples

### A. Antigravity / Gemini AI (`.gemini/config/mcp_config.json` or workspace `mcp_config.json`)

Add the following entry to your `mcp_config.json`:

```json
{
  "mcpServers": {
    "codegraph": {
      "command": "C:\\Projects\\CodeGraphMcp\\publish\\CodeGraphMcp.exe",
      "args": [
        "serve"
      ]
    }
  }
}
```

If targeting a fixed repository with local database storage:

```json
{
  "mcpServers": {
    "codegraph": {
      "command": "C:\\Projects\\CodeGraphMcp\\publish\\CodeGraphMcp.exe",
      "args": [
        "serve",
        "-r",
        "C:\\Projects\\TargetApp",
        "-l"
      ]
    }
  }
}
```

---

### B. Claude Desktop (`%APPDATA%\Claude\claude_desktop_config.json`)

```json
{
  "mcpServers": {
    "codegraph": {
      "command": "C:\\Projects\\CodeGraphMcp\\publish\\CodeGraphMcp.exe",
      "args": [
        "serve"
      ]
    }
  }
}
```

---

### C. Cursor (`.cursor/mcp.json` or Cursor Settings -> Features -> MCP)

**Name:** `codegraph`  
**Type:** `command`  
**Command:** `C:\Projects\CodeGraphMcp\publish\CodeGraphMcp.exe serve`

Or in `.cursor/mcp.json`:

```json
{
  "mcpServers": {
    "codegraph": {
      "command": "C:\\Projects\\CodeGraphMcp\\publish\\CodeGraphMcp.exe",
      "args": ["serve"]
    }
  }
}
```

---

### D. VSCode (Cline / Roo Code / MCP Extension)

In your MCP settings (`cline_mcp_settings.json`):

```json
{
  "mcpServers": {
    "codegraph": {
      "command": "C:\\Projects\\CodeGraphMcp\\publish\\CodeGraphMcp.exe",
      "args": ["serve"],
      "disabled": false,
      "autoApprove": []
    }
  }
}
```

---

## 4. Command Line Reference (`serve`)

| Argument / Flag | Short | Description |
| :--- | :--- | :--- |
| `--repo` | `-r` | Default target repository path. Omit for dynamic unbound mode. |
| `--store-local` | `-l` | Store database in `<repo-path>/.agent-index/codegraph.db`. Requires `--repo`. |
| `--db-dir` | — | Explicit directory override for SQLite database location. |

---

## 5. Available MCP Tools

Once connected, AI assistants gain access to these 5 tools:

| Tool Name | Purpose | Key Parameters |
| :--- | :--- | :--- |
| `get_symbol_definition` | Retrieves exact source code, line range, signature, and doc comments for a symbol. Reads dirty buffer if unsaved changes exist on disk. | `symbolName`, `symbolType`, `filePath`, `targetDirectory` |
| `find_symbol_callers` | Queries symbol reference graph to find all caller invocations across the project. | `symbolName`, `targetDirectory` |
| `search_codebase_graph` | Wildcard symbol pattern search (`%`) with type and language filtering. | `pattern`, `symbolType`, `language`, `targetDirectory` |
| `get_diff_impact` | Analyzes git diffs and dirty buffer to evaluate blast radius before refactoring. | `sinceCommit`, `targetDirectory` |
| `get_servicestack_dto_contract` | Resolves cross-stack C# DTO -> TypeScript interface -> Angular component mappings. | `dtoName`, `targetDirectory` |

---

## 6. Troubleshooting & Diagnostics

- **Clean Stdio Pipe:** All application logs and diagnostic output are routed exclusively to `stderr` (with LogLevel set to `Warning`). `stdout` is dedicated entirely to JSON-RPC protocol transport.
- **Index Database Missing:** If a tool returns a message indicating no index database was found, run the `index` CLI command first:
  ```powershell
  CodeGraphMcp.exe index "C:\Projects\TargetApp" --store-local
  ```
- **Database Locks:** `CodeGraphMcp` uses SQLite in Write-Ahead Logging (`WAL`) mode with read-only connection pooling during queries to prevent database locking errors.
