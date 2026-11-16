# CodeGraphMcp Recommendations & Performance Findings

**Date**: 13 August 2026  
**Target Project**: `C:\Work\Clever Services\master`  
**Author**: Antigravity Assistant  

---

## 1. Executive Summary

This document provides a comprehensive review of performance benchmarks, query pattern findings, and language indexer recommendations for `CodeGraphMcp`. 

During exploratory search and architecture planning across the `Clever Services` codebase (~80+ C# projects and microservices), `CodeGraphMcp` was evaluated for indexing efficiency, query latency, and search accuracy compared to standard file-scanning techniques (such as PowerShell `Get-ChildItem` / `Select-String` and ripgrep).

---

## 2. Indexing & Query Latency Benchmarks

### 2.1 Cold-Start Indexing vs Warm Query Performance
- **Cold-Start Multi-Worktree Indexing**: When `CodeGraphMcp` initialises indexing across a repository, it aggregates AST symbol data for all active worktrees into the central cache database (`.agent-index\code_graph.db`). For this core codebase, indexing across 3 concurrent worktrees (configured for rapid failover and instant recovery) generated a combined **159.5 MB** SQLite index file.
- **Warm Query Latency**: Once `.agent-index\code_graph.db` is built across the worktrees, symbol queries execute in **sub-100ms** (sub-second) response times across all worktree scopes.

### 2.2 Benchmark Comparison

| Method | Query Pattern | Execution Time | Output Type |
| :--- | :--- | :--- | :--- |
| **`CodeGraphMcp`** (`search_codebase_graph`) | `pattern: "User"` | **< 100 ms** | Structured AST Symbol Metadata (`startLine`, `endLine`, symbol type, doc comments) |
| **`CodeGraphMcp`** (`search_codebase_graph`) | `pattern: "MPS"` | **< 100 ms** | Structured AST Symbol Metadata (`startLine`, `endLine`, symbol type, doc comments) |
| **PowerShell `Get-ChildItem`** (`Select-String`) | `Pattern: "User"` | **1,762 ms** | Raw text lines |

> **Key Finding**: `CodeGraphMcp` is **over 17x faster** than disk-scanning utilities once cached, returning rich AST metadata rather than plain text snippets.

---

## 3. Query Pattern & Best Practice Findings

### 3.1 Substring Matching & Wildcard Elimination
- **Default Substring Matching**: Setting `exactMatch: false` (the default) automatically performs substring matching on plain query strings (e.g. `pattern: "MPS"` or `pattern: "User"`).
- **Wildcards (`%`)**: Explicit `%` wildcards are unnecessary for general keyword searches when `exactMatch: false` is active.

### 3.2 Result Set Optimization & Filtering
- **20-Match Limit**: `CodeGraphMcp` caps queries at 20 matches. For high-frequency terms like `"User"`, a broad query without filters can consume all 20 slots on properties or fields.
- **`symbolType` Filtering**: Supply `symbolType: "Class"` or `symbolType: "Interface"` to filter result sets and ensure target declarations are returned.
- **Line Range Targeting**: `CodeGraphMcp` outputs precise `startLine` and `endLine` boundaries. Agents can pass these exact ranges directly to `view_file`, avoiding unnecessary reads of 1,000+ line files.

### 3.3 Trialing Removal / Configuration of the 20-Result Cap
- **Trial Objective**: Evaluate removing or increasing the 20-result limit in `CodeGraphMcp` to provide complete visibility across microservice boundaries in a single query.
- **Key Advantages**:
  - **Single-Query Completeness**: Widespread domain interfaces, model types, or shared utilities across 80+ projects will return completely without arbitrary truncation.
  - **Fewer Agent Iterations**: Eliminates the need for follow-up narrowed searches when a target symbol falls outside the top 20 matches.
- **Recommended Implementation Design**:
  - **Optional `limit` Parameter**: Provide an optional `limit` parameter in `search_codebase_graph` (e.g. defaulting to `null` / unlimited or configurable by caller).
  - **Relevance-Based Ordering**: Sort results by structural significance (e.g. exact match first, then Classes & Interfaces, then Methods/Properties) so high-value symbol definitions appear first in the returned payload.

### 3.4 Multi-Tool Graph Navigation
Combine `CodeGraphMcp` tools sequentially for maximum efficiency:
1. `search_codebase_graph`: Discover symbol locations and definitions.
2. `get_symbol_definition`: Navigate directly to target type declarations across project references.
3. `find_symbol_callers`: Trace invocation points across solution boundaries.

---

## 4. Language Registry & Extension Recommendations

### 4.1 Current Supported Languages
As reported by `get_supported_languages`:
- `csharp` (`.cs`)
- `typescript` (`.ts`, `.tsx`)
- `html` (`.html`, `.htm`)
- `powershell` (`.ps1`, `.psm1`, `.psd1`)

### 4.2 Recommended Indexer Extensions

1. **JSON & YAML Support (`.json`, `.yml`, `.yaml`)**
   - **Rationale**: Microservices and cloud architectures rely heavily on configuration manifests (`appsettings.json`, `azure-pipelines.yml`, OpenAPI specs). Adding JSON and YAML to the parser registry will enable instant AST search for configuration keys and pipeline tasks.

2. **XML via HTML Tree Parser (`.xml`, `.csproj`, `.config`, `.vcxproj`)**
   - **Rationale**: HTML and XML share identical hierarchical element/attribute tree structures. Aliasing XML file extensions (`.xml`, `.csproj`, `.config`, `.vcxproj`, `.props`) to the existing HTML tree parser will allow `CodeGraphMcp` to index project references, NuGet dependencies, and XML documentation without requiring a separate standalone parser.

### 4.3 Stale Worktree & Index Cleanup Utility (`prune_indexed_worktrees`)
- **Rationale**: In multi-branch git workflows, temporary worktrees or deleted repository folders leave orphaned index records and large database files (`code_graph.db`) behind in the index registry.
- **Recommended Feature Design**:
  - **Prune Command / Tool**: Introduce a tool (e.g., `prune_indexed_worktrees` or `cleanup_index_cache`) that scans the registry of indexed paths, detects folders that no longer exist on disk, and purges their database records.
  - **Auto-Garbage Collection**: Perform an automatic lightweight validation check during MCP server startup to un-register missing worktree paths and free disk space.

---

## 5. Summary Matrix of Agent Usage Rules

```
                      ┌─────────────────────────────────────────┐
                      │          Target Asset Type              │
                      └────────────────────┬────────────────────┘
                                           │
                    ┌──────────────────────┴──────────────────────┐
                    ▼                                             ▼
          [Code & Symbols]                              [Non-Code / Config]
   (C#, TypeScript, HTML, PowerShell)            (.json, .yml, .bicep, .md, .xml)
                    │                                             │
                    ▼                                             ▼
         Use CodeGraphMcp                              Use Grep / PowerShell
  search_codebase_graph(pattern: "...")              grep_search / Get-ChildItem
```
