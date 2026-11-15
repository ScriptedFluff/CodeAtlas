# CodeAtlas 🗺️

[![.NET 10.0](https://img.shields.io/badge/.NET-10.0-512BD4?logo=dotnet)](https://dotnet.microsoft.com/)
[![ReadyToRun](https://img.shields.io/badge/Compile-ReadyToRun_(R2R)-007ACC?logo=dotnet)](https://learn.microsoft.com/en-us/dotnet/core/deploying/ready-to-run)
[![SQLite WAL](https://img.shields.io/badge/Storage-SQLite_WAL-003B57?logo=sqlite)](https://www.sqlite.org/wal.html)
[![MCP Server](https://img.shields.io/badge/Protocol-MCP-green)](https://modelcontextprotocol.io/)

> **Repository structural memory for AI assistants, developers, and CI/CD pipelines.**
> **Stop making AI rediscover repository structure from scratch every session.**

> [!NOTE]
> **Project Renamed**: Formerly known as **CodeGraphMcp**, the project is now **CodeAtlas** to reflect that the persistent structural index (`.codeatlas/atlas.db`) is the core product—serving AI assistants via MCP, developers via CLI, DevOps pipelines via portable `.db` artefacts, and **[CodeAtlas.Traits](https://github.com/ScriptedFluff/CodeAtlas.Traits)** for pattern discovery.

---

## 🎯 Why CodeAtlas Exists: Structural Memory for Amnesiac AI Agents

After a few months in a codebase, a developer carries a mental map of where services live, how DTOs cross the stack, and what breaks if a method signature changes. **An AI coding assistant starts every session without that map.** Without structural memory, an agent repeatedly greps file trees and reads entire 1,500-line classes into its context window just to locate a single 20-line method.

`CodeAtlas` acts as **persistent structural memory** for the repository. Instead of repeatedly scanning raw files and burning thousands of context tokens, AI agents and tools query a persistent model of symbols, exact line spans, callers, and cross-stack relationships:

```text
Source Files ──► Incremental AST Indexer ──► .codeatlas/atlas.db (+ Live Dirty Buffer)
                                                   ├──► AI Coding Assistants (MCP stdio daemon)
                                                   ├──► Developers           (Fast CLI lookups)
                                                   ├──► DevOps / CI Pipelines (Portable .db artefact)
                                                   └──► CodeAtlas.Traits     (Pattern & convention memory)
```

---

## 🧭 Where CodeAtlas Fits in an AI Workflow (The Map vs. Local Building Styles)

Together with its companion project **[CodeAtlas.Traits](https://github.com/ScriptedFluff/CodeAtlas.Traits)**, the ecosystem separates structural navigation, convention discovery, and high-level reasoning into three clean layers:

```text
┌────────────────────────────────────────────────────────────────────────────┐
│ 1. CodeAtlas — Repository Structural Memory ("The Map")                    │
│    • What exists? Where does it live (FilePath, StartLine..EndLine)?       │
│    • Who calls it? What depends on it? What breaks if I change it?         │
│    • How does a C# DTO cross into TypeScript interfaces & Angular views?   │
└─────────────────────────────────────┬──────────────────────────────────────┘
                                      │
                                      ▼
┌────────────────────────────────────────────────────────────────────────────┐
│ 2. CodeAtlas.Traits — Repository Pattern Memory ("Local Building Styles")  │
│    • What existing code smells similar to what I'm about to build?         │
│    • Where does this kind of code normally live, and how is it structured? │
│    • After generating a feature, what semantic overlaps can we consolidate?│
└─────────────────────────────────────┬──────────────────────────────────────┘
                                      │
                                      ▼
┌────────────────────────────────────────────────────────────────────────────┐
│ 3. AI Assistant — Domain Reasoning & Intent                                │
│    • Reads only the exact 20-line symbol spans needed (saving context)     │
│    • Decides whether to reuse, extend, or write new code in local style    │
│    • Verifies caller blast radius before modifying shared contracts        │
└────────────────────────────────────────────────────────────────────────────┘
```

### Core Outcomes
- **Protect the AI Context Budget**: Returns exact `StartLine`–`EndLine` boundaries, signatures, and doc comments in **1–5 ms** so agents read only the lines they need instead of loading entire files.
- **Instant Blast-Radius Awareness**: Answers *"Who calls this method?"* (`FindSymbolCallers`) and *"What downstream callers are impacted by my uncommitted git diff?"* (`GetDiffImpact`) before code is merged.
- **Cross-Stack Contract Tracing**: Links backend C# ServiceStack Request/Response DTOs directly to generated TypeScript interfaces and consuming Angular components (`GetServiceStackDtoContract`).
- **Live Uncommitted Edit Overlay**: Merges the on-disk SQLite index with unsaved/uncommitted working-tree edits at query time so the map is never stale while you code.
- **Portable DevOps Artefact**: Because `.codeatlas/atlas.db` is a self-contained SQLite file (`--db-dir`), CI/CD pipelines can pull the latest index from Blob Storage and run sub-second incremental git-delta updates without re-indexing from scratch.

---

## 🤖 MCP Tools for AI Assistants (`CodeAtlas.Mcp`)

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

| Tool | Question Answered | Description |
| :--- | :--- | :--- |
| **`SearchCodebaseGraph`** | *"Where is X?"* | Searches symbols by substring, wildcard, or regex with language/type filters and structural relevance ranking. |
| **`GetSymbolDefinition`** | *"Show me X's exact code & signature"* | Returns exact source snippet, signature, doc comment, and `StartLine`–`EndLine` boundaries (including `partial` classes and live edits). |
| **`FindSymbolCallers`** | *"Who uses X?"* | Inverse reference-graph lookup listing incoming call sites, type usages, and inheritance across the repository. |
| **`GetDiffImpact`** | *"What breaks if I commit this?"* | Analyses `git diff` and dirty files to report modified symbols and their downstream caller blast radius. |
| **`GetServiceStackDtoContract`** | *"Where is this DTO used in the UI?"* | Traces C# ServiceStack DTO $\rightarrow$ TypeScript interface $\rightarrow$ Angular component usages end-to-end. |
| **`SyncFile`** | *"Update the map for the file I just edited"* | Surgically re-indexes or purges a single file or glob pattern in **5–10 ms**. |
| **`SyncGitDiff`** | *"Catch up with my latest branch changes"* | Runs an incremental delta sync of git-modified and dirty files without scanning the full repository. |
| **`GetSupportedLanguages`** | *"What file types are indexed?"* | Lists active AST language parsers and supported file extensions loaded in the server instance. |

---

## 💻 Quick Start & CLI Usage (`CodeAtlas.Cli`)

### Build & Publish Release Executables
```powershell
git clone https://github.com/ScriptedFluff/CodeAtlas.git
cd CodeAtlas

# Publish ReadyToRun MCP daemon, CLI utility, and all 8 drop-in parser plugins (.NET 10 SDK)
pwsh -File ./scripts/publish.ps1
```

### Common CLI Commands
```powershell
# 1. Index a repository (incremental git-delta aware; --force-full rebuilds from scratch)
.\publish\cli\codeatlas.exe index "C:\Projects\MyRepo"

# 2. Query a symbol definition (merges SQLite index + live uncommitted disk edits)
.\publish\cli\codeatlas.exe query "IndexOrchestrator" -r "C:\Projects\MyRepo"

# 3. Query with language, symbol type, and pagination filters
.\publish\cli\codeatlas.exe query "ConnectionStrings" -r "C:\Projects\MyRepo" --lang json --type Property --limit 20

# 4. Surgically sync a single file or glob pattern in milliseconds
.\publish\cli\codeatlas.exe sync "src/Services/**/*.cs" -r "C:\Projects\MyRepo"

# 5. Inspect index status, git HEAD sync state, and symbol/reference counts
.\publish\cli\codeatlas.exe status "C:\Projects\MyRepo"

# 6. Manage drop-in language parser plugins or per-repo ignore rules (.codeatlas/settings.json)
.\publish\cli\codeatlas.exe parsers list
.\publish\cli\codeatlas.exe ignore list -r "C:\Projects\MyRepo"
```

---

## 🛠️ Supported Languages & AST Extensions

| Parser ID | Extensions | AST Symbols & References Extracted |
| :--- | :--- | :--- |
| **`csharp`** | `.cs` | Classes (including `partial`), Interfaces, Structs, Enums, Records, Methods, Properties, Signatures, Call/Type/Inheritance References |
| **`typescript`** | `.ts`, `.tsx` | Functions, Classes, Interfaces, Enums, Type Aliases, Exports, Call/Type References |
| **`javascript`** | `.js`, `.jsx`, `.mjs`, `.cjs` | Functions, Classes, Methods, Exports, Call References |
| **`html`** | `.html`, `.htm`, `.cshtml`, `.razor`, `.xml`, `.csproj`, `.config` | Tag Elements, Angular/Razor Component Selectors, Attributes, IDs |
| **`json`** | `.json` | JSON Keys, Nested Configuration Property Paths (`appsettings.json`, `package.json`) |
| **`yaml`** | `.yaml`, `.yml` | Pipeline Tasks, Jobs, Configuration Keys, Specs |
| **`css`** | `.css`, `.scss`, `.less` | Class Rules, ID Selectors, Style Rule Sets |
| **`powershell`** | `.ps1`, `.psm1`, `.psd1` | Functions, Parameters, Script Declarations, Command Invocations |

---

## ⚖️ Architectural Trade-Offs (Where CodeAtlas Fits)

| Approach | Primary Strength | Key Trade-Off | Where CodeAtlas Fits |
| :--- | :--- | :--- | :--- |
| **`ripgrep` / `grep` / `Select-String`** | Zero setup; fast raw text scan | No AST boundaries (`StartLine`–`EndLine`), symbol kinds, or caller directionality | Replaces broad text searches with exact symbol definitions and incoming call sites |
| **LSP Bridges (Roslyn, `tsserver`)** | Compiler-exact type & overload resolution | High resident RAM across large solutions; slow cold load; single-language silos | Trades full compiler diagnostics for ~65–70 MB RAM, instant startup, 8-language coverage, and cross-stack DTO links |
| **Cloud / IDE Indexers** | Rich remote repo search & IDE integration | Cloud infrastructure or editor-bound state | Self-contained local SQLite `.db` artefact usable by any MCP client, CLI script, or CI pipeline |

Every language parser is an isolated class library implementing `ILanguageParser` (`CodeAtlas.Abstractions`), compiled with `.NET 10 PublishReadyToRun` and loaded via collectible `AssemblyLoadContext` instances so custom language grammars can be dropped into `Parsers/` without recompiling the host.

---

## 📊 Performance & Reproducible Benchmarks

Measured on AMD Ryzen 5 7600X (6C/12T, 32 GB RAM, NVMe SSD, Windows 11, .NET 10.0 R2R):

| Metric | Corpus 1: `CodeAtlas` Repo *(139 files, 1,001 symbols)* | Corpus 2: Polyglot Corpus *(2,000 C#/TS files, 8,000 symbols)* |
| :--- | :--- | :--- |
| **Cold Full Index** *(engine / CLI)* | `183 ms` / `467 ms` | `2,390 ms` / `2,619 ms` *(~835 files/s)* |
| **Incremental Git Delta (`noop`)** | `516 ms` / `679 ms` | `108 ms` / `288 ms` |
| **MCP `GetSymbolDefinition`** *(warm median / p95)* | **`1.39 ms`** / `2.42 ms` | **`4.51 ms`** / `7.67 ms` |
| **MCP `FindSymbolCallers`** *(warm median / p95)* | **`0.81 ms`** / `1.12 ms` | **`3.18 ms`** / `4.28 ms` |
| **MCP `SearchCodebaseGraph`** *(warm median / p95)* | **`0.86 ms`** / `1.09 ms` | **`4.48 ms`** / `5.53 ms` |
| **MCP `SyncFile` Single-File Re-Index** *(median)* | **`9.70 ms`** | **`5.30 ms`** |
| **SQLite Index Size / Resident RAM** | `600 KB` / `69.9 MB` | `2.03 MB` / `64.3 MB` |

Reproduce locally via [`scripts/benchmark.ps1`](./scripts/benchmark.ps1):
```powershell
pwsh -File ./scripts/publish.ps1
pwsh -File ./scripts/benchmark.ps1 -IncludeSyntheticCorpus
```

---

## 📚 Further Documentation & Governance

- **[USAGE.md](./USAGE.md)** — Full CLI command reference and `.codeatlas/settings.json` ignore/include configuration.
- **[MCP-CONFIGURE.md](./MCP-CONFIGURE.md)** — Multi-IDE & AI assistant MCP setup guide.
- **[developer.md](./developer.md)** — Internal architecture, plugin SDK guide, and contributor specification.
- **Companion Project**: **[CodeAtlas.Traits](https://github.com/ScriptedFluff/CodeAtlas.Traits)** — Repository pattern memory, convention discovery, and consolidation built on top of `atlas.db`.
- **Author & Maintainer**: **Garry Flanagan** ([ScriptedFluff](https://github.com/ScriptedFluff)) | **License**: [Apache 2.0](./LICENSE)
