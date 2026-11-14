# CodeAtlas

[![.NET 10.0](https://img.shields.io/badge/.NET-10.0-512BD4?logo=dotnet)](https://dotnet.microsoft.com/)
[![ReadyToRun](https://img.shields.io/badge/Compile-ReadyToRun_(R2R)-007ACC?logo=dotnet)](https://learn.microsoft.com/en-us/dotnet/core/deploying/ready-to-run)
[![SQLite WAL](https://img.shields.io/badge/Storage-SQLite_WAL-003B57?logo=sqlite)](https://www.sqlite.org/wal.html)
[![MCP Server](https://img.shields.io/badge/Protocol-MCP-green)](https://modelcontextprotocol.io/)

> **External structural memory for AI agents, developers, and CI/CD pipelines—persisting polyglot AST symbol graphs in portable SQLite WAL artefacts for single-millisecond lookups, caller hierarchies, git blast-radius analysis, and cross-stack C# $\rightarrow$ TypeScript DTO contracts.**

> [!NOTE]
> **Project Renamed**: Formerly known as **CodeGraphMcp**, the project is now **CodeAtlas** to reflect that MCP is only one transport over the underlying graph. The core engine (`CodeAtlas.Core`), standalone CLI (`CodeAtlas.Cli` / `atlas`), MCP daemon (`CodeAtlas.Mcp`), and zero-dependency plugin SDK (`CodeAtlas.Abstractions`) operate across local development and automated build pipelines alike.

---

## 🎯 Why CodeAtlas Exists: External Memory for Amnesiac AI Agents

After a few months in a codebase, a developer already carries a mental map of where services live, how DTOs cross the stack, and which configuration files matter. **An AI coding agent starts every session without that map.** Even with massive context windows, an unguided agent behaves like a tourist wandering through thousands of files—reading `Program.cs`, `Startup.cs`, `appsettings.json`, and full 1,500-line classes just to answer a question about a single symbol.

`CodeAtlas` solves that by treating repository structure as **persistent, queryable external memory** rather than something an LLM must rediscover from raw files on every prompt:

```text
Source Files ──► Tree-sitter / AST Plugins ──► Portable SQLite WAL Artefact (+ Live Dirty Buffer)
                                                        ├──► MCP stdio Daemon  (AI Coding Assistants)
                                                        ├──► CLI Utility       (Local Developer Lookups)
                                                        └──► CI/CD & Blob Sync (Pipeline Agents & Code Health)
```

Instead of dumping whole files into context, an agent asks the graph *where* a symbol lives and *what* depends on it, receiving exact file paths, signatures, and `StartLine`–`EndLine` boundaries in **~1–5 ms**.

### Design Priorities
- **Context Budget Protection**: Returns exact `StartLine`–`EndLine` spans, signatures, and doc comments so agents read only the lines they need instead of burning tokens on full files.
- **Portable SQLite Knowledge Artefacts**: Stores the graph in a self-contained SQLite WAL `.db` file (`--db-dir`) that works offline locally and can be published to or pulled from Blob Storage in CI/CD pipelines without cold re-indexing.
- **Single-Millisecond Warm MCP Queries**: Persistent `.NET 10` ReadyToRun `stdio` daemon answers warm symbol, search, and caller queries in **0.8–4.5 ms** median round-trip time.
- **Incremental Git-Delta & Worktree Seeding**: Tracks `last_indexed_commit` per repository, re-indexing only changed files on commit switches and seeding new git worktrees from sibling index snapshots.
- **Live Dirty-Buffer & Single-File Sync**: Overlays uncommitted disk edits at query time and exposes surgical single-file/glob re-indexing (`SyncFile` / `codeatlas sync`) in **5–10 ms**.
- **Polyglot & Cross-Stack Linkage**: Indexes 8 language families out of the box and traces C# ServiceStack Request/Response DTOs $\rightarrow$ TypeScript interfaces $\rightarrow$ consuming Angular components.

---

## ⚖️ How CodeAtlas Compares to Existing Approaches

Code intelligence for AI assistants involves real architectural trade-offs between accuracy, memory footprint, infrastructure complexity, and polyglot coverage:

| Approach | Primary Strength | Key Trade-Off | Where CodeAtlas Fits |
| :--- | :--- | :--- | :--- |
| **`ripgrep` / `grep` / `Select-String`** | Zero setup; fast raw regex/text scan | No AST boundaries (`StartLine`–`EndLine`), symbol kinds, or caller directionality; noisy string hits | Replaces repetitive broad searches with typed symbol definitions and incoming call sites |
| **LSP Bridges (Roslyn, `tsserver`)** | Compiler-exact type & overload resolution | High resident RAM across large solutions; slow cold load; single-language silos; no config/DTO cross-language graph | Trades full compiler type-checking for ~65–70 MB RAM, instant startup, 8-language coverage, and cross-stack DTO links |
| **Cloud / IDE Indexers (Sourcegraph Cody, Continue)** | Rich remote repo search, embeddings & IDE integration | Cloud accounts/infrastructure or editor-bound state | Fully local, offline-capable SQLite `.db` store exposed directly to any MCP client or shell script |
| **Vector / Neo4j Graph-RAG** | Expressive multi-hop Cypher queries & natural-language concept search | Requires external DB containers, embedding pipelines, and higher query latency | Zero external services; deterministic SQLite WAL joins; pairs with `CodeAtlas.Traits` if local ONNX semantics are needed |

### Why Not Expose an LSP Directly Over MCP?
Language Servers (Roslyn for C#, `tsserver` for TypeScript) have deeper compiler-level type inference than Tree-sitter, including exact method overload disambiguation. If you work inside a single loaded solution and need full semantic model diagnostics, an LSP bridge is stronger.

`CodeAtlas` uses Tree-sitter + SQLite WAL instead of wrapping LSPs for four practical reasons:
1. **Multi-Repo Memory & Cold Start**: Keeping Roslyn + `tsserver` warm across 80+ microservices consumes multiple gigabytes of RAM and significant solution-load time. `CodeAtlas.Mcp` idles at **~65–70 MB RAM** and queries pre-indexed SQLite files immediately.
2. **Unified Polyglot Graph**: A single query engine covers C#, TypeScript, JavaScript, HTML/Razor/XML, JSON, YAML, CSS/SCSS, and PowerShell without orchestrating eight separate language servers.
3. **Cross-Language Contract Edges**: LSPs stop at their own language boundary; `CodeAtlas` links backend C# DTOs directly to generated TypeScript contracts and Angular components.
4. **Compilation Model (`ReadyToRun` + Collectible Plugins)**: `CodeAtlas` compiles ahead-of-time via `.NET 10 PublishReadyToRun` (`R2R`) rather than full `NativeAOT` specifically so third-party language parsers can be dropped in dynamically via isolated, collectible `AssemblyLoadContext` instances without recompiling the host.

---

## 📊 Benchmarks & Methodology

### Test Environment
- **Hardware**: AMD Ryzen 5 7600X (6 cores / 12 threads), 32 GB RAM, NVMe SSD
- **OS & Runtime**: Windows 11, .NET SDK `10.0.403`, Release `PublishReadyToRun=true`
- **Methodology**:
  - **Warm MCP `stdio` Daemon**: Persistent `CodeAtlas.Mcp.exe` process communicating over JSON-RPC 2.0 `stdio` (`25` measured requests per tool after `3` warm-up calls). This matches how AI coding assistants invoke MCP servers in production.
  - **Cold CLI One-Shot**: Full `CodeAtlas.exe query` OS process spawn, including loading all 8 parser plugin assemblies (`5` runs).
  - **Disk-Scan Baseline**: PowerShell `Get-ChildItem -Recurse | Select-String -SimpleMatch` (excluding `bin`, `obj`, `.git`, `publish`) with warm OS file cache (`5` runs). Note that `Select-String` returns raw matching lines only, not symbol boundaries or caller graphs.

### Measured Results

| Metric | Corpus 1: `CodeAtlas` Repo *(139 files, 1,001 symbols, 1,502 refs)* | Corpus 2: Multi-Language Corpus *(2,000 C#/TS files, 8,000 symbols, 1,000 refs)* |
| :--- | :--- | :--- |
| **Cold Full Index** *(engine / CLI wall-clock)* | `183 ms` / `467 ms` | `2,390 ms` / `2,619 ms` *(~835 files/s)* |
| **Incremental Git Delta (`noop`, clean tree)** | `516 ms` / `679 ms`* | `108 ms` / `288 ms` |
| **MCP `GetSymbolDefinition`** *(warm median / p95)* | **`1.39 ms`** / `2.42 ms` | **`4.51 ms`** / `7.67 ms` |
| **MCP `FindSymbolCallers`** *(warm median / p95)* | **`0.81 ms`** / `1.12 ms` | **`3.18 ms`** / `4.28 ms` |
| **MCP `SearchCodebaseGraph`** *(warm median / p95)* | **`0.86 ms`** / `1.09 ms` | **`4.48 ms`** / `5.53 ms` |
| **MCP `SyncFile` Single-File Re-Index** *(median / p95)* | **`9.70 ms`** / `12.20 ms` | **`5.30 ms`** / `7.83 ms` |
| **CLI One-Shot `query`** *(cold process spawn median)* | `185.5 ms` | `183.5 ms` |
| **Baseline: PowerShell `Select-String`** *(median / p95)* | `503.8 ms` / `2,594.6 ms` | `108.0 ms` / `110.0 ms` |
| **SQLite Index Disk Size (`.db`)** | `600 KB` | `2.03 MB` |
| **`CodeAtlas.Mcp` Resident RAM (`WorkingSet64`)** | `69.9 MB` | `64.3 MB` |

*\*Corpus 1 `noop` delta included git working-tree dirty-file inspection with untracked local planning files present.*

### Reproducing Benchmarks Locally
Anyone cloning the repository can run the exact benchmark suite on their own machine via [scripts/benchmark.ps1](./scripts/benchmark.ps1):

```powershell
pwsh -File ./scripts/publish.ps1
pwsh -File ./scripts/benchmark.ps1 -IncludeSyntheticCorpus
```

---

## 🛠️ Supported Languages & AST Extensions

| Parser ID | Extensions | AST Symbols & References Extracted |
| :--- | :--- | :--- |
| **`csharp`** | `.cs` | Classes (including partial), Interfaces, Structs, Enums, Records, Methods, Properties, Attributes, Call/Type References |
| **`typescript`** | `.ts`, `.tsx` | Functions, Classes, Interfaces, Enums, Type Aliases, Exports, Call/Type References |
| **`javascript`** | `.js`, `.jsx`, `.mjs`, `.cjs` | Functions, Classes, Methods, Exports, Call References |
| **`html`** | `.html`, `.htm`, `.cshtml`, `.razor`, `.xml`, `.csproj`, `.config` | Tag Elements, Angular/Razor Component Selectors, Attributes, IDs |
| **`json`** | `.json` | JSON Keys, Nested Configuration Property Paths (`appsettings.json`, `package.json`) |
| **`yaml`** | `.yaml`, `.yml` | Pipeline Tasks, Jobs, Configuration Keys, Specs |
| **`css`** | `.css`, `.scss`, `.less` | Class Rules, ID Selectors, Style Rule Sets |
| **`powershell`** | `.ps1`, `.psm1`, `.psd1` | Functions, Parameters, Script Declarations, Command Invocations |

---

## 🚀 Quick Start & Installation

### 1. Prerequisites
- [.NET 10.0 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)

### 2. Build & Publish Release Executables
```powershell
git clone https://github.com/ScriptedFluff/CodeAtlas.git
cd CodeAtlas

# Publish ReadyToRun MCP daemon, CLI utility, and all 8 drop-in parser plugins
pwsh -File ./scripts/publish.ps1
```

Published outputs:
- **MCP Daemon Server**: `.\publish\server\CodeAtlas.Mcp.exe`
- **CLI Utility**: `.\publish\cli\CodeAtlas.exe` (with `CodeAtlas.Cli.exe` and `atlas.exe` aliases)
- **Drop-in Parser Plugins**: `.\publish\server\Parsers\` and `.\publish\cli\Parsers\`

---

## 🧩 Modular Drop-in Parser Architecture

Every language parser is an independent class library implementing `CodeAtlas.Abstractions` (`ILanguageParser`) and loaded at runtime into an isolated, collectible `AssemblyLoadContext`:

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

### Multi-Tier Discovery & Anti-Shadowing Security
1. **Environment Tier**: `CODEATLAS_PARSERS_DIR` or `CODEATLAS_PARSERS_PATH`
2. **User Tier**: `%LOCALAPPDATA%\CodeAtlas\Parsers`
3. **Application Tier**: `{AppBaseDirectory}\Parsers`

- **`Strict` Policy (Default)**: Prevents unprivileged user-tier or environment-tier binaries from silently shadowing or overriding core application parsers.
- **`AllowAll` / `AllowList`**: Permits authorised custom parser overrides when developing or testing new language grammars.

---

## 📖 CLI Usage

```powershell
# 1. Index a repository (incremental git-delta aware; --force-full rebuilds from scratch)
.\publish\cli\codeatlas.exe index "C:\Projects\MyRepo"

# 2. Query a symbol definition (merges SQLite index + live uncommitted disk edits)
.\publish\cli\codeatlas.exe query "IndexOrchestrator" -r "C:\Projects\MyRepo"

# 3. Query with language, symbol type, and pagination filters
.\publish\cli\codeatlas.exe query "ConnectionStrings" -r "C:\Projects\MyRepo" --lang json --type Property --limit 20 --skip 0

# 4. Surgically sync or purge a single file or glob pattern in milliseconds
.\publish\cli\codeatlas.exe sync "src/Services/**/*.cs" -r "C:\Projects\MyRepo"

# 5. Inspect index status, git HEAD sync state, and symbol/reference counts
.\publish\cli\codeatlas.exe status "C:\Projects\MyRepo"

# 6. Manage drop-in language parser plugins
.\publish\cli\codeatlas.exe parsers list
.\publish\cli\codeatlas.exe parsers disable json
.\publish\cli\codeatlas.exe parsers enable json
.\publish\cli\codeatlas.exe parsers info csharp

# 7. Manage per-repository ignore rules (.codeatlas/settings.json)
.\publish\cli\codeatlas.exe ignore list -r "C:\Projects\MyRepo"

# 8. Prune stale index caches from deleted repositories or git worktrees
.\publish\cli\codeatlas.exe prune

# 9. Continuous watcher for auto-reindexing on git commits
.\publish\cli\codeatlas.exe watch "C:\Projects\MyRepo" --interval 30

# 10. Start the MCP stdio server
.\publish\cli\codeatlas.exe serve
```

---

## 🤖 MCP Server Setup for AI Assistants

Add `CodeAtlas.Mcp.exe` to your assistant's `mcp_config.json` (Antigravity, Claude Desktop, Cursor, VS Code Copilot / Roo Code):

```json
{
  "mcpServers": {
    "codeatlas": {
      "command": "C:\\Tools\\CodeAtlas\\publish\\server\\CodeAtlas.Mcp.exe",
      "args": []
    }
  }
}
```

### Exposed MCP Tools (8 Tools + Automatic Worktree GC)

1. **`GetSymbolDefinition`**: Returns exact source snippet, signature, doc comment, and line boundaries (`StartLine`–`EndLine`), including multi-file `partial` declarations and live uncommitted edits.
2. **`FindSymbolCallers`**: Performs inverse reference-graph lookup to list incoming call sites and usages across the workspace.
3. **`SearchCodebaseGraph`**: Searches symbols with substring/wildcard matching, optional `exactMatch` and `regex` filtering, language/type filters, structural relevance ranking, and pagination (`limit`, `offset`).
4. **`GetSupportedLanguages`**: Lists active AST language parsers and supported file extensions loaded in the server instance.
5. **`GetDiffImpact`**: Analyses `git diff` and dirty buffers to report modified symbols and downstream caller blast radius before refactoring.
6. **`GetServiceStackDtoContract`**: Resolves end-to-end C# ServiceStack DTO $\rightarrow$ TypeScript interface $\rightarrow$ Angular component usages.
7. **`SyncFile`**: Surgically re-indexes or deletes a single file (or glob pattern) in milliseconds, optionally accepting unsaved in-memory `content`.
8. **`SyncGitDiff`**: Runs a fast incremental delta sync of git-modified and dirty files without scanning the full repository.

*(Orphaned `.db` caches from deleted git worktrees are also garbage-collected automatically in the background on server startup.)*

---

## ⚙️ Customising Ignore & Include Rules (`.codeatlas/settings.json`)

Place a `settings.json` file inside `<repo>/.codeatlas/` (or manage it via `codeatlas ignore`) to customise indexing scope:

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

- **Default Exclusions**: Automatically skips lockfiles and minified bundles (`package-lock.json`, `pnpm-lock.yaml`, `yarn.lock`, `*.min.js`) as well as build/dependency directories (`node_modules`, `bin`, `obj`, `dist`, `publish`, `coverage`).

---

## 👤 Author & Development Transparency

- **Author & Maintainer**: **Garry Flanagan** ([ScriptedFluff](https://github.com/ScriptedFluff))
- **Repository**: [ScriptedFluff/CodeAtlas](https://github.com/ScriptedFluff/CodeAtlas)
- **License**: [Apache 2.0](./LICENSE)

### 🤖 AI Pair-Programming Statement
This project was designed and built through active AI-assisted pair-programming, using AI coding tools as velocity multipliers alongside human architecture, verification, and benchmarking:
- **Primary AI Assistant Platform**: Google Antigravity
- **Code Generation & Refactoring**: Gemini
- **Architecture Planning & Review**: Split between Claude Sonnet and Gemini

---

## 📚 Further Documentation

- [USAGE.md](./USAGE.md) — Comprehensive CLI & tool command reference.
- [MCP-CONFIGURE.md](./MCP-CONFIGURE.md) — Multi-IDE & AI assistant MCP setup guide.
- [developer.md](./developer.md) — Internal architecture, plugin SDK guide, and contributor specification.

---

## 🏗️ CI/CD Artefact Workflows & The CodeAtlas Ecosystem

Because a `CodeAtlas` index is a self-contained SQLite file (`--db-dir`), it acts as a **portable repository knowledge artefact**—similar to compiled packages, SARIF outputs, or coverage reports—rather than ephemeral IDE state:

- **DevOps & Pipeline Agents**: Ephemeral CI/CD runners (e.g., Azure DevOps or GitHub Actions running Copilot / automated PR workflows) can pull the latest `.db` snapshot from Blob Storage, run a sub-second incremental `codeatlas index --db-dir <cache>` delta against the branch commit, and immediately query `GetDiffImpact` or symbol graphs without re-indexing the repository from scratch.
- **Scheduled Architectural Maintenance (`CodeAtlas` + `CodeAtlas.Traits`)**: Together with its companion project **[CodeAtlas.Traits](https://github.com/ScriptedFluff/CodeAtlas.Traits)**, the ecosystem answers three distinct engineering questions—especially important as AI coding velocity accelerates risk of silent duplication and architectural drift:
  1. **Where does it live?** (`CodeAtlas` — AST symbol definitions and exact `StartLine`–`EndLine` spans)
  2. **What depends on it?** (`CodeAtlas` — incoming call sites, cross-stack C# $\rightarrow$ TypeScript DTO contracts, and git diff blast radius)
  3. **Is the codebase staying healthy or drifting?** (**[CodeAtlas.Traits](https://github.com/ScriptedFluff/CodeAtlas.Traits)** — scheduled/weekly capability and behaviour indexing in `traits.db` using content-addressable `body_hash` digests, a developer thesaurus, and a local CPU-only ONNX semantic engine to surface duplicated patterns and overlapping logic across large codebases)
