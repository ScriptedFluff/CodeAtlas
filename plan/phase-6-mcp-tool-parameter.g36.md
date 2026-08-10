# Phase 6 Detailed Plan: MCP Tool Parameter Extensions

This plan details the implementation of **MCP Tool Parameter Extensions** as part of Phase 6 (Multi-Project Solution Support & Unbound MCP Server).

It defines how all MCP tools (`GetSymbolDefinitionTool`, `FindSymbolCallersTool`, `SearchCodebaseGraphTool`, `GetDiffImpactTool`, and `GetServiceStackDtoContractTool`) accept an optional `target_directory` parameter to query, index, and resolve symbol graph data dynamically across multiple workspace roots or multi-project solution subdirectories.

---

## Technical Overview & Strategic Sequencing

1. 🎯 **Goal 1: Consistent `target_directory` Parameter Exposure & Guidance**
   - Ensure all 5 MCP tool methods expose `string? targetDirectory = null` with explicit `[Description]` attributes guiding AI agents when to supply a target workspace path (e.g., in multi-repo or multi-project solutions).
   - Enforce default fallback: when `targetDirectory` is `null` or empty, `IDynamicWorkspaceService` falls back to the default server repository path or current working directory.

2. 🔄 **Goal 2: Dynamic Workspace & Dirty Buffer Resolution**
   - Ensure tools scope both SQLite database queries (`CodeGraphDb`) and dirty file buffer lookups (`DirtyFileBuffer`) to the workspace root resolved by `IDynamicWorkspaceService.GetOrIndexWorkspaceAsync(targetDirectory, ct)`.

3. 🔗 **Goal 3: Dynamic ServiceStack Cross-Stack Linker Binding**
   - Update `GetServiceStackDtoContractTool` and `ServiceStackLinker` registration/usage so cross-stack DTO mapping operates dynamically on the target workspace database rather than relying on a fixed, static DI singleton DB.

4. 🧪 **Goal 4: Automated Testing & Schema Validation**
   - Implement tests verifying multi-workspace isolation, tool invocation with custom `target_directory` paths, and schema export accuracy for AI client consumption.

---

## Proposed Changes

### Component 1: MCP Tool Parameter Guidance & Workspace Scope

#### [MODIFY] [GetSymbolDefinitionTool.cs](file:///d:/Projects/C%23/CodeGraphMcp/src/CodeGraphMcp/Mcp/Tools/GetSymbolDefinitionTool.cs)
- Verify `targetDirectory` parameter has detailed description: `"Optional: target repository/workspace directory to query (defaults to active workspace)"`.
- Resolve workspace via `await workspaceService.GetOrIndexWorkspaceAsync(targetDirectory, ct)`.
- Use `bufferManager.GetOrCreateBuffer(context.WorkspaceRoot)` for dirty symbol buffer lookup.

#### [MODIFY] [FindSymbolCallersTool.cs](file:///d:/Projects/C%23/CodeGraphMcp/src/CodeGraphMcp/Mcp/Tools/FindSymbolCallersTool.cs)
- Pass `targetDirectory` into `workspaceService.GetOrIndexWorkspaceAsync(targetDirectory, ct)`.
- Query `context.Db.References.FindCallers(symbolName)`.

#### [MODIFY] [SearchCodebaseGraphTool.cs](file:///d:/Projects/C%23/CodeGraphMcp/src/CodeGraphMcp/Mcp/Tools/SearchCodebaseGraphTool.cs)
- Pass `targetDirectory` to `workspaceService.GetOrIndexWorkspaceAsync(targetDirectory, ct)`.
- Scope SQLite query and `bufferManager.GetOrCreateBuffer(context.WorkspaceRoot)` to the target workspace.

#### [MODIFY] [GetDiffImpactTool.cs](file:///d:/Projects/C%23/CodeGraphMcp/src/CodeGraphMcp/Mcp/Tools/GetDiffImpactTool.cs)
- Pass `targetDirectory` to `workspaceService.GetOrIndexWorkspaceAsync(targetDirectory, ct)`.
- Use `context.WorkspaceRoot` for Git queries (`GetHeadCommitHash`, `GetCurrentBranch`, `GetChangedFiles`, `GetDirtyFiles`).

---

### Component 2: Dynamic ServiceStack Linker Resolution

#### [MODIFY] [GetServiceStackDtoContractTool.cs](file:///d:/Projects/C%23/CodeGraphMcp/src/CodeGraphMcp/Mcp/Tools/GetServiceStackDtoContractTool.cs)
- Replace static `ServiceStackLinker` constructor parameter with dynamic resolution per workspace DB, or inject a factory `Func<CodeGraphDb, ServiceStackLinker>` / instantiate `new ServiceStackLinker(context.Db)`.
- Call `linker.EnsureLinked()` against the target workspace `CodeGraphDb`.

#### [MODIFY] [McpServer.cs](file:///d:/Projects/C%23/CodeGraphMcp/src/CodeGraphMcp/Mcp/McpServer.cs)
- Update DI registrations to remove single-tenant `ServiceStackLinker` singleton and ensure dynamic workspace resolution.

---

### Component 3: Serialization & Schema Safety

#### [MODIFY] [McpJsonContext.cs](file:///d:/Projects/C%23/CodeGraphMcp/src/CodeGraphMcp/Mcp/McpJsonContext.cs)
- Validate all parameter types and tool result record types are registered in `McpJsonContext` for AOT-safe JSON serialization.

---

## Verification Plan

### Automated Tests
- **Dynamic Tool Parameter Integration Tests**:
  - Run unit tests calling each tool with `targetDirectory = null` and with an explicit directory path pointing to a secondary solution folder.
  - Verify queries return data specific to the specified workspace without cross-contamination.
- **ServiceStack Cross-Stack Dynamic Test**:
  - Execute `GetServiceStackDtoContractTool` on a workspace with C# and TS DTO files via `targetDirectory`.
  - Verify linking happens on the target DB.

### Manual Verification
- Test via MCP Inspector or CLI tool runner passing `{ "target_directory": "./tests/Fixtures/SampleSolution" }` and confirming valid symbol resolution.
