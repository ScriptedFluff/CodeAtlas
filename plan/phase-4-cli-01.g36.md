# Phase 4 Implementation Plan — REF-01, WATCH-01 & CLI-01

**Model Signature:** Gemini 3.6 Flash (g36)  
**Status:** REF-01 Completed & Committed (`4b939e4`) · WATCH-01 & CLI-01 Ready for Implementation

---

## Overview

Phase 4 completes the knowledge graph engine by:
1. **REF-01 (Completed):** Extracting cross-file invocation/usage edges into `symbol_references` with scoped symbol resolution and top-level script fallback.
2. **WATCH-01:** Adding `FileWatcher` to auto-synchronize live `DirtyFileBuffer` during long-running MCP sessions on file saves/creates/deletions.
3. **CLI-01:** Shipping diagnostic and maintenance subcommands (`status`, `watch`, `clean`) in `Program.cs`.

---

## Completed Work Unit — REF-01 (Symbol Reference Extraction)

### Implemented Changes
- **Core Models & Interfaces:**
  - `SymbolReference` record ([SymbolReference.cs](file:///d:/Projects/C%23/CodeAtlas/src/CodeAtlas/Models/SymbolReference.cs)).
  - `ILanguageParser.ParseReferences` default interface method ([ILanguageParser.cs](file:///d:/Projects/C%23/CodeAtlas/src/CodeAtlas/Parser/ILanguageParser.cs)).
- **AST Parsers:**
  - `CSharpParser.ParseReferences` for `Call`, `TypeUsage`, `Inheritance` ([CSharpParser.cs](file:///d:/Projects/C%23/CodeAtlas/src/CodeAtlas/Parser/CSharpParser.cs)).
  - `TypeScriptParser.ParseReferences` for `Call`, `TypeUsage`, `Import` ([TypeScriptParser.cs](file:///d:/Projects/C%23/CodeAtlas/src/CodeAtlas/Parser/TypeScriptParser.cs)).
- **Database Layer & Resolution:**
  - Migration 004 (`idx_refs_file`) in [MigrationRunner.cs](file:///d:/Projects/C%23/CodeAtlas/src/CodeAtlas/Db/MigrationRunner.cs).
  - Scoped symbol resolution (`ResolveId` with `preferredFilePath`) and top-level file fallback (`GetPrimarySymbolIdForFile`) in [SymbolQueries.cs](file:///d:/Projects/C%23/CodeAtlas/src/CodeAtlas/Db/Queries/SymbolQueries.cs).
  - `DeleteByFile` and `UpsertFileReferences` in [ReferenceQueries.cs](file:///d:/Projects/C%23/CodeAtlas/src/CodeAtlas/Db/Queries/ReferenceQueries.cs).
- **Indexing Engine:**
  - 2-pass indexing in [IndexOrchestrator.cs](file:///d:/Projects/C%23/CodeAtlas/src/CodeAtlas/Indexer/IndexOrchestrator.cs).
  - Reference updates and file deletion cleanup in [DeltaEngine.cs](file:///d:/Projects/C%23/CodeAtlas/src/CodeAtlas/Git/DeltaEngine.cs).
- **Testing:**
  - 21 unit tests passing in [ReferenceTests.cs](file:///d:/Projects/C%23/CodeAtlas/tests/CodeAtlas.Tests/ReferenceTests.cs).

---

## Remaining Work Unit 1 — WATCH-01 (File Watcher for Live MCP Server)

### Objective
Automatically update `DirtyFileBuffer` when files are modified, created, or deleted during an active `CodeAtlas serve` session.

### Proposed Changes

#### [NEW] [FileWatcher.cs](file:///d:/Projects/C%23/CodeAtlas/src/CodeAtlas/Git/FileWatcher.cs)
- Create `FileWatcher` class wrapping `FileSystemWatcher` with a 300ms debounce timer.
- Handle `Changed`, `Created`, `Deleted`, and `Renamed` events.
- On file change/creation: call `DirtyFileBuffer.UpdateFile(relPath)`.
- On file deletion: call `DirtyFileBuffer.Invalidate(relPath)`.

#### [MODIFY] [McpServer.cs](file:///d:/Projects/C%23/CodeAtlas/src/CodeAtlas/Mcp/McpServer.cs)
- Instantiate and start `FileWatcher` when MCP server starts.
- Ensure proper disposal when MCP server shuts down.

---

## Remaining Work Unit 2 — CLI-01 (Diagnostic & Utility Subcommands)

### Objective
Expose CLI utilities to inspect index health, wipe state, and run continuous background delta re-indexing.

### Proposed Subcommands in [Program.cs](file:///d:/Projects/C%23/CodeAtlas/src/CodeAtlas/Program.cs)

1. **`status`**
   - Displays database location, commit hash, indexed branch, total file count, symbol count, and reference count.
2. **`clean`**
   - Options: `--yes` (`-y`) to skip confirmation.
   - Deletes SQLite database files (`.db`, `-wal`, `-shm`).
3. **`watch`**
   - Options: `--interval <seconds>` (default 5s).
   - Periodically polls git working tree and triggers `DeltaEngine.RunAsync()`.

---

## Verification Plan

### Automated Tests
- Run `dotnet test` (all tests passing).
- Add unit tests for `FileWatcher` debounce logic.

### CLI & Manual Verification
```powershell
dotnet build src/CodeAtlas
dotnet test
./publish/CodeAtlas.exe status D:\Projects\C#\CodeAtlas --store-local
./publish/CodeAtlas.exe clean D:\Projects\C#\CodeAtlas --store-local --yes
```
