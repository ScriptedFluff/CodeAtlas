# Phase 6 Implementation Plan: Multi-Project Solution Support & Unbound MCP Server

Phase 6 enables `CodeGraphMcp` to operate as an unbound MCP server where the target solution or project directory does not need to be configured ahead of time. The AI assistant can pass the target directory dynamically per tool call, and the MCP server will resolve, index, and query the multi-project solution graph on demand.

---

## Technical Overview & Strategic Sequencing

1. 🏆 **Component 1: Dynamic Workspace DB Manager (`WorkspaceDbManager`)**
   - *Goal*: Replace static single-database DI registration with a thread-safe manager that resolves, migrates, and caches `CodeGraphDb` instances keyed by normalized `target_directory` paths.

2. 🥈 **Component 2: On-Demand Auto-Indexer & Dirty File Buffer (`DynamicIndexService`)**
   - *Goal*: Automatically check, warm, or index a solution/project workspace when target tools query an un-indexed or modified `target_directory`.

3. 🥉 **Component 3: Tool Parameter Extensions (`target_directory`)**
   - *Goal*: Update all MCP tools (`GetSymbolDefinition`, `FindSymbolCallers`, `SearchCodebaseGraph`, `GetDiffImpact`, `GetServiceStackDtoContract`) to accept `target_directory` as a tool call parameter.
   - *Fallback*: If `target_directory` is omitted by the AI, fall back to current working directory.

4. 📦 **Component 4: Multi-Project & Solution Root Mapping**
   - *Goal*: Ensure file enumeration, relative path normalization, and symbol links accurately handle solution folders containing multiple project subfolders (e.g., C# `.sln` roots, monorepos).

5. 🚀 **Component 5: Unbound CLI Entry Point (`mcp serve`)**
   - *Goal*: Make `--repo` optional in `mcp serve` CLI command to support starting the server without pre-binding to a fixed directory.

---

## User Review Required

> [!IMPORTANT]
> **On-Demand Auto-Indexing Overhead**: When an AI queries a `target_directory` for the first time via MCP, if the directory is not yet indexed, the MCP server will perform auto-indexing before returning results. To prevent timeouts on very large solutions, initial indexing will report progress and utilize parallel parsing.

> [!NOTE]
> **Backward Compatibility**: If a user runs `CodeGraphMcp mcp serve --repo <path>`, existing single-repo behavior remains fully supported as the default target directory when tool parameters omit `target_directory`.

---

## Proposed Changes

### Component 1: Dynamic Workspace DB Manager

#### [NEW] [WorkspaceDbManager.cs](file:///d:/Projects/C%23/CodeGraphMcp/src/CodeGraphMcp/Db/WorkspaceDbManager.cs)
- Thread-safe dictionary caching `CodeGraphDb` connections by normalized target directory path.
- Resolves DB file location using `StorageConfig.ResolveDbPath(null, storeLocal: false, targetDir)`.
- Runs DB migration on first load for each workspace.

---

### Component 2: On-Demand Auto-Indexer & Workspace Service

#### [NEW] [DynamicWorkspaceService.cs](file:///d:/Projects/C%23/CodeGraphMcp/src/CodeGraphMcp/Services/DynamicWorkspaceService.cs)
- Encapsulates DB connection retrieval, dirty file buffer caching, and auto-indexing per target directory.
- Checks if target DB has symbols; triggers `IndexOrchestrator.IndexAsync(targetDir)` if unindexed or stale.

---

### Component 3: MCP Tool Parameter Extensions

#### [MODIFY] [GetSymbolDefinitionTool.cs](file:///d:/Projects/C%23/CodeGraphMcp/src/CodeGraphMcp/Mcp/Tools/GetSymbolDefinitionTool.cs)
#### [MODIFY] [FindSymbolCallersTool.cs](file:///d:/Projects/C%23/CodeGraphMcp/src/CodeGraphMcp/Mcp/Tools/FindSymbolCallersTool.cs)
#### [MODIFY] [SearchCodebaseGraphTool.cs](file:///d:/Projects/C%23/CodeGraphMcp/src/CodeGraphMcp/Mcp/Tools/SearchCodebaseGraphTool.cs)
#### [MODIFY] [GetDiffImpactTool.cs](file:///d:/Projects/C%23/CodeGraphMcp/src/CodeGraphMcp/Mcp/Tools/GetDiffImpactTool.cs)
#### [MODIFY] [GetServiceStackDtoContractTool.cs](file:///d:/Projects/C%23/CodeGraphMcp/src/CodeGraphMcp/Mcp/Tools/GetServiceStackDtoContractTool.cs)
- Add optional `TargetDirectory` property (with description for AI model guidance) to all tool input records.
- Obtain target `CodeGraphDb` and `DirtyFileBuffer` dynamically from `DynamicWorkspaceService`.

---

### Component 4: Server Setup & Unbound CLI Option

#### [MODIFY] [McpServer.cs](file:///d:/Projects/C%23/CodeGraphMcp/src/CodeGraphMcp/Mcp/McpServer.cs)
- Register `WorkspaceDbManager` and `DynamicWorkspaceService` in DI.
- Accept nullable `defaultRepoPath` during server startup.

#### [MODIFY] [McpCommand.cs](file:///d:/Projects/C%23/CodeGraphMcp/src/CodeGraphMcp/Commands/McpCommand.cs)
- Change `--repo` CLI option from required to optional (defaulting to current directory or unbound mode).

---

## Verification Plan

### Automated Tests
- **WorkspaceDbManager Tests**:
  - Test opening and caching DBs for multiple distinct target directories concurrently.
  - Verify isolation between different workspace databases.
- **Dynamic MCP Tool Tests**:
  - Execute `GetSymbolDefinitionTool` and `SearchCodebaseGraphTool` passing different `target_directory` arguments.
  - Verify auto-indexing triggers correctly for new target solution directories.
- **Multi-Project Solution Parsing Tests**:
  - Index a solution workspace containing 2+ `.csproj` subfolders.
  - Verify caller resolution and symbol lookups span across projects correctly relative to the solution root.

### Manual Verification
- Launch `CodeGraphMcp mcp serve` without `--repo`.
- Simulate MCP tool requests supplying `target_directory` pointing to multi-project C# solutions and verify accurate code graph responses.

---

## Future Plans (Formerly Phase 7)

Phase 7 features have been moved to future plans for potential future development:

### 1. 🏆 .gitignore Support & Filtering
- **Goal**: Implement standard `.gitignore` pattern matching supporting root and nested `.gitignore` files.
- **Integration**: Filter files in `IndexOrchestrator` scanning, `FileWatcher`, and `DeltaEngine` to skip `bin/`, `obj/`, `node_modules/`, `.git/`, `.agent-index/`, and user-ignored build artifacts.

### 2. 🥈 Language-Agnostic Multi-File Symbol Declaration Merging
- **Goal**: Support symbols split across multiple files in a language-agnostic way (e.g., C# `partial` types, TypeScript interface/namespace merging, module augmentations).
- **Database & Graph**: Update `SymbolDb` and reference resolution logic so multi-file declarations aggregate member definitions and declaration sites without overwriting each other in the reference graph.

### 3. 🥉 JavaScript & JSX Support
- **Goal**: Extend language parsing to `.js`, `.jsx`, `.mjs`, and `.cjs` files.
- **Implementation**: Reuse/adapt Tree-Sitter AST parsing from `TypeScriptParser` for JS/JSX function declarations, ES6 modules, CommonJS `require`, and method invocations.

### 4. 📦 Modular / Drop-in Parser Architecture
- **Goal**: Introduce a `ParserRegistry` to dynamically register and discover `ILanguageParser` implementations.
- **Future Readiness**: Decouples language parsers from hardcoded orchestrator arrays, enabling an extensible plugin/loader architecture.

