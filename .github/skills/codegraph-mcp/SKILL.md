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
```json
{
  "pattern": "Get%User",
  "language": "csharp"
}
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

---

## 3. CLI Commands Quick Reference

For standalone terminal execution or scripting:

```powershell
# 1. Index repository incrementally (stores in .agent-index/codegraph.db)
dotnet run --project src/CodeGraphMcp -- index "C:\path\to\repo" --store-local

# 2. Query symbol by name
dotnet run --project src/CodeGraphMcp -- query "IndexOrchestrator" -r "C:\path\to\repo" --store-local

# 3. Check repository index status
dotnet run --project src/CodeGraphMcp -- status "C:\path\to\repo" --store-local

# 4. Continuous watcher for background re-indexing on git updates
dotnet run --project src/CodeGraphMcp -- watch "C:\path\to\repo" --store-local --interval 30

# 5. Clean index database
dotnet run --project src/CodeGraphMcp -- clean "C:\path\to\repo" --store-local --yes

# 6. Run MCP stdio server
dotnet run --project src/CodeGraphMcp -- serve -r "C:\path\to\repo" --store-local
```

---

## 4. Agent Best Practices

1. **Prefer `get_symbol_definition` Over Large File Reads**: To save context window budget, query symbol definitions directly instead of reading full 1000+ line source files.
2. **Check Diff Impact Before Refactoring**: Always run `get_diff_impact` to identify all modified/dirty files and total affected symbol counts before making structural code changes.
3. **Use Wildcards (`%`) for Partial Names**: When exact symbol names are unknown, use `search_codebase_graph` with `%` wildcards (e.g., `Db%Manager` or `%Handler`).
4. **Live Dirty Buffer Awareness**: Tools automatically check disk for uncommitted file edits before querying the database, so unsaved work is always reflected in queries.
