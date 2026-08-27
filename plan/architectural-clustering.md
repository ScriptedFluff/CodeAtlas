# Feature Specification: Configurable Architectural Clustering

## 1. Overview & Objective

Provide optional clustering capabilities to group symbols, references, and files into architectural domains. This enables AI coding agents to reason about codebases at multiple zoom levels (microservices, monolithic namespaces, or folder modules) without being overwhelmed by flat symbol graphs.

---

## 2. Clustering Strategies

| Strategy Flag | Target Repository Type | Clustering Mechanism | Example Boundaries |
|---|---|---|---|
| `--cluster-by=project` | Microservices / Multi-Project Solutions | Uses `.csproj`, `package.json`, `go.mod` boundaries. | `AuthService`, `BillingApi`, `SharedKernel` |
| `--cluster-by=namespace` | Large Monoliths | Groups by top-level or second-level namespace hierarchy. | `App.Domain.Billing`, `App.Infrastructure.Storage` |
| `--cluster-by=folder` | Monorepos / Feature Folders / Polyglot | Groups by directory path at a specified `--cluster-depth=N`. | `src/features/checkout`, `src/features/search` |
| `--cluster-by=auto` | Default | Automatically detects multi-project vs monolith structure. | Auto-selected |

---

## 3. Database Schema Extension

Add an indexed `cluster` column to the `symbols` table:

```sql
ALTER TABLE symbols ADD COLUMN cluster TEXT;
CREATE INDEX IF NOT EXISTS idx_symbols_cluster ON symbols(cluster);
```

---

## 4. MCP Agent Tools Enhancement

1. **`get_architecture_clusters` (New Tool)**: Returns a high-level summary of all clusters, their symbol counts, and key entry points.
2. **Query Scoping (`cluster?: string`)**: Add an optional `cluster` filter to `search_codebase_graph` and `find_symbol_callers`.
3. **Cross-Boundary Analysis**: Surfaces dependencies that cross cluster boundaries for blast-radius analysis during refactoring.