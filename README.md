# CodeAtlas 🚀

[![.NET 10.0](https://img.shields.io/badge/.NET-10.0-512BD4?logo=dotnet)](https://dotnet.microsoft.com/)
[![Native AOT](https://img.shields.io/badge/Compile-Native_AOT-007ACC?logo=visualstudio)](https://learn.microsoft.com/en-us/dotnet/core/deploying/native-aot/)
[![MCP Server](https://img.shields.io/badge/Protocol-MCP-green)](https://modelcontextprotocol.io/)

> **Local high-performance code knowledge graph indexer & query engine providing sub-100ms symbol lookups, caller reference graphs, git blast-radius impact analysis, and ServiceStack cross-stack DTO contracts for AI coding assistants.**

> [!NOTE]
> **Project Renamed**: Formerly known as **CodeGraphMcp**, the project has been renamed to **CodeAtlas** to reflect its broader role as a standalone, ultra-fast structural code indexer and knowledge oracle. The MCP server is now packaged as a dedicated sub-project (`CodeAtlas.Mcp`), while the core engine and CLI (`CodeAtlas.Cli` / `atlas`) operate independently.

---

## 🎯 Overview & Goals

`CodeAtlas` bridges the gap between massive local codebases (80+ C# microservices, Angular/TypeScript apps, configurations, and scripts) and AI assistants (Antigravity, GitHub Copilot, Claude Desktop, Cursor). 

Instead of forcing AI agents to repeatedly scan file trees or spend context window tokens loading full 1,000+ line files, `CodeAtlas` maintains a lightweight, incremental SQLite index of your codebase AST symbols. AI models query exact symbol locations, signatures, caller chains, and precise line ranges (`StartLine` – `EndLine`) in **sub-100ms**.

### Core Goals
- ⚡ **Sub-100ms Warm Queries**: 17x faster than disk-scanning tools like PowerShell `Select-String` or `Get-ChildItem`.
- 🧠 **Context Budget Protection**: Enables AI agents to read only the exact line range where a target symbol lives.
- 🔄 **Live Dirty Buffer Awareness**: Indexes unsaved file modifications on disk before querying SQLite, so unsaved work is immediately searchable.
- 🌐 **Multi-Language AST Support**: C#, TypeScript/JavaScript, HTML/XML, JSON, YAML, CSS, and PowerShell.
- 🔗 **Cross-Stack Linkage**: Maps C# DTOs -> TypeScript interfaces -> consuming Angular component files.

---

## ⚡ Why CodeAtlas Is Better Than Other Code-Graph MCP Servers

CodeAtlas is fundamentally different from other code-graph MCP servers. Most existing tools in this space are Python-based, slow, language-limited, and often assembled through AI-generated code rather than engineered. CodeAtlas is a native, high-performance, multi-language semantic indexer built for real engineering workflows and fast AI command-chains.

### 🚀 10–20x Faster Execution (50–100ms Queries)
Unlike Python MCP servers, which typically respond in 300–2000ms, CodeAtlas consistently delivers 50–100ms warm queries. This makes it 10–20x faster than alternatives and allows AI assistants to chain multiple commands without latency buildup. Slow MCP servers cause AI to stall, retry, hallucinate, or lose context. CodeAtlas avoids all of this by being engineered for speed, not prototyped for convenience.

### ⚡ Native AOT .NET 10
CodeAtlas uses Native AOT .NET 10, giving it compiled performance, multi-threading, and predictable memory behavior. Python tools suffer from GIL contention, slow AST traversal, slow JSON serialization, and runtime overhead. CodeAtlas avoids these bottlenecks entirely.

### 🌐 True Multi-Language & Cross-Stack Visibility
CodeAtlas indexes C#, TypeScript, JavaScript, HTML, XML, JSON, YAML, CSS, and PowerShell. Python alternatives are almost always language-specific and cannot build a unified cross-stack graph. CodeAtlas links C# DTOs to TypeScript interfaces and Angular components, providing full-stack visibility that no other MCP server offers.

### 🔄 Incremental SQLite Index & Live Dirty Buffer Awareness
CodeAtlas maintains an incremental SQLite index and merges dirty buffers, meaning unsaved changes are searchable immediately. Python tools typically rescan the filesystem on every query, causing slowdowns and inconsistent results. CodeAtlas performs diff-aware updates, partial reindexing, and blast-radius analysis for safe refactoring.

### 🧠 AI Context Budget Protection
CodeAtlas protects AI context budgets by returning exact line ranges for symbols instead of dumping entire files. This reduces token usage, improves accuracy, and prevents hallucinations. Python alternatives often send full files or large chunks of text, wasting tokens and increasing cost.

> **In short**: CodeAtlas is the only fast, multi-language, engineered, semantic, incremental, diff-aware, dirty-buffer-aware, cross-stack code-graph MCP server available today. All alternatives are Python, slow, language-limited, and unsuitable for high-performance AI workflows. CodeAtlas is built for real developers and real AI assistants, delivering sub-100ms responses and production-grade reliability.

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
git clone https://github.com/ScriptedFluff/CodeAtlas.git
cd CodeAtlas

# Publish Server daemon, CLI utility, and all 8 drop-in parser plugins
pwsh -File ./scripts/publish.ps1
```
The compiled executables and plugins will be located at:
- MCP Daemon Server: `.\publish\server\CodeAtlas.Mcp.exe`
- CLI Utility: `.\publish\cli\CodeAtlas.Cli.exe`
- Drop-in Plugins: `.\publish\server\Parsers\` and `.\publish\cli\Parsers\`

---

## 🧩 Modular / Drop-in Parser Architecture

`CodeAtlas` features a decoupled drop-in plugin architecture. Every language parser is an independent DLL loaded into an isolated, collectible `AssemblyLoadContext`:

```text
├── Parsers/                 <-- Active drop-in language parser DLLs
│   ├── CSharp/
│   ├── TypeScript/
│   ├── JavaScript/
│   ├── Html/
│   ├── PowerShell/
│   ├── Json/
│   ├── Yaml/
│   └── Css/
└── DisabledParsers/         <-- Temporarily disabled parser folders
```

### Multi-Tier Search Locations
1. **Environment Tier**: `CODEATLAS_PARSERS_DIR` or `CODEATLAS_PARSERS_PATH`
2. **User Tier**: `%LOCALAPPDATA%\CodeAtlas\Parsers`
3. **Application Tier**: `{AppBaseDirectory}\Parsers`

### Anti-Shadowing Security Policies
- **`Strict` (Default)**: Prevents unprivileged user-tier or environment-tier binaries from silently shadowing or overriding core application parsers.
- **`AllowAll` / `AllowList`**: Permits authorized custom parser overrides when testing or developing new language grammars.

---

## 📖 CLI How-to-Use Tutorial

`CodeAtlas.Cli` (`atlas` / `codeatlas`) operates as a standalone command-line utility.

```powershell
# 1. Index a repository (stores SQLite database inside <repo>/.codeatlas/atlas.db)
.\publish\cli\codeatlas.exe index "C:\Projects\MyRepo"

# 2. Query a symbol definition
.\publish\cli\codeatlas.exe query "IndexOrchestrator" -r "C:\Projects\MyRepo"

# 3. Query with type, language, and pagination filters
.\publish\cli\codeatlas.exe query "Morningstar" -r "C:\Projects\MyRepo" --lang json --type Property --limit 20 --skip 0

# 4. Check index database status & commit state
.\publish\cli\codeatlas.exe status "C:\Projects\MyRepo"

# 5. Manage drop-in language parser plugins
.\publish\cli\codeatlas.exe parsers list               # List all installed parsers and statuses
.\publish\cli\codeatlas.exe parsers disable json       # Move parser to DisabledParsers/
.\publish\cli\codeatlas.exe parsers enable json        # Re-enable parser
.\publish\cli\codeatlas.exe parsers info csharp        # View plugin metadata and assembly paths

# 6. Prune stale worktrees & obsolete index caches to reclaim disk space
.\publish\cli\codeatlas.exe prune

# 7. Continuous watcher for auto-reindexing on git commits
.\publish\cli\codeatlas.exe watch "C:\Projects\MyRepo" --interval 30

# 8. Start the MCP stdio server for AI assistants
.\publish\cli\codeatlas.exe serve
```

---

## 🤖 MCP Server Setup for AI Assistants

To connect `CodeAtlas` to your AI coding assistant (Antigravity, Claude Desktop, Cursor, VSCode Roo Code), add the following entry to your `mcp_config.json`:

```json
{
  "mcpServers": {
    "codeatlas": {
      "command": "D:\\Projects\\C#\\CodeGraphMcp\\publish\\server\\CodeAtlas.Mcp.exe",
      "args": []
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

## ⚙️ Customizing Ignore & Include Rules (`.codeatlas/settings.json`)

Place a `settings.json` file inside your repository's `.codeatlas/` directory to configure project-specific indexing scope:

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
- **Repository**: [ScriptedFluff/CodeAtlas](https://github.com/ScriptedFluff/CodeAtlas)
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

---

## 🔗 Linked & Companion Projects

- **[CodeAtlas.Traits](https://github.com/ScriptedFluff/CodeAtlas.Traits)** — Companion capability, behaviour, and technology indexing layer (`traits.db`) for `CodeAtlas`. Indexes classes and methods by *what they do* using content-addressable `body_hash` digests, a multi-language developer thesaurus, and a CPU-only ONNX semantic engine to detect duplicate or overlapping logic across the codebase. Third party AI models can tag as well using the MCP endpoints, to add richer tagging.
