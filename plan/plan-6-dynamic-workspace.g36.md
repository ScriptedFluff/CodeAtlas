# Phase 6-1 Implementation Plan: Dynamic Workspace DB Manager

Phase 6-1 establishes the foundation for multi-project and unbound MCP server execution by introducing `WorkspaceDbManager`. This service replaces static single-database DI registration with a thread-safe, cached manager that dynamically resolves, migrates, and manages `CodeGraphDb` instances keyed by normalized target workspace directories.

---

## Technical Overview & Architecture

Currently, `McpServer` registers a single, fixed `CodeGraphDb` in `IServiceCollection` based on a required CLI argument `--repo`. 
To support unbound MCP servers and dynamic multi-directory querying, `WorkspaceDbManager`:
1. **Normalizes Workspace Paths**: Resolves target directory paths to full canonical paths and maps subdirectories to their containing workspace root (e.g. Git root).
2. **Caches Active Database Connections**: Maintains a thread-safe `ConcurrentDictionary<string, CodeGraphDb>` mapping canonical workspace keys to open `CodeGraphDb` SQLite connections.
3. **Thread-Safe Initialization**: Ensures database creation and `db.Migrate()` schema migrations execute atomically per workspace root, preventing SQLite locking conflicts when tool calls arrive concurrently.
4. **Lifecycle & Cleanup**: Implements `IDisposable` to cleanly flush WAL logs and close SQLite connections across all active workspace databases when the MCP server shuts down.

---

## User Review Required

> [!NOTE]
> **Workspace Root Resolution Strategy**: When an AI model calls a tool with `target_directory: "d:/Projects/MySln/src/ProjectA"`, `WorkspaceDbManager` will resolve the canonical workspace root (e.g. `d:/Projects/MySln` if a Git root is present). This ensures multi-project solutions share a single unified SQLite database index.

> [!IMPORTANT]
> **Atomic Instantiation Pattern**: `ConcurrentDictionary` with `Lazy<CodeGraphDb>` is used to prevent thread races during database creation and migration when multiple concurrent JSON-RPC requests target the same unindexed workspace.

---

## Proposed Changes

### Component 1: Workspace DB Manager Core Implementation

#### [NEW] [IWorkspaceDbManager.cs](file:///d:/Projects/C%23/CodeGraphMcp/src/CodeGraphMcp/Db/IWorkspaceDbManager.cs)
- Define interface for dynamic workspace DB resolution:
```csharp
namespace CodeGraphMcp.Db;

public interface IWorkspaceDbManager : IDisposable
{
    CodeGraphDb GetOrCreateDb(string targetDirectory);
    string ResolveWorkspaceRoot(string targetDirectory);
    IReadOnlyCollection<string> ActiveWorkspaces { get; }
}
```

#### [NEW] [WorkspaceDbManager.cs](file:///d:/Projects/C%23/CodeGraphMcp/src/CodeGraphMcp/Db/WorkspaceDbManager.cs)
- Implement `IWorkspaceDbManager`:
  - Constructor accepts optional `dbDirOverride` (`string?`) and `storeLocal` (`bool`).
  - Use `ConcurrentDictionary<string, Lazy<CodeGraphDb>>` for atomic lazy initialization.
  - Implement `ResolveWorkspaceRoot(string targetDirectory)`:
    - Normalizes path using `Path.GetFullPath(targetDirectory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)`.
    - Looks up Git root via `GitHelpers.FindGitRoot(normalizedPath)`; if found, returns Git root; otherwise returns normalized target directory.
  - Implement `GetOrCreateDb(string targetDirectory)`:
    - Resolves workspace key using `ResolveWorkspaceRoot(targetDirectory)`.
    - Resolves DB file path using `StorageConfig.ResolveDbPath(dbDirOverride, storeLocal, workspaceKey)`.
    - Atomically instantiates `CodeGraphDb(dbPath)` and runs `db.Migrate()`.
  - Implement `Dispose()`:
    - Iterates over instantiated `Lazy<CodeGraphDb>` instances and disposes each `CodeGraphDb` connection safely.

---

### Component 2: Service & DI Registration Integration

#### [MODIFY] [McpServer.cs](file:///d:/Projects/C%23/CodeGraphMcp/src/CodeGraphMcp/Mcp/McpServer.cs)
- Update `McpServer.RunAsync` to register `IWorkspaceDbManager` as a Singleton in `IServiceCollection`.
- Remove static `CodeGraphDb` singleton registration; tools and services will retrieve `CodeGraphDb` dynamically via `IWorkspaceDbManager` or `DynamicWorkspaceService`.

---

### Component 3: Test Suite

#### [NEW] [WorkspaceDbManagerTests.cs](file:///d:/Projects/C%23/CodeGraphMcp/tests/CodeGraphMcp.Tests/WorkspaceDbManagerTests.cs)
- Unit tests covering:
  1. **Canonical Root Resolution**: Verifies subdirectories inside a Git repository resolve to the same workspace root key.
  2. **Multi-Workspace Database Isolation**: Verifies distinct target directories generate distinct, isolated database connections.
  3. **Concurrent Access Thread Safety**: Verifies parallel tasks requesting `GetOrCreateDb` for the same directory return the same `CodeGraphDb` instance without throwing SQLite locks or duplicate migration errors.
  4. **Clean Disposal**: Verifies disposing `WorkspaceDbManager` closes all underlying SQLite database connections.

---

## Verification Plan

### Automated Tests
- Execute unit tests using `dotnet test`:
  ```powershell
  dotnet test tests/CodeGraphMcp.Tests/CodeGraphMcp.Tests.csproj --filter "FullyQualifiedName~WorkspaceDbManagerTests"
  ```
- Run the complete unit test suite:
  ```powershell
  dotnet test
  ```

### Manual Verification
- Test creating DB instances for multiple workspace paths in an interactive C# script / test scenario.
- Confirm SQLite `.db` files are created under `%LOCALAPPDATA%\CodeGraph\dbs\` with proper schema tables populated upon migration.
