# Implementation Plan — WATCH-01 (FileSystemWatcher for Live MCP Sessions)

Keep the live `DirtyFileBuffer` automatically synchronized when files are created, modified, or deleted during long-running MCP server sessions.

## Architecture Context: Git Pre-Warm vs. FileWatcher Real-Time Updates

```
Startup:   [git status]  --->  DirtyFileBuffer.Refresh() (eager pre-warm, 2-4s)
                                       │
Runtime:   [OS File System Event]  ---> FileWatcher  ---> DirtyFileBuffer.UpdateFile(relPath) (0ms git call)
```

1. **Why `git status` alone isn't enough during runtime:**
   - `DirtyFileBuffer.Refresh()` calls `git status --porcelain` at server startup to discover all existing uncommitted files.
   - Re-running `git status` on every single MCP tool call during a session would add 2–4 seconds of latency to every query (`search_codebase_graph`, `get_symbol_definition`).
2. **How `FileWatcher` complements Git:**
   - `DirtyFileBuffer` initial state is pre-warmed by `git status` at startup.
   - During the session, `FileSystemWatcher` catches file modifications/saves via OS events and calls `DirtyFileBuffer.UpdateFile(relPath)` for just the edited file — keeping the dirty cache 100% live in memory with zero `git` process overhead.

---

## User Review Required

> [!NOTE]
> Debounce interval is set to **300ms** by default to coalesce rapid file system events caused by IDE auto-saves, build scripts, or code formatters.

> [!IMPORTANT]
> The file watcher monitors all subdirectories within the repository root, excluding standard build/tooling folders (`.git`, `.agent-index`, `bin`, `obj`, `publish`, `node_modules`, `dist`, `.next`, `coverage`, `.vs`).

## Open Questions

- None.

## Proposed Changes

### Core Engine (`CodeGraphMcp`)

---

#### [NEW] [FileWatcher.cs](file:///d:/Projects/C%23/CodeGraphMcp/src/CodeGraphMcp/Git/FileWatcher.cs)
- Create `FileWatcher` class wrapping `FileSystemWatcher`.
- Subscribes to `Changed`, `Created`, `Deleted`, and `Renamed` events on the repository path.
- Filters events by supported parser extensions (`.cs`, `.ts`, `.tsx`, `.js`, `.jsx`, `.html`, `.htm`, `.ps1`, `.psm1`, `.psd1`) and ignores blacklisted directories (`.git`, `.agent-index`, `bin`, `obj`, `publish`, `node_modules`, `dist`, `.next`, `coverage`, `.vs`).
- Implements 300ms task-based debouncing (`CancellationTokenSource` per file path) to handle rapid IDE saves cleanly.
- Updates `DirtyFileBuffer` via `UpdateFile(relPath)` for modifications/creations and `Invalidate(relPath)` for deletions/renames.
- Implements `IDisposable` to stop watching and cancel pending timer tasks.

#### [MODIFY] [McpServer.cs](file:///d:/Projects/C%23/CodeGraphMcp/src/CodeGraphMcp/Mcp/McpServer.cs)
- Instantiate `FileWatcher` in `McpServer.RunAsync` after eager pre-warming of `DirtyFileBuffer`.
- Call `watcher.Start()`.
- Ensure `FileWatcher` is disposed when `McpServer.RunAsync` finishes / cancels.

### Test Suite (`CodeGraphMcp.Tests`)

---

#### [NEW] [FileWatcherTests.cs](file:///d:/Projects/C%23/CodeGraphMcp/tests/CodeGraphMcp.Tests/Git/FileWatcherTests.cs)
- `FileWatcher_FileCreated_UpdatesDirtyBuffer`: Verify creating a new source file populates dirty buffer after debounce delay.
- `FileWatcher_FileDeleted_InvalidatesDirtyBuffer`: Verify deleting a file removes it from dirty buffer.
- `FileWatcher_IgnoredDirectory_DoesNotUpdateBuffer`: Verify edits inside ignored directories (e.g. `bin/`, `.git/`) are ignored.
- `FileWatcher_Debounce_CoalescesRapidChanges`: Verify multiple rapid writes to the same file coalesce into a single buffer update.

## Verification Plan

### Automated Tests
- Execute test suite: `dotnet test`
- All unit tests (including new `FileWatcherTests`) must pass cleanly.

### Manual Verification
1. Run `dotnet build src\CodeGraphMcp` to ensure clean build.
2. Start MCP server against a test repo path.
3. Modify a source file on disk and verify live queries reflect the updated symbols without restarting the server.
