# CodeGraphMcp 🚀

[![.NET 10.0](https://img.shields.io/badge/.NET-10.0-512BD4?logo=dotnet)](https://dotnet.microsoft.com/)
[![Native AOT](https://img.shields.io/badge/Compile-Native_AOT-007ACC?logo=visualstudio)](https://learn.microsoft.com/en-us/dotnet/core/deploying/native-aot/)
[![MCP Server](https://img.shields.io/badge/Protocol-MCP-green)](https://modelcontextprotocol.io/)

> **Local high-performance code knowledge graph indexer & query engine providing sub-100ms symbol lookups, caller reference graphs, git blast-radius impact analysis, and ServiceStack cross-stack DTO contracts for AI coding assistants.**

---

## 🎯 Overview & Goals

`CodeGraphMcp` bridges the gap between massive local codebases (80+ C# microservices, Angular/TypeScript apps, configurations, and scripts) and AI assistants (Antigravity, GitHub Copilot, Claude Desktop, Cursor). 

Instead of forcing AI agents to repeatedly scan file trees or spend context window tokens loading full 1,000+ line files, `CodeGraphMcp` maintains a lightweight, incremental SQLite index of your codebase AST symbols. AI models query exact symbol locations, signatures, caller chains, and precise line ranges (`StartLine` – `EndLine`) in **sub-100ms**.

### Core Goals
- ⚡ **Sub-100ms Warm Queries**: 17x faster than disk-scanning tools like PowerShell `Select-String` or `Get-ChildItem`.
- 🧠 **Context Budget Protection**: Enables AI agents to read only the exact line range where a target symbol lives.
- 🔄 **Live Dirty Buffer Awareness**: Indexes unsaved file modifications on disk before querying SQLite, so unsaved work is immediately searchable.
- 🌐 **Multi-Language AST Support**: C#, TypeScript/JavaScript, HTML/XML, JSON, YAML, CSS, and PowerShell.
- 🔗 **Cross-Stack Linkage**: Maps C# DTOs -> TypeScript interfaces -> consuming Angular component files.

---

## 🛠️ Supported Languages & AST Extensions

| Language | Extensions | AST Symbols & Elements Parsed |
| :--- | :--- | :--- |
| **C#** | `.cs` | Classes, Interfaces, Structs, Enums, Records, Methods, Properties, Attributes |
| **TypeScript / JavaScript** | `.ts`, `.tsx`, `.js`, `.jsx` | Functions, Classes, Interfaces, Enums, Types, Exports |
| **HTML / XML** | `.html`, `.htm`, `.xml`, `.csproj`, `.config` | Tag Elements, Component Names, Attributes, IDs, Properties |
| **JSON & Manifests** | `.json`, `appsettings.json`, `package.json` | JSON Keys, Nested Object Property Paths |
| **YAML & Pipelines** | `.yaml`, `.yml` | Pipeline Tasks, Jobs, Configuration Keys, Specs |
| **CSS / Styles** | `.css` | Class Rules, ID Selectors, Rule Sets |
| **PowerShell** | `.ps1`, `.psm1` | Functions, Parameters, Script Declarations |

---

## 🚀 Quick Start & Installation

### 1. Prerequisites
- [.NET 10.0 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)

### 2. Build & Publish Release Executables
```powershell
# Clone the repository
git clone https://github.com/ScriptedFluff/CodeGraphMcp.git
cd CodeGraphMcp

# Publish MCP Server daemon binary to ./publish/server
dotnet publish src/CodeGraphMcp.Server/CodeGraphMcp.Server.csproj -c Release -o ./publish/server

# Publish CLI binary to ./publish/cli
dotnet publish src/CodeGraphMcp.Cli/CodeGraphMcp.Cli.csproj -c Release -o ./publish/cli
```
The compiled executables will be located at:
- MCP Daemon Server: `.\publish\server\CodeGraphMcp.Server.exe`
- CLI Utility: `.\publish\cli\CodeGraphMcp.Cli.exe`

---

## 📖 CLI How-to-Use Tutorial

`CodeGraphMcp.Cli` (`cg`) operates as a standalone command-line utility.

```powershell
# 1. Index a repository (stores SQLite database inside <repo>/.codegraph/code_graph.db)
.\publish\cli\CodeGraphMcp.Cli.exe index "C:\Projects\MyRepo"

# 2. Query a symbol definition
.\publish\cli\CodeGraphMcp.Cli.exe query "IndexOrchestrator" -r "C:\Projects\MyRepo"

# 3. Query with type, language, and pagination filters
.\publish\cli\CodeGraphMcp.Cli.exe query "Morningstar" -r "C:\Projects\MyRepo" --lang json --type Property --limit 20 --skip 0

# 4. Check index database status & commit state
.\publish\cli\CodeGraphMcp.Cli.exe status "C:\Projects\MyRepo"

# 5. Prune stale worktrees & obsolete index caches to reclaim disk space
.\publish\cli\CodeGraphMcp.Cli.exe prune

# 6. Continuous watcher for auto-reindexing on git commits
.\publish\cli\CodeGraphMcp.Cli.exe watch "C:\Projects\MyRepo" --interval 30

# 7. Start the MCP stdio server for AI assistants
.\publish\cli\CodeGraphMcp.Cli.exe serve
```

---

## 🤖 MCP Server Setup for AI Assistants

To connect `CodeGraphMcp` to your AI coding assistant (Antigravity, Claude Desktop, Cursor, VSCode Roo Code), add the following entry to your `mcp_config.json`:

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

### Available MCP Tools

AI assistants receive 7 specialized code-graph tools:
1. `get_symbol_definition`: Get exact source snippet, signature, doc comment, and line numbers (`StartLine`–`EndLine`).
2. `find_symbol_callers`: Find all incoming call sites and invocations across solution boundaries.
3. `search_codebase_graph`: Search symbols with automatic substring matching (or optional `%` wildcards), regex post-filtering, type/language filters, and pagination (`limit`, `offset`).
4. `get_supported_languages`: Discover supported extensions and active AST parsers.
5. `get_diff_impact`: Analyze git diffs & dirty buffer to compute blast radius before refactoring.
6. `get_servicestack_dto_contract`: Resolve C# DTO -> TypeScript interface -> Angular component endpoints.
7. `prune_indexed_worktrees`: Clean up stale `.db` files from deleted git worktrees.

---

## ⚙️ Customizing Ignore & Include Rules (`.codegraph/settings.json`)

Place a `settings.json` file inside your repository's `.codegraph/` directory to configure project-specific indexing scope:

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

* **Default Exclusions**: Built-in defaults ignore heavy lockfiles (`package-lock.json`, `pnpm-lock.yaml`, `yarn.lock`, `*.min.js`) and output directories (`node_modules`, `bin`, `obj`, `dist`, `publish`, `coverage`).

---

## 👤 Author & Development Transparency

- **Author & Maintainer**: **Garry Flanagan** ([ScriptedFluff](https://github.com/ScriptedFluff))
- **Repository**: [ScriptedFluff/CodeGraphMcp](https://github.com/ScriptedFluff/CodeGraphMcp)
- **License**: [Apache 2.0](file:///d:/Projects/C%23/CodeGraphMcp/LICENSE)

### 🤖 AI Pair-Programming Statement
This project was designed and built through active AI-assisted pair-programming. As an experienced software developer, I leverage cutting-edge AI tools as velocity multipliers rather than substitutes for engineering judgment:
- **Primary AI Assistant Platform**: Google Antigravity
- **Code Generation & Refactoring**: Gemini 3.6
- **Architecture Planning & Review**: 50/50 split between Claude Sonnet 4.6 and Gemini 3.6

Whether inspecting symbol caller graphs, tuning AST parsers, or benchmarking Native AOT query performance, modern AI tooling allows solo developers to deliver production-grade software tools at unprecedented speed.

---

## 📚 Further Documentation

- [USAGE.md](file:///d:/Projects/C%23/CodeGraphMcp/USAGE.md) — Comprehensive CLI & tool command reference.
- [MCP-CONFIGURE.md](file:///d:/Projects/C%23/CodeGraphMcp/MCP-CONFIGURE.md) — Multi-IDE & AI engine MCP setup guide.
- [developer.md](file:///d:/Projects/C%23/CodeGraphMcp/developer.md) — Architecture & developer contributor guide.
