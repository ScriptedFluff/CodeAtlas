# Phase 6 Implementation Plan: On-Demand Workspace Service & Dynamic Indexing

This plan details the design, architecture, file changes, and verification strategy for the **On-Demand Workspace Service** (`DynamicWorkspaceService`) and **Workspace Database Manager** (`WorkspaceDbManager`). 

This component enables `CodeGraphMcp` to operate as an **unbound MCP server**. The AI assistant can supply an optional `target_directory` parameter with any tool call, allowing the server to dynamically locate, open, index, cache, and query code graphs across multiple project and solution workspaces on demand.

---

## Technical Overview & Strategic Sequencing

To decouple `CodeGraphMcp` from a single static repository path supplied at startup, the system will implement a two-tiered dynamic workspace service:

```
                               ┌───────────────────────────────────┐
                               │     MCP Tool Invocation           │
                               │ (e.g. target_directory: "d:/App") │
                               └─────────────────┬─────────────────┘
                                                 │
                                                 ▼
                               ┌───────────────────────────────────┐
                               │      DynamicWorkspaceService      │
                               │   (Resolves, indexes, refreshes)  │
                               └─────────────────┬─────────────────┘
                                                 │
                                ┌────────────────┴────────────────┐
                                ▼                                 ▼
                     ┌─────────────────────┐           ┌────────────────────┐
                     │ WorkspaceDbManager  │           │ IndexOrchestrator  │
                     │ (Cache & DB pools)  │           │ (On-demand index)  │
                     └──────────┬──────────┘           └────────────────────┘
                                │
                                ▼
                     ┌─────────────────────┐
                     │     CodeGraphDb     │
                     │ (.agent-index/*.db) │
                     └─────────────────────┘
```

Execution sequence:

1. 🏆 **Component 1: Workspace Path Normalizer & Boundary Detector (`WorkspacePathResolver`)**
   - *Goal*: Resolve input target directory to canonical absolute paths, detect solution (`.sln`/`.slnx`) or project boundaries, and normalize paths consistently across operating systems.
2. 🥈 **Component 2: Thread-Safe Dynamic Database Manager (`WorkspaceDbManager`)**
   - *Goal*: Replace single-instance `CodeGraphDb` DI registration with a thread-safe cache manager that opens, migrates, caches, and recycles SQLite databases keyed by normalized workspace root paths.
3. 🥉 **Component 3: On-Demand Workspace & Auto-Indexing Service (`DynamicWorkspaceService`)**
   - *Goal*: Coordinate workspace DB resolution, check index initialization/freshness, trigger `IndexOrchestrator` on demand when an un-indexed workspace is queried, and handle dirty file buffer updates.
4. 📦 **Component 4: Tool Parameter Extensions & Unbound CLI Options**
   - *Goal*: Add optional `target_directory` parameter to all MCP tools, update DI registrations in `McpServer.cs`, and make `--repo` CLI flag optional.

---

## User Review Required

> [!IMPORTANT]
> **Database Storage Location**: By default, `CodeGraphDb` stores `.agent-index/codegraph.db` inside the target workspace directory. If a target workspace directory is read-only (e.g., system directory or external dependency repo), `WorkspaceDbManager` will fall back to storing the database in `%LOCALAPPDATA%/CodeGraphMcp/Cache/<hash>/codegraph.db`.

> [!NOTE]
> **Fallback & Default Workspace Behavior**: If an MCP tool call omits the `target_directory` argument:
> 1. Fall back to `--repo` argument if supplied when starting `CodeGraphMcp mcp serve`.
> 2. If `--repo` was omitted, fall back to current working directory (`Environment.CurrentDirectory`).

---

## Open Questions

> [!IMPORTANT]
> 1. Should concurrent requests for the *same un-indexed target directory* block and await a single shared indexing task? *(Recommended: Yes, use an asynchronous lock (`SemaphoreSlim`) per workspace path to prevent duplicate parallel indexing runs).*
> 2. Should active file watchers (`FileWatcher`) be dynamically attached to workspaces as they are accessed on demand? *(Recommended: Yes, lazy-initialize `FileWatcher` instances per active workspace to keep the code graph updated when files change on disk).*

---

## Proposed Changes

---

### Component 1: Workspace Path Normalizer & Boundary Resolver

#### [NEW] [WorkspacePathResolver.cs](file:///d:/Projects/C%23/CodeGraphMcp/src/CodeGraphMcp/Config/WorkspacePathResolver.cs)
- Provide path normalization logic (`NormalizePath(string rawPath)`) resolving relative paths, symlinks, casing, and trailing slashes.
- Implement solution and repository boundary detection:
  - Walk up directory tree from input target path to locate `.sln`, `.slnx`, `.git`, `package.json`, or project files (`.csproj`, `tsconfig.json`).
  - Return normalized workspace root path and database path (`.agent-index/codegraph.db`).

---

### Component 2: Dynamic Database Manager (`WorkspaceDbManager`)

#### [NEW] [WorkspaceDbManager.cs](file:///d:/Projects/C%23/CodeGraphMcp/src/CodeGraphMcp/Db/WorkspaceDbManager.cs)
- Implement `IWorkspaceDbManager` interface to manage cached `CodeGraphDb` instances.
- Maintain a thread-safe `ConcurrentDictionary<string, CodeGraphDb>` map keyed by normalized workspace root paths.
- Method `GetOrCreateDb(string workspaceRoot)`:
  - Resolves target DB file path (`<workspaceRoot>/.agent-index/codegraph.db` or fallback cache path).
  - Initializes `CodeGraphDb`, ensuring tables, PRAGMA settings, and indexes are created via SQLite migration logic.
- Implement `IDisposable` / `IAsyncDisposable` to properly close SQLite connection pools during application shutdown.

---

### Component 3: On-Demand Workspace Service (`DynamicWorkspaceService`)

#### [NEW] [DynamicWorkspaceService.cs](file:///d:/Projects/C%23/CodeGraphMcp/src/CodeGraphMcp/Indexer/DynamicWorkspaceService.cs)
- Provide primary workspace resolution & indexing method:
  ```csharp
  public async Task<WorkspaceContext> GetOrIndexWorkspaceAsync(string? targetDirectory, CancellationToken ct = default)
  ```
- Steps:
  1. Determine effective target path (input `targetDirectory` -> `--repo` fallback -> CWD fallback).
  2. Resolve workspace root via `WorkspacePathResolver`.
  3. Acquire `WorkspaceContext` lock to prevent redundant concurrent index operations.
  4. Obtain `CodeGraphDb` instance via `WorkspaceDbManager`.
  5. Check if index is missing or empty (`db.Symbols.GetCount() == 0` or timestamp check).
  6. If indexing is required, invoke `IndexOrchestrator.IndexAsync(workspaceRoot, ct)` on demand.
  7. Return `WorkspaceContext` wrapping the workspace root path, relative path resolver, and active `CodeGraphDb`.

#### [MODIFY] [IndexOrchestrator.cs](file:///d:/Projects/C%23/CodeGraphMcp/src/CodeGraphMcp/Indexer/IndexOrchestrator.cs)
- Refactor constructor to accept target `CodeGraphDb` directly or allow passing target DB context during `IndexAsync` invocation.

---

### Component 4: Tool Parameter Extensions & Unbound CLI Server Setup

#### [MODIFY] [GetSymbolDefinitionTool.cs](file:///d:/Projects/C%23/CodeGraphMcp/src/CodeGraphMcp/Mcp/Tools/GetSymbolDefinitionTool.cs)
#### [MODIFY] [FindSymbolCallersTool.cs](file:///d:/Projects/C%23/CodeGraphMcp/src/CodeGraphMcp/Mcp/Tools/FindSymbolCallersTool.cs)
#### [MODIFY] [SearchCodebaseGraphTool.cs](file:///d:/Projects/C%23/CodeGraphMcp/src/CodeGraphMcp/Mcp/Tools/SearchCodebaseGraphTool.cs)
#### [MODIFY] [GetDiffImpactTool.cs](file:///d:/Projects/C%23/CodeGraphMcp/src/CodeGraphMcp/Mcp/Tools/GetDiffImpactTool.cs)
#### [MODIFY] [GetServiceStackDtoContractTool.cs](file:///d:/Projects/C%23/CodeGraphMcp/src/CodeGraphMcp/Mcp/Tools/GetServiceStackDtoContractTool.cs)
- Update tool input schemas to include optional `target_directory` parameter (e.g. `[JsonPropertyName("target_directory")] public string? TargetDirectory { get; init; }`).
- Replace direct constructor dependency on `CodeGraphDb` with `DynamicWorkspaceService`.
- Resolve target workspace dynamically before executing queries:
  ```csharp
  var context = await dynamicWorkspaceService.GetOrIndexWorkspaceAsync(input.TargetDirectory, ct);
  ```

#### [MODIFY] [McpServer.cs](file:///d:/Projects/C%23/CodeGraphMcp/src/CodeGraphMcp/Mcp/McpServer.cs)
- Register `WorkspacePathResolver`, `WorkspaceDbManager`, and `DynamicWorkspaceService` in dependency injection.
- Pass optional default repository path (from CLI `--repo` flag if provided).

#### [MODIFY] [McpCommand.cs](file:///d:/Projects/C%23/CodeGraphMcp/src/CodeGraphMcp/Commands/McpCommand.cs)
- Mark `--repo` CLI option as optional, enabling server execution in unbound mode.

---

## Verification Plan

### Automated Tests
- **`WorkspacePathResolverTests`**:
  - Test path normalization across Windows/Linux separators (`\`, `/`), relative paths (`../`), and casing.
  - Test solution and project root boundary detection (`.sln`, `.slnx`, `.csproj`, `tsconfig.json`, `package.json`, `.git`).
- **`WorkspaceDbManagerTests`**:
  - Test concurrent acquisition of DB instances for multiple target directories.
  - Verify SQLite isolation and proper connection disposal on shutdown.
- **`DynamicWorkspaceServiceTests`**:
  - Test auto-indexing trigger when querying an un-indexed target directory.
  - Verify concurrent queries to the same un-indexed directory execute indexing only once.
  - Test fallback chain (input path -> CLI repo -> CWD).
- **`DynamicMcpToolTests`**:
  - Test `GetSymbolDefinitionTool` and `SearchCodebaseGraphTool` invocations passing distinct `target_directory` arguments.

### Manual Verification
- Launch `CodeGraphMcp mcp serve` without `--repo`.
- Use JSON-RPC or MCP test client to send tool requests targeting multiple different solution directories.
- Confirm auto-indexing logs and accurate symbol definition / caller responses per target workspace.
