# CodeGraphMcp Potential Features Roadmap

This document tracks candidate features and enhancements for `CodeGraphMcp` derived from benchmark findings and architecture recommendations.

---

## Feature Backlog

### 1. Result Cap Adjustment & Relevance Ordering
- **Status**: **Completed**
- **Description**: Add configurable `limit` parameter to `search_codebase_graph` and implement structural relevance sorting (exact match > types > methods > properties).

### 2. JSON & YAML AST Support (`.json`, `.yml`, `.yaml`)
- **Status**: **Completed**
- **Description**: Extend language indexer registry to parse JSON and YAML config files (`appsettings.json`, pipeline YAMLs, OpenAPI specs) for instant key/symbol lookups.

### 3. XML via HTML Tree Parser (`.xml`, `.csproj`, `.config`, `.vcxproj`)
- **Status**: **Completed**
- **Description**: Alias XML file extensions to the existing HTML tree parser to index project dependencies, NuGet references, and XML doc elements without requiring a new parser.

### 4. Stale Worktree & Index Cleanup Utility (`prune_indexed_worktrees`)
- **Status**: **Completed**
- **Description**: Introduce a `prune_indexed_worktrees` / `cleanup_index_cache` tool and startup auto-garbage collector to purge un-registered or missing git worktree paths from `code_graph.db` and reclaim disk space.

### 5. Global Registry Awareness, Index Linking & Layered Delta Storage
- **Status**: Proposed (Requires R&D)
- **Description**:
  - **Global Awareness of Local (`--store-local`) Indexes**: Maintain a lightweight global registry so global management tools (`prune`, `status`) stay aware of all `.agent-index/code_graph.db` database instances created across repository worktrees.
  - **Identical Commit Deduplication / Index Linking**: When indexing a new worktree sharing the exact same `HEAD` commit hash as an existing index, avoid immediately duplicating the SQLite database. Link to the existing base index until worktrees diverge.
  - **Layered Delta Storage (Immutable Base + Local Overlay)**: When worktrees built from a shared base commit diverge, keep the common base index immutable and record only delta diffs/new symbols in the worktree's local DB (or lazily supersede the base index).
  - **R&D / Speed Requirement**: Requires benchmark evaluation (e.g. SQLite `ATTACH DATABASE` or overlay views) to guarantee multi-database overlay queries do not degrade warm query performance below the sub-100ms SLA.

### 6. CSS Selector Path Hierarchy & Context Indexing
- **Status**: Proposed
- **Description**: Extend `CssParser.cs` to index media query contexts (`@media`), `@keyframes` animation targets, and BEM modifier/nested selector paths (e.g., `btn.primary` or `@keyframes.fadeIn`) alongside flat class and ID symbols.

### 7. Search Result Pagination Metadata (Total Hit Count vs. HasMore Flag)
- **Status**: Proposed / Under Evaluation (Low Priority)
- **Description**: Evaluate returning pagination metadata (`totalMatches` or `hasMore`) in `SearchResult` payloads to indicate if additional pages exist beyond the requested `limit`/`offset`.
- **Potential Performance & Token Cost**:
  - **Full Count (`COUNT(*)`) Cost**: Running a full `SELECT COUNT(*)` on broad wildcard queries (`pattern: "%"` or `pattern: "User"` across 80+ projects) forces SQLite to scan every matching record in the database instead of stopping early at `LIMIT`, turning sub-10ms queries into 50–150ms full-index scans.
  - **Token Overhead**: Adding verbose metadata to every MCP response increases JSON context payload size for LLM context windows.
- **Proposed `hasMore` Workaround (Fetch `limit + 1`)**:
  - **Implementation Strategy**: Instead of executing a separate `COUNT(*)` query, request `limit + 1` records from SQLite (`LIMIT limit + 1 OFFSET offset`).
  - **Logic**:
    1. If `results.Count > limit`, drop the `(limit + 1)`-th item and set `hasMore: true` on the returned payload record (`SearchResult(pattern, matches, hasMore: true)`).
    2. If `results.Count <= limit`, set `hasMore: false`.
  - **Advantages**:
    - **Zero Performance Overhead**: SQLite stops reading records immediately at `limit + 1`.
    - **Minimal Context Cost**: Adds only a single boolean field (`hasMore: true/false`) to the response payload.
- **Current Client Workaround**: If `hasMore` is omitted entirely, clients rely on receiving fewer matches than `limit` (or an empty array `[]`) to determine when the final page of search results has been reached.

