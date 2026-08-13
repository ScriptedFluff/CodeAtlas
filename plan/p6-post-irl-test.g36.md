# Implementation Plan: Post-IRL Test Enhancements (`p6-post-irl-test.g36.md`)

*Model Tag: `g36` (Gemini 3.6)*
*Date: 13 August 2026*

## Executive Overview
Based on real-world developer feedback and benchmarking detailed in `codegraphmcp.12082026 1.md`, `CodeGraphMcp` has proven to be the primary high-performance search tool (<300ms indexing, <50ms query latency, line-range precision).

This plan outlines the design and implementation steps for 6 specific enhancements to further optimize agent developer experience, reduce context noise, and expand indexing scope.

---

## Technical Stages & Item Breakdown

### Stage 1: Search Tool Enhancements (ExactMatch & Regex Filtering)
**Goal:** Reduce context noise and token overhead by allowing precise string matching and regex post-filtering directly within `search_codebase_graph`.

#### 1.1 `ExactMatch: boolean` Option
- **Target File:** [CodeGraphTools.cs](file:///d:/Projects/C%23/CodeGraphMcp/src/CodeGraphMcp/Mcp/CodeGraphTools.cs) & SQLite database repository layer ([SqliteSymbolIndex.cs](file:///d:/Projects/C%23/CodeGraphMcp/src/CodeGraphMcp/Db/SqliteSymbolIndex.cs)).
- **Changes:**
  - Update `search_codebase_graph` MCP tool signature to include an optional `exact_match` / `ExactMatch` boolean parameter (default `false`).
  - Modify SQL query in `SqliteSymbolIndex` to query `WHERE lower(name) = lower(@query)` or `WHERE name = @query` when `ExactMatch` is set to `true`, instead of `LIKE '%' || @query || '%'`.
  - Add unit tests verifying exact lookup for symbols like `Model` returns only `Model` without returning `UserModel`, `ModelBase`, etc.

#### 1.2 `RegexPattern: string` Filtering
- **Target File:** [CodeGraphTools.cs](file:///d:/Projects/C%23/CodeGraphMcp/src/CodeGraphMcp/Mcp/CodeGraphTools.cs) & [SymbolSearcher.cs](file:///d:/Projects/C%23/CodeGraphMcp/src/CodeGraphMcp/Commands/SymbolSearcher.cs).
- **Changes:**
  - Update `search_codebase_graph` parameter set to accept optional `regex` / `RegexPattern` string.
  - Apply standard `System.Text.RegularExpressions.Regex.IsMatch` filtering on the fetched results before returning JSON to the model context.
  - Gracefully catch invalid regex pattern syntax and return a friendly tool error response.

---

### Stage 2: Expanded File Whitelist & Metadata Indexing
**Goal:** Minimize the need to fall back to `grep` by indexing key configuration, manifest, and pipeline files.

- **Target Files:**
  - [LanguageDetector.cs](file:///d:/Projects/C%23/CodeGraphMcp/src/CodeGraphMcp/Parser/LanguageDetector.cs)
  - [TreeSitterParserManager.cs](file:///d:/Projects/C%23/CodeGraphMcp/src/CodeGraphMcp/Parser/TreeSitterParserManager.cs)
  - File scanner / indexer configuration files.
- **Changes:**
  - Add file extensions and exact file patterns to the whitelist: `.json`, `.yaml`, `.yml`, `package.json`, `appsettings.json`, `Docker-compose.yml`, pipeline configs (`.github/workflows/*.yml`).
  - Implement structured symbol/key extraction or tree-sitter / JSON key parsing for config entries so keys/sections are registered as symbols with line ranges.

---

### Stage 3: Dynamic Capabilities Endpoint (`get_supported_languages`)
**Goal:** Expose a metadata tool endpoint allowing AI agents to discover supported index languages dynamically upon session initialization.

- **Target File:** [CodeGraphTools.cs](file:///d:/Projects/C%23/CodeGraphMcp/src/CodeGraphMcp/Mcp/CodeGraphTools.cs).
- **Changes:**
  - Add new MCP tool `get_supported_languages`.
  - Return JSON listing active parsers and file extensions (e.g. `["C#", "TypeScript", "JavaScript", "HTML", "JSON", "YAML"]`).
  - Enables agents to route searches intelligently (using `CodeGraphMcp` for supported languages and fallback for others).

---

### Stage 4: Documentation & Schema Improvements
**Goal:** Instruct agentic planners on tool capabilities, project auto-scoping, and line-range usage.

#### 4.1 MCP Tool Descriptions
- Update descriptions in `CodeGraphTools.cs` and `mcp_config` schemas:
  - Document that search queries automatically scope to the active workspace project database index without requiring directory path parameters.
  - Explicitly emphasize that symbol definitions return precise `StartLine` and `EndLine` numbers, guiding models to execute scoped/partial file reads instead of loading entire source files.

#### 4.2 Repository & Customization Specs
- Update [MCP-CONFIGURE.md](file:///d:/Projects/C%23/CodeGraphMcp/MCP-CONFIGURE.md), [USAGE.md](file:///d:/Projects/C%23/CodeGraphMcp/USAGE.md), and [.agents/skills/codegraph-mcp/SKILL.md](file:///d:/Projects/C%23/CodeGraphMcp/.agents/skills/codegraph-mcp/SKILL.md) to reflect new parameters (`exact_match`, `regex`, `get_supported_languages`) and indexing strategies.

---

## Verification Plan

### Automated Tests
- Execute unit tests via `dotnet test tests/CodeGraphMcp.Tests/CodeGraphMcp.Tests.csproj`:
  - `ExactMatch` SQL query assertion tests.
  - `RegexPattern` filter matching tests.
  - `get_supported_languages` tool execution tests.
  - Metadata indexing tests for `.json` / `.yaml` files.

### Manual Verification
- Run `CodeGraphMcp` via CLI / MCP client and verify:
  1. `search_codebase_graph(query: "Model", exact_match: true)` returns only exact `Model` symbols.
  2. `get_supported_languages` returns full set of supported parsers.
  3. Querying symbols in `package.json` or `appsettings.json` returns accurate line numbers.
