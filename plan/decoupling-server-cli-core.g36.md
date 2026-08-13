# Architectural Plan: Decoupling CodeGraphMcp into Core, Server, and CLI Projects

**Model Tag**: `g36` (Gemini 3.6)  
**Date**: 2026-08-13  
**Status**: Proposal / Architectural Plan  
**Target Architecture**: Multi-project solution separating Domain Logic (`Core`), MCP Daemon Host (`Server`), and Command-Line Interface (`Cli`).

---

## 🎯 Overview & Architectural Motivation

Currently, `CodeGraphMcp` operates as a single executable project that handles CLI parsing, database indexing, TreeSitter AST parsing, and MCP stdio server functionality within one binary. 

While efficient for initial development, decoupling this monolithic entrypoint into three specialized projects provides significant architectural benefits:

1. **Clean Separation of Concerns**: Core domain logic (indexing, SQLite storage, AST parsing, git diff impact, DTO resolution) will have zero dependencies on CLI argument frameworks (`System.CommandLine`) or MCP transport protocols (`ModelContextProtocol`).
2. **Sub-5ms Warm CLI Queries via IPC**: A lightweight CLI client can query an already-running `CodeGraphMcp.Server` daemon via Named Pipes / Unix Domain Sockets without re-initializing SQLite connection pools or parsing configuration files from cold disk starts.
3. **Library Reusability**: `CodeGraphMcp.Core` can be referenced by third-party tools (Visual Studio extensions, VS Code extensions, Rider plugins, CI/CD pipelines) directly.
4. **Independent Native AOT Tuning**: Native AOT trimming directives and symbols can be optimized separately for the daemon host vs. the CLI launcher.

---

## 🏗️ Proposed Solution Architecture

```
CodeGraphMcp.slnx
├── src/
│   ├── CodeGraphMcp.Core/         # Class Library (.dll) - Pure domain logic
│   │   ├── Config/                # CodeGraphSettings, codegraphmcp.json loader
│   │   ├── Db/                    # SqliteDbFactory, Schema Migrations, Symbol Repository
│   │   ├── Indexer/               # IndexOrchestrator, FileWatcher, Dirty Buffer Scanner
│   │   ├── Parser/                # C#, TS/JS, HTML/XML, JSON, YAML, CSS, PowerShell Parsers
│   │   ├── Git/                   # GitBlastRadiusAnalyzer, Diff Impact Engine
│   │   ├── CrossStack/            # ServiceStack DTO Contract Resolver
│   │   └── Models/                # SymbolNode, CodeGraphSymbol, CallerReference
│   │
│   ├── CodeGraphMcp.Server/       # Executable (.exe) - MCP Daemon Host
│   │   ├── Mcp/                   # 7 MCP Tool Registrations & JSON-RPC Dispatcher
│   │   ├── Ipc/                   # NamedPipe / Unix Socket Server Listener (Daemon Mode)
│   │   └── Program.cs             # Entry point for `CodeGraphMcp.Server.exe serve`
│   │
│   └── CodeGraphMcp.Cli/          # Executable (.exe) - Command Line Utility (`cg`)
│       ├── Commands/              # IndexCommand, QueryCommand, StatusCommand, PruneCommand
│       ├── Ipc/                   # NamedPipe Client for ultra-fast daemon queries
│       └── Program.cs             # Entry point for CLI command runner
│
└── tests/
    └── CodeGraphMcp.Tests/        # xUnit Test Suite (References Core, Server, and Cli)
```

---

## 📋 Implementation Phases

### Phase 1: Extract `CodeGraphMcp.Core` Class Library
* **Target Project**: `src/CodeGraphMcp.Core/CodeGraphMcp.Core.csproj` (`<OutputType>Library</OutputType>`)
* **Tasks**:
  1. Create `CodeGraphMcp.Core.csproj` targeting `net10.0`.
  2. Move `Config/`, `Db/`, `Indexer/`, `Parser/`, `Git/`, `CrossStack/`, and `Models/` from `src/CodeGraphMcp/` to `CodeGraphMcp.Core/`.
  3. Transfer core NuGet dependencies (`Microsoft.Data.Sqlite` 10.0.11, `SQLitePCLRaw.bundle_e_sqlite3` 3.0.5, `TreeSitter.DotNet` 1.3.0) to `CodeGraphMcp.Core.csproj`.
  4. Ensure all internal service interfaces (`IIndexOrchestrator`, `ISymbolRepository`, `IAstParser`) are cleanly exposed.

### Phase 2: Create `CodeGraphMcp.Server` MCP Daemon Host
* **Target Project**: `src/CodeGraphMcp.Server/CodeGraphMcp.Server.csproj` (`<OutputType>Exe</OutputType>`)
* **Tasks**:
  1. Create `CodeGraphMcp.Server.csproj` targeting `net10.0` with `<PublishAot>true</PublishAot>`.
  2. Add Project Reference to `CodeGraphMcp.Core`.
  3. Move `Mcp/` folder containing the 7 tool definitions (`GetSymbolDefinition`, `FindSymbolCallers`, `SearchCodebaseGraph`, etc.) to `CodeGraphMcp.Server`.
  4. Add `ModelContextProtocol` (2.1.0) and `Serilog` logging dependencies.
  5. Configure `Program.cs` to run the stdio MCP server host loop.

### Phase 3: Create `CodeGraphMcp.Cli` Utility Launcher (`cg`)
* **Target Project**: `src/CodeGraphMcp.Cli/CodeGraphMcp.Cli.csproj` (`<OutputType>Exe</OutputType>`)
* **Tasks**:
  1. Create `CodeGraphMcp.Cli.csproj` targeting `net10.0` with `<PublishAot>true</PublishAot>`.
  2. Add Project Reference to `CodeGraphMcp.Core`.
  3. Move `Commands/CommandFactory.cs` and `CommandLineSanitizer.cs` to `CodeGraphMcp.Cli`.
  4. Add `System.CommandLine` dependency.
  5. Configure `Program.cs` as the entrypoint for CLI command invocation.

### Phase 4: Implement High-Performance IPC Client/Server Protocol (Optional Fast-Path)
* **Goal**: Enable sub-5ms CLI queries by talking to a running `CodeGraphMcp.Server` background process.
* **Mechanism**:
  1. `CodeGraphMcp.Server` opens a Named Pipe listener (`\\.\pipe\codegraphmcp_<repo_hash>`) on startup.
  2. When `cg query` or `cg status` is executed, `CodeGraphMcp.Cli` attempts a fast 2ms pipe connection to the daemon pipe.
  3. **Fallback**: If no daemon process is active, `CodeGraphMcp.Cli` instantiates `CodeGraphMcp.Core` directly in-process to execute the command seamlessly.

### Phase 5: Solution Refactoring & Verification
* **Tasks**:
  1. Add new projects to `CodeGraphMcp.slnx`.
  2. Update `tests/CodeGraphMcp.Tests/CodeGraphMcp.Tests.csproj` to reference `CodeGraphMcp.Core`.
  3. Run full test suite (`dotnet test`) to verify all 79+ tests pass without regression.
  4. Perform Native AOT publish for both `CodeGraphMcp.Server` and `CodeGraphMcp.Cli`.
  5. Update documentation files (`README.md`, `USAGE.md`, `MCP-CONFIGURE.md`, `developer.md`).

---

## 🔍 Risk & Mitigation Strategy

| Risk | Impact | Mitigation Strategy |
| :--- | :--- | :--- |
| **Breaking Existing MCP Paths** | AI Assistants (Antigravity, Copilot, Cursor) rely on current executable path | Maintain a legacy shim/alias executable or update `mcp_config.json` documentation smoothly. |
| **IPC Stale Pipe Locks** | Crashed daemon leaves pipe open | Implement pipe timeouts and automatic fallback to in-process execution. |
| **Native AOT Trimming Errors** | Reflection issues in isolated assemblies | Retain `<EnableTrimAnalyzer>true</EnableTrimAnalyzer>` and explicit `<TrimmerRootAssembly>` settings in both executable `.csproj` files. |

---

## 🚀 Verification Plan

### Automated Tests
```powershell
# Run full unit test suite across decoupled assemblies
dotnet test

# Validate Native AOT publishing for both Server and CLI binaries
dotnet publish src/CodeGraphMcp.Server/CodeGraphMcp.Server.csproj -c Release -o ./publish/server
dotnet publish src/CodeGraphMcp.Cli/CodeGraphMcp.Cli.csproj -c Release -o ./publish/cli
```

### Manual Verification
1. Test CLI commands (`cg index`, `cg query`, `cg status`, `cg prune`) against sample codebase.
2. Connect `CodeGraphMcp.Server.exe` to Antigravity / Claude Desktop `mcp_config.json` and verify all 7 tools function correctly.
