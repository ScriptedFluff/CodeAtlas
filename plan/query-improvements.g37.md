# Feature Specification & Implementation Plan: Query Engine Improvements (`Primary Query Glob Transpilation`)

**Plan Version:** 1.0 (Model Tag: `g37`)  
**Status:** Proposed / Ready for Review  
**Target Projects:** `CodeGraphMcp.Core`, `CodeGraphMcp.Cli`, `CodeGraphMcp.Server`, `CodeGraphMcp.Tests`  
**Related Specifications:** [`plan/query-filtering.g37.md`](file:///d:/Projects/C%23/CodeGraphMcp/plan/query-filtering.g37.md), [`plan/query-filtering.md`](file:///d:/Projects/C%23/CodeGraphMcp/plan/query-filtering.md)

---

## 1. Executive Summary & Problem Statement

Currently, CodeGraph's primary search parameter across CLI (`codegraph query <symbol-name>`) and MCP (`search_codebase_graph(pattern: "...")`) defaults to an **inclusive substring search**:
```sql
WHERE (symbol_name = @exact COLLATE NOCASE OR symbol_name LIKE @pattern COLLATE NOCASE)
```
where `@pattern` is automatically wrapped in `%...%`.

While this behavior is convenient for simple name lookups (typing `User` matches `UserService`, `UserModel`, `GetUser`), it causes friction and performance limitations when users or AI agents provide intentional glob patterns (e.g., `*Dto`, `I[A-Z]*`, `Get*Request`, `User*`):
1. **Unwanted Substring Over-matching**: A query for `*Dto` or `User*` gets wrapped in `%...%`, matching `PreUserPostDtoExtra` rather than respecting the caller's explicit prefix or suffix boundaries.
2. **Missing Native Glob Power**: Syntax like `I[A-Z]*` or `(Request|Response)` in the primary argument requires using the secondary `--filter` flag instead of just typing the pattern directly.
3. **The Danger of In-Memory Post-Scanning for Primary Queries**: While `--filter` can safely fall back to chunked in-memory regex scanning when properly bounded by a primary search or cursor, allowing unbounded in-memory table scans on open primary queries would create unacceptable memory pressure and latency across large codebases.

### Core Architectural Goals
1. **Fast Zero-Allocation Mode Detection**: Classify the primary query string in $O(N)$ single-pass time between **Mode A: Pure Identifier** and **Mode B: Explicit Glob / Pattern**.
2. **Boundary-Respecting SQL Pushdown**: When a glob is detected, translate it directly to native SQLite `WHERE` clauses (`GLOB` / `LIKE`) that respect the user's exact boundary anchors without adding implicit `%...%` wrappers.
3. **Strict SQL-Only Pushdown for Primary Queries**: Disallow unbounded in-memory regex table scans on primary queries. If an unsupported regex construct (e.g., lookaround assertions) is passed as a primary query, fail fast with a helpful diagnostic error directing the user to `--filter`.
4. **Prefix Index Optimization**: Transparently optimize prefix globs (e.g., `Get*`, `User*`) to leverage SQLite B-tree index scans for sub-millisecond execution over hundreds of thousands of symbols.

---

## 2. Query Classification & Transpilation Specification

```mermaid
flowchart TD
    Input["Primary Query String: 'query'"] --> Detector["QueryPatternClassifier.Detect(query)"]
    
    Detector -->|Pure Identifier: 'UserService'| ModeA["Mode A: Substring Search<br/>WHERE symbol_name = @exact OR symbol_name LIKE '%UserService%'<br/>Relevance Ranking: Exact First"]
    
    Detector -->|Glob Pattern: 'I[A-Z]*' / '*Dto' / 'Get*Request'| ModeB["Mode B: Boundary-Respecting Transpilation<br/>Transpile to SQL GLOB / LIKE with strict user boundaries"]
    
    ModeB --> TranspileCheck{"Is Translatable to Pure SQL?"}
    TranspileCheck -->|Yes| PushdownSQL["Execute Direct SQLite WHERE (GLOB/LIKE)"]
    TranspileCheck -->|No (Complex Regex)| FailFast["Fail Fast: Error 'Primary query pattern must be SQL-translatable. Use --filter for complex regex.'"]
    
    ModeA --> MergeDirty["Merge with Dirty Buffer (Live Uncommitted Files)"]
    PushdownSQL --> MergeDirty
    MergeDirty --> Results["Emit Paginated Search Results"]
```

### 2.1. Detection & Classification Matrix

| Input Pattern | Classification | Target SQLite WHERE Clause | Boundary Semantics |
|---|---|---|---|
| `"UserService"` | **Mode A (Pure Identifier)** | `(symbol_name = 'UserService' COLLATE NOCASE OR symbol_name LIKE '%UserService%' COLLATE NOCASE)` | Substring match (implicit `%...%`) + exact match boost |
| `"*Controller"` | **Mode B (Suffix Glob)** | `symbol_name GLOB '*Controller'` | Suffix only (ends with `Controller`) |
| `"Get*"` | **Mode B (Prefix Glob)** | `symbol_name GLOB 'Get*'` / `symbol_name LIKE 'Get%' COLLATE NOCASE` | Prefix only (starts with `Get`) |
| `"Get*Request"` | **Mode B (Infix Glob)** | `symbol_name GLOB 'Get*Request'` | Exact prefix `Get` and suffix `Request` |
| `"^I[A-Z].*"` | **Mode B (Character Class)** | `symbol_name GLOB 'I[A-Z]*'` | Interface convention match (case-sensitive) |
| `"(Request\|Response)"` | **Mode B (Alternation)** | `(symbol_name LIKE '%Request%' OR symbol_name LIKE '%Response%')` | Multi-way SQL `OR` |
| `"(?=.*Service)"` | **Unsupported as Primary** | *None (Throws `ArgumentException`)* | Rejects unbounded scan; directs to `--filter` |

### 2.2. Mode A vs. Mode B Boundary Rules

* **Mode A (Pure Identifiers)**:
  - Valid identifier characters: `[A-Za-z0-9_]`, spaces, colons, hyphens.
  - Automatically wraps with `%...%` for inclusive discovery.
  - Employs relevance ordering: `ORDER BY CASE WHEN symbol_name = @exact COLLATE NOCASE THEN 0 ELSE 1 END, file_path, start_line`.
* **Mode B (Explicit Globs)**:
  - Triggered by: `*`, `?`, `[`, `]`, `|`, `^`, `$`, `(`, `)`.
  - **No implicit wrapping**: A pattern like `*Dto` matches `OrderDto` and `UserDto`, but does *not* match `OrderDtoHandler`.
  - A pattern like `Get*` matches `GetOrder` and `GetUser`, but does *not* match `AsyncGetOrder`.
  - If the user desires substring matching with globs, they explicitly write `*Dto*` or `*Get*`.

---

## 3. SQLite Index Acceleration & Optimization

SQLite cannot easily use a standard B-tree index for general substring `LIKE '%Foo%'` or leading wildcard `GLOB '*Foo'`. However, **Prefix Globs** (e.g. `Get*`, `User*`, `I*`) can utilize SQLite's B-Tree index on `symbols(symbol_name COLLATE NOCASE)`:

```sql
-- Prefix query transpiled for optimal B-Tree index seek:
SELECT id, symbol_name, symbol_type, class_name, language, file_path, start_line, end_line, doc_comment
FROM symbols
WHERE symbol_name GLOB 'Get*'
ORDER BY id ASC
LIMIT @limit;
```

When case-insensitivity is desired for ASCII prefix globs:
```sql
WHERE symbol_name >= 'GET' AND symbol_name < 'GEU'
```
or standard indexed `LIKE 'Get%' COLLATE NOCASE`.

---

## 4. Architectural Components & Code Layout

```
src/
├── CodeGraphMcp.Core/
│   ├── Filtering/
│   │   ├── QueryPatternClassifier.cs      [NEW] Fast $O(N)$ string scanner for identifier vs glob detection
│   │   ├── FilterTranspiler.cs            [MODIFY] Add TranspilePrimaryQuery method with boundary preservation
│   │   └── FilterTranspileResult.cs       [EXISTING] Tracks SQL pushdown and compiled matchers
│   ├── Db/
│   │   └── Queries/
│   │       └── SymbolQueries.cs           [MODIFY] Integrate automatic glob detection into FindByName
│   └── Git/
│       └── DirtyFileBuffer.cs             [MODIFY] Apply boundary-aware glob evaluation for dirty uncommitted symbols
├── CodeGraphMcp.Cli/
│   └── Commands/
│       └── QueryCommandHandler.cs         [MODIFY] Transparently route primary argument through QueryPatternClassifier
└── CodeGraphMcp.Server/
    └── Mcp/
        └── Tools/
            └── SearchCodebaseGraphTool.cs [MODIFY] Enable boundary-respecting globs in primary 'pattern' parameter
tests/
└── CodeGraphMcp.Tests/
    ├── QueryPatternClassifierTests.cs     [NEW] Unit tests for classifier detection and boundary preservation
    ├── PrimaryQueryGlobTests.cs           [NEW] Integration tests for FindByName with globs, prefixes, and suffixes
    └── SearchCodebaseGraphToolTests.cs    [MODIFY] Add test cases for primary glob pattern searching
```

---

## 5. Detailed Component Specifications

### 5.1. `QueryPatternClassifier` (`CodeGraphMcp.Core/Filtering/QueryPatternClassifier.cs`)

```csharp
namespace CodeGraphMcp.Filtering;

public enum QueryPatternKind
{
    PureIdentifier,
    GlobPattern,
    ComplexRegex
}

public static class QueryPatternClassifier
{
    public static QueryPatternKind Classify(string? input)
    {
        if (string.IsNullOrWhiteSpace(input)) return QueryPatternKind.PureIdentifier;

        bool hasGlobTokens = false;
        for (int i = 0; i < input.Length; i++)
        {
            char c = input[i];

            // Check for complex regex constructs that cannot be pushed down to SQL
            if (c == '\\' && i + 1 < input.Length)
            {
                char next = input[i + 1];
                if (next is 'b' or 'B' or 'd' or 'D' or 'w' or 'W' or 's' or 'S' or 'p' or 'P' or 'k' or '1' or '2' or '3' or '4' or '5' or '6' or '7' or '8' or '9')
                    return QueryPatternKind.ComplexRegex;
                i++;
                continue;
            }

            if (c == '(' && i + 1 < input.Length && input[i + 1] == '?')
                return QueryPatternKind.ComplexRegex;

            if (c is '+' or '{' or '}')
                return QueryPatternKind.ComplexRegex;

            if (c is '*' or '?' or '[' or ']' or '|' or '^' or '$')
                hasGlobTokens = true;
        }

        return hasGlobTokens ? QueryPatternKind.GlobPattern : QueryPatternKind.PureIdentifier;
    }
}
```

### 5.2. `FilterTranspiler.TranspilePrimaryQuery` (`CodeGraphMcp.Core/Filtering/FilterTranspiler.cs`)

Adds a dedicated transpilation path for primary queries that **enforces explicit boundaries**:
* Unlike `--filter` which may treat unanchored patterns with permissive matching, `TranspilePrimaryQuery` treats:
  - `*Dto` as strict suffix match (`symbol_name GLOB '*Dto'`).
  - `Get*` as strict prefix match (`symbol_name GLOB 'Get*'`).
  - `User*Dto` as strict infix match (`symbol_name GLOB 'User*Dto'`).
  - `(Request|Response)` as strict alternation.
* Throws `ArgumentException` with an actionable message if `QueryPatternKind.ComplexRegex` is detected.

### 5.3. `SymbolQueries.FindByName` & `FindWithFilter` Refactoring

In [`SymbolQueries.cs`](file:///d:/Projects/C%23/CodeGraphMcp/src/CodeGraphMcp.Core/Db/Queries/SymbolQueries.cs):
```csharp
public IReadOnlyList<Symbol> FindByName(
    string symbolName,
    int limit = 500,
    int offset = 0,
    string? language = null,
    SymbolType? symbolType = null)
{
    var kind = QueryPatternClassifier.Classify(symbolName);
    if (kind == QueryPatternKind.GlobPattern)
    {
        var filter = FilterTranspiler.TranspilePrimaryQuery(symbolName);
        var res = FindWithFilter(filter, cursor: 0, limit: limit, offset: offset, language: language, symbolType: symbolType);
        return res.Items;
    }

    // Otherwise, execute standard Mode A exact + substring query
    ...
}
```

---

## 6. Verification & Test Plan

### 6.1. Classifier Unit Tests (`QueryPatternClassifierTests.cs`)
1. **`Classify_PlainIdentifiers_ReturnsPureIdentifier`**:
   - `"User"`, `"UserService"`, `"my_variable"`, `"App123"` $\rightarrow$ `PureIdentifier`.
2. **`Classify_GlobsAndWildcards_ReturnsGlobPattern`**:
   - `"*Dto"`, `"Get*"`, `"I[A-Z]*"`, `"(Request|Response)"`, `"User*Model"` $\rightarrow$ `GlobPattern`.
3. **`Classify_ComplexRegex_ReturnsComplexRegex`**:
   - `"(?=.*Service)"`, `"\bWord\b"`, `"Item{1,3}"` $\rightarrow$ `ComplexRegex`.

### 6.2. Primary Query Glob Tests (`PrimaryQueryGlobTests.cs`)
1. **`FindByName_WithSuffixGlob_RespectsBoundaries`**:
   - Seed: `OrderDto`, `UserDto`, `OrderDtoHandler`, `CustomerDtoResponse`.
   - Query: `"*Dto"`.
   - Asserts: Matches `OrderDto`, `UserDto`. Does *not* match `OrderDtoHandler` or `CustomerDtoResponse`.
2. **`FindByName_WithPrefixGlob_RespectsBoundaries`**:
   - Seed: `GetOrder`, `GetUser`, `AsyncGetOrder`.
   - Query: `"Get*"`.
   - Asserts: Matches `GetOrder`, `GetUser`. Does *not* match `AsyncGetOrder`.
3. **`FindByName_WithInterfaceGlob_CaseSensitiveMatch`**:
   - Seed: `IUserService`, `ICustomerService`, `ItemService`, `iLegacyService`.
   - Query: `"I[A-Z]*"`.
   - Asserts: Matches `IUserService`, `ICustomerService`. Does *not* match `ItemService` or `iLegacyService`.
4. **`FindByName_WithComplexRegex_ThrowsArgumentException`**:
   - Query: `"(?=.*Service)"`.
   - Asserts: Throws `ArgumentException` with message mentioning `--filter`.

### 6.3. CLI & MCP End-to-End Tests
1. **CLI Positional Argument**:
   - Run `codegraph query "*Controller"` $\rightarrow$ outputs only symbols ending with `Controller`.
2. **MCP Tool Argument**:
   - Run `search_codebase_graph(pattern: "Get*Request")` $\rightarrow$ returns exact matches with zero post-scan overhead.

---

## 7. Implementation Checklist & Timeline

- [ ] **Phase 1: Query Pattern Classifier & Primary Transpiler**
  - Implement `QueryPatternClassifier` in `CodeGraphMcp.Core/Filtering/`.
  - Add `TranspilePrimaryQuery` method in `FilterTranspiler.cs`.
  - Add unit tests in `QueryPatternClassifierTests.cs`.
- [ ] **Phase 2: Database & Dirty Buffer Integration**
  - Update `SymbolQueries.FindByName` to seamlessly route globs through primary transpiler.
  - Update `DirtyFileBuffer.FindByName` to respect glob boundaries.
  - Add integration tests in `PrimaryQueryGlobTests.cs`.
- [ ] **Phase 3: CLI & MCP Server Verification**
  - Verify CLI positional arguments with globs (`codegraph query "*Dto"`).
  - Verify MCP `SearchCodebaseGraphTool` with globs.
  - Run full test suite (`dotnet test`).
