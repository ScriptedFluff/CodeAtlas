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

Once connected, AI assistants gain access to these 7 tools:

| Tool Name | Purpose | Key Parameters |
| :--- | :--- | :--- |
| `get_symbol_definition` | Retrieves exact source code, precise line range (StartLine to EndLine), signature, and doc comments for a symbol. Accepts `withReferences: true` to include incoming call sites. Reads dirty buffer if unsaved changes exist on disk. Automatically scopes to active project index. | `symbolName`, `symbolType`, `filePath`, `targetDirectory`, `withReferences` |
| `find_symbol_callers` | Queries symbol reference graph to find all caller invocations across the project. | `symbolName`, `targetDirectory` |
| `search_codebase_graph` | Wildcard symbol pattern search (`%`) with type and language filtering. Supports `exactMatch: true` for exact symbol name matching, `regex: "..."` filtering, and `limit` (default 20, 0 for expanded/unlimited up to 500). Auto-scopes to workspace. | `pattern`, `symbolType`, `language`, `targetDirectory`, `exactMatch`, `regex`, `limit` |
| `get_supported_languages` | Discovers supported file extensions and language parsers actively enabled on the CodeGraphMcp server. | *None* |
| `get_diff_impact` | Analyzes git diffs and dirty buffer to evaluate blast radius before refactoring. | `sinceCommit`, `targetDirectory` |
| `get_servicestack_dto_contract` | Resolves cross-stack C# DTO -> TypeScript interface -> Angular component mappings. | `dtoName`, `targetDirectory` |
| `prune_indexed_worktrees` | Scans index storage registry for database caches whose underlying repository folders or git worktrees no longer exist on disk, purging obsolete `.db` cache files and freeing disk space. Runs automatically on startup. | `dbDir`, `dryRun` |

---

## 6. Enforcing Tool Priority Over `grep` and `Get-ChildItem`

When an AI assistant has access to multiple tools, standard LLM tool-selection heuristics often default to native built-ins like `grep_search` or CLI terminal commands (`Get-ChildItem`, `dir`, `grep`) due to tool registration prominence or false assumptions about indexing latency.

To ensure AI assistants consistently prioritize `CodeGraphMcp` over text-search and terminal probing, configure system prompt interception rules for your AI engine:

### A. GitHub Copilot (`.copilot/copilot-instructions.md`)

Use a **STEP 0 — INPUT INTERception** pattern to force search routing before any tool calls or command executions:

```markdown
## STEP 0 — CODEBASE SEARCH ROUTING (runs before any code search or file investigation)

This is not a guideline. This is step 0 of every search or investigation turn, without exception.

**Before calling `grep_search`, `run_command` (`grep`, `Get-ChildItem`, `findstr`), or reading raw source files:**

1. Check if the target repository contains indexed code (C#, TypeScript, JavaScript, HTML, JSON, YAML, CSS, PowerShell).
2. **Execute `search_codebase_graph` or `get_symbol_definition` FIRST.**
3. Use `exactMatch: true` for exact symbol lookups to prevent context noise, and `regex: "..."` for pattern filtering.
4. Use precise `StartLine` and `EndLine` ranges returned by `CodeGraphMcp` to perform targeted partial file reads instead of loading entire source files into context window budget.

**Exemptions (narrow):**
- Searching non-code assets (e.g. Markdown documentation, images, binary assets, raw log files).
- Explicit non-indexed filesystem checks.
```

---

### B. Antigravity / Gemini AI (`GEMINI.md` / `AGENTS.md`)

Add the search priority strategy rule directly to your global or project `.agents/rules/`:

```markdown
# Codebase Search Strategy Rule
- **Prioritise CodeGraphMcp**: When searching inside indexed projects (C#, TypeScript, JavaScript, HTML, JSON, YAML, CSS, PowerShell), ALWAYS prioritize `CodeGraphMcp` tools (`search_codebase_graph`, `get_symbol_definition`, `find_symbol_callers`) over `grep_search` or terminal commands due to sub-300ms indexing and sub-50ms query speeds.
- **Line Range Scoping**: Use returned `StartLine` to `EndLine` ranges for scoped file viewing rather than loading entire 1000+ line source files.
- **Grep / CLI Fallback**: Use `grep_search` or CLI shell tools only as a fallback if `CodeGraphMcp` fails, is unavailable, or when searching un-indexed text assets (Markdown docs, raw logs).
```

---

## 7. Troubleshooting & Diagnostics

- **Clean Stdio Pipe:** All application logs and diagnostic output are routed exclusively to `stderr` (with LogLevel set to `Warning`). `stdout` is dedicated entirely to JSON-RPC protocol transport.
- **Index Database Missing:** If a tool returns a message indicating no index database was found, run the `index` CLI command first:
  ```powershell
  CodeGraphMcp.exe index "C:\Projects\TargetApp" --store-local
  ```
- **Database Locks:** `CodeGraphMcp` uses SQLite in Write-Ahead Logging (`WAL`) mode with read-only connection pooling during queries to prevent database locking errors.

---

## 7. Local Inter-Agent Relay Service (AGY ↔ Copilot)

`CodeGraphMcp` includes a zero-installer, user-level PowerShell background relay daemon located in `./scripts/relay/`. This service allows **AGY (Antigravity)** and **GitHub Copilot** to exchange messages and context directly over a local HTTP endpoint without triggering IDE workspace permission prompts or requiring local Administrator elevation.

### Architecture
- **Daemon script:** [RelayService.ps1](file:///d:/Projects/C%23/CodeGraphMcp/scripts/relay/RelayService.ps1) — High-performance `System.Net.HttpListener` on `http://localhost:9123/`.
- **Zero-flash launcher:** [RunHidden.vbs](file:///d:/Projects/C%23/CodeGraphMcp/scripts/relay/RunHidden.vbs) — VBScript launcher preventing console window pop-up flashes during startup.
- **Installer script:** [Install-RelayService.ps1](file:///d:/Projects/C%23/CodeGraphMcp/scripts/relay/Install-RelayService.ps1) — Registers a user logon startup shortcut (`CodeGraphRelay.lnk`) in `%APPDATA%\Microsoft\Windows\Start Menu\Programs\Startup\`.
- **Uninstaller script:** [Uninstall-RelayService.ps1](file:///d:/Projects/C%23/CodeGraphMcp/scripts/relay/Uninstall-RelayService.ps1) — Stops active daemon processes and removes startup shortcuts.

### API Endpoints

| Endpoint | Method | Description |
| :--- | :--- | :--- |
| `/health` | `GET` | Returns status, uptime, and depth of targeted queues (`agy`, `copilot`, `broadcast`). |
| `/send` | `POST` | Enqueues a message payload. Accepts optional JSON property `"to": "agy"` or `"to": "copilot"`. |
| `/receive` | `GET` | Dequeues incoming messages for a specific client (`?client=agy` or `?client=copilot`). |

### Setup & Usage

```powershell
# Install and start relay daemon
.\scripts\relay\Install-RelayService.ps1

# Test health check
Invoke-RestMethod -Uri "http://localhost:9123/health"

# Send targeted message to Copilot
Invoke-RestMethod -Uri "http://localhost:9123/send" -Method POST -Body '{"to":"copilot","sender":"AGY","msg":"hello"}' -ContentType "application/json"

# Receive targeted message as Copilot
Invoke-RestMethod -Uri "http://localhost:9123/receive?client=copilot"

# Uninstall relay service
.\scripts\relay\Uninstall-RelayService.ps1
```

