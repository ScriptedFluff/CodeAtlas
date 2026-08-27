# Phase 7 Implementation Plan: Performance Optimizations & Worktree DB Seeding

Phase 7 focuses on drastic performance optimizations for indexing large repositories (reducing full index times from minutes to seconds) and introducing cross-worktree/branch database seeding to leverage existing index states across git worktrees and branch switches.

---

## User Review Required

> [!IMPORTANT]
> **Single-Pass Parsing Breaking Change in `ILanguageParser`**: The `ILanguageParser` interface will be extended with a unified `FileParseResult ParseFile(string filePath, string source)` method to extract both symbols and references in a single AST parse pass.

> [!NOTE]
> **Worktree DB Seeding Behavior**: When an unindexed git worktree or branch is detected, `CodeAtlas` will look for existing indexed databases for the same repository in `.agent-index` or global storage. If found, it will copy the database snapshot and perform an incremental `git diff` delta index instead of doing a full re-parse.

---

## Proposed Changes

### Component 1: Single-Pass Combined AST Parsing & Parser Pooling

#### [MODIFY] [ILanguageParser.cs](file:///d:/Projects/C%23/CodeAtlas/src/CodeAtlas/Parser/ILanguageParser.cs)
- Add `FileParseResult ParseFile(string filePath, string source)` record containing `IReadOnlyList<Symbol> Symbols` and `IReadOnlyList<SymbolReference> References`.
- Provide default fallback implementation for backward compatibility.

#### [MODIFY] [CSharpParser.cs](file:///d:/Projects/C%23/CodeAtlas/src/CodeAtlas/Parser/CSharpParser.cs)
#### [MODIFY] [TypeScriptParser.cs](file:///d:/Projects/C%23/CodeAtlas/src/CodeAtlas/Parser/TypeScriptParser.cs)
#### [MODIFY] [JavaScriptParser.cs](file:///d:/Projects/C%23/CodeAtlas/src/CodeAtlas/Parser/JavaScriptParser.cs)
#### [MODIFY] [HtmlParser.cs](file:///d:/Projects/C%23/CodeAtlas/src/CodeAtlas/Parser/HtmlParser.cs)
#### [MODIFY] [PowerShellParser.cs](file:///d:/Projects/C%23/CodeAtlas/src/CodeAtlas/Parser/PowerShellParser.cs)
- Implement `ParseFile` to traverse tree-sitter AST **once** per file and collect both symbols and references.
- Utilize thread-local parser caching to avoid native tree-sitter parser allocations per file.

---

### Component 2: Lock-Free Parallel Parsing & Bulk SQLite Transactions

#### [MODIFY] [IndexOrchestrator.cs](file:///d:/Projects/C%23/CodeAtlas/src/CodeAtlas/Indexer/IndexOrchestrator.cs)
- Remove `lock (db)` inside `Parallel.ForEachAsync`. Collect all parsed symbols and references in concurrent thread-safe collections in memory.
- Perform batch symbol and reference insertions inside a single transaction using `UpsertSymbolsBatch` and `UpsertReferencesBatch`.

#### [MODIFY] [SymbolQueries.cs](file:///d:/Projects/C%23/CodeAtlas/src/CodeAtlas/Db/Queries/SymbolQueries.cs)
#### [MODIFY] [ReferenceQueries.cs](file:///d:/Projects/C%23/CodeAtlas/src/CodeAtlas/Db/Queries/ReferenceQueries.cs)
- Add `UpsertSymbolsBatch(IReadOnlyList<(string FilePath, IReadOnlyList<Symbol> Symbols)> batch)` executing bulk inserts in a single transaction.
- Add `UpsertReferencesBatch(IReadOnlyList<(string FilePath, IReadOnlyList<SymbolReference> Refs)> batch, SymbolLookupCache cache)`.

---

### Component 3: In-Memory Symbol Lookup Cache ($O(1)$ Reference Resolution)

#### [NEW] [SymbolLookupCache.cs](file:///d:/Projects/C%23/CodeAtlas/src/CodeAtlas/Db/SymbolLookupCache.cs)
- In-memory dictionary mapping `(SymbolName, FilePath)` -> `SymbolId` and `FilePath` -> `PrimarySymbolId`.
- Populated after bulk symbol insertion or cached dynamically.
- Eliminates individual SQL `SELECT` queries during reference insertion.

---

### Component 4: Worktree DB Seeding & Cross-Branch Delta Seeding

#### [NEW] [DbSeedingService.cs](file:///d:/Projects/C%23/CodeAtlas/src/CodeAtlas/Db/DbSeedingService.cs)
- Identifies git repository identity via common commit hash or git remote origin URL.
- Scans local `.agent-index` and global database paths for pre-existing index DBs of the same repo.
- If found, copies the database file to target path and records source commit hash.

#### [MODIFY] [DeltaEngine.cs](file:///d:/Projects/C%23/CodeAtlas/src/CodeAtlas/Git/DeltaEngine.cs)
- Integrates `DbSeedingService`.
- If target DB has no index state, attempts to seed from a sibling worktree/branch DB.
- If seeded successfully, switches mode to `"seeded-delta"` and runs incremental git diff re-indexing from the seeded commit to HEAD.

---

## Verification Plan

### Automated Tests
- **Performance & Batching Benchmarks**:
  - Test `IndexOrchestrator` on a simulated multi-thousand file dataset and verify indexing completes in under 15 seconds.
  - Verify `SymbolLookupCache` correctly resolves symbol IDs without running SQLite SELECT statements.
- **Single-Pass Parser Tests**:
  - Verify `ParseFile` outputs identical symbols and references as separate `Parse` and `ParseReferences` calls across C#, TS, JS, HTML, and PS1.
- **Worktree Seeding & Delta Tests**:
  - Simulate creating a second git worktree at a slightly different commit.
  - Verify `DeltaEngine` seeds the DB from main worktree and indexes only changed files via `git diff`.

### Manual Verification
- Run `CodeAtlas index` on a large target solution repository (~5,000 files).
- Measure and report indexing duration (confirming > 15x throughput increase).
