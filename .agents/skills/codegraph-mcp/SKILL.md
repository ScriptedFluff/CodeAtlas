---
name: codegraph-mcp
description: Local code knowledge graph indexer & query engine providing fast symbol lookup, caller reference graph analysis, git blast-radius impact analysis, and ServiceStack cross-stack DTO contracts. Use when navigating code, tracing caller functions, searching symbols with wildcards, evaluating git change blast radius, or analyzing DTO endpoints.
---

# CodeGraphMcp Skill Guide

`CodeGraphMcp` is a local code knowledge graph and indexer engine that parses C#, TypeScript/JavaScript, HTML, and PowerShell codebases into a local SQLite database.

It operates both as a **CLI tool** and as an **MCP (Model Context Protocol) Server**, allowing AI assistants (Antigravity, GitHub Copilot, Claude Desktop, Cursor) to quickly query symbol definitions, callers, diff impact, and cross-stack contracts without scanning entire file trees.

---

## 1. When to Use CodeGraphMcp

Use `CodeGraphMcp` when:
- **Locating Symbol Definitions**: Finding exact implementation code, line range, and signatures for classes, methods, or interfaces.
- **Tracing Callers**: Finding all places in the codebase that invoke a specific method or constructor.
- **Assessing Blast Radius**: Analyzing modified or uncommitted files and counting affected symbols before performing refactors.
- **Cross-Stack Navigation**: Tracing a ServiceStack C# DTO to its corresponding TypeScript model and consuming Angular components.
- **Fast Wildcard Search**: Finding symbols using `%` SQL LIKE patterns across multiple languages.

---

## 2. MCP Server Tools Quick Reference

When connected via MCP (`serve`), AI agents should use the following tools:

### `get_symbol_definition`
Retrieves exact source code, file path, line numbers, signature, and doc comments. Supports SQL `LIKE` `%` wildcards. Includes **live uncommitted buffer** changes if files have unsaved edits.
```json
{
  "symbolName": "IndexOrchestrator",
  "symbolType": "Class",
  "filePath": "src/CodeGraphMcp"
}
```

### `find_symbol_callers`
Queries the `symbol_references` graph table to return caller function names, file paths, and line numbers.
```json
{
  "symbolName": "ExecuteQuery"
}
```

### `search_codebase_graph`
Searches symbol names using `%` wildcards with optional `symbolType` (`Class`, `Method`, `Interface`, `Property`, `Enum`, `Function`) and `language` (`csharp`, `typescript`, `html`, `powershell`) filters.
Supports `exactMatch: true` for exact symbol matching, `regex: "..."` for pattern filtering, and `limit: 20` (pass higher limit or 0 for expanded/unlimited up to 500 max cap).
Results are automatically sorted by structural relevance (exact matches & type definitions first). Automatically scopes to active project index. Returns precise `StartLine` and `EndLine` ranges for targeted file reading.
```json
{
  "pattern": "GetUser",
  "exactMatch": true,
  "language": "csharp"
}
```

### `get_supported_languages`
Discovers all file extensions and language parsers actively supported by the current CodeGraphMcp instance.
```json
{}
```

### `get_diff_impact`
Lists changed and dirty files along with affected symbol counts to gauge blast radius before refactoring.
```json
{
  "sinceCommit": "HEAD~1"
}
```

### `get_servicestack_dto_contract`
Looks up cross-stack mappings for ServiceStack endpoints: returns C# DTO class, TypeScript interface, and Angular component file locations.
```json
{
  "dtoName": "GetUserBillingRequest"
}
```

### `prune_indexed_worktrees`
Scans index cache directories for database files whose original repository folders or git worktrees no longer exist on disk, purging obsolete `.db` files and reclaiming disk space.
```json
{}
```

---

## 3. CLI Commands Quick Reference

For standalone terminal execution or scripting:

```powershell
# 1. Index repository incrementally (stores in .codegraph/code_graph.db)
dotnet run --project src/CodeGraphMcp -- index "C:\path\to\repo"

# 2. Query symbol by name with optional type, language, limit and skip filters
dotnet run --project src/CodeGraphMcp -- query "Morningstar" --repo "C:\path\to\repo" --lang json --type Property --limit 20 --skip 0

# 3. Check repository index status
dotnet run --project src/CodeGraphMcp -- status "C:\path\to\repo"

# 4. Prune stale worktrees & obsolete index database caches
dotnet run --project src/CodeGraphMcp -- prune

# 5. Continuous watcher for background re-indexing on git updates
dotnet run --project src/CodeGraphMcp -- watch "C:\path\to\repo" --interval 30

# 6. Clean index database
dotnet run --project src/CodeGraphMcp -- clean "C:\path\to\repo" --yes

# 7. Run MCP stdio server
dotnet run --project src/CodeGraphMcp -- serve -r "C:\path\to\repo"
```

---

## 4. Agent Best Practices

1. **Automatic Project Workspace Scoping**: Queries automatically scope to the active workspace project database index—no manual path filtering parameters are required.
2. **Prefer Symbol Definitions & Precise Line Ranges**: To save context budget, use `get_symbol_definition` or `search_codebase_graph` to obtain exact `StartLine` and `EndLine` numbers, performing targeted partial reads instead of reading entire files.
3. **Use Exact Match & Regex Filtering**: Use `exactMatch: true` on common names like `"Model"` to avoid context noise from matching `UserModel`/`ModelBase`. Use `regex` for custom string patterns.
4. **Check Diff Impact Before Refactoring**: Always run `get_diff_impact` to identify all modified/dirty files and total affected symbol counts before making structural code changes.
5. **Dynamic Language Discovery**: Call `get_supported_languages` on session start to verify whether `CodeGraphMcp` covers the target project's language stack.
6. **Live Dirty Buffer Awareness**: Tools automatically check disk for uncommitted file edits before querying the database, so unsaved work is always reflected in queries.
