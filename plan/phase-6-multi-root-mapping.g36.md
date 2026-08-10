# Phase 6 Detailed Plan: Multi-Project & Solution Root Mapping

This plan details the implementation of **Multi-Project & Solution Root Mapping** as part of Phase 6 (Multi-Project Solution Support & Unbound MCP Server).

It defines how `CodeGraphMcp` identifies workspace roots, enumerates multi-project solutions (such as C# `.sln`/`.slnx` roots or TypeScript/JavaScript monorepos), normalizes file paths consistently across operating systems, and indexes cross-project symbol relationships.

---

## Technical Overview & Strategic Sequencing

1. 🔍 **Goal 1: Solution & Workspace Root Resolution (`SolutionRootResolver`)**
   - Provide auto-discovery of solution and repo roots when a target directory points to a subproject folder or deep nested directory.
   - Detect workspace boundary indicators (`.sln`, `.slnx`, `.git`, `package.json`, `.agent-index`).
   - Ensure the primary database and relative file indexing anchor at the top-level solution root so relative paths remain uniform across all subprojects.

2. 📁 **Goal 2: File Enumeration & Path Normalization (`WorkspacePathNormalizer`)**
   - Ensure file paths stored in `Symbols` and `References` tables are relative to the resolved solution root.
   - Enforce forward-slash `/` path separators consistently across Windows, Linux, and macOS to prevent OS-dependent cache mismatches.

3. 🏗️ **Goal 3: Multi-Project Structure & Project Boundary Mapping**
   - Discover `.csproj` / `tsconfig.json` / `package.json` boundaries during indexing.
   - Associate source files with their enclosing project modules so cross-project dependency graphs and symbol lookups accurately reflect solution structure.

4. 🔗 **Goal 4: Cross-Project Symbol & Reference Resolution**
   - Resolve symbol references spanning multiple `.csproj` projects within the same solution (e.g., core domain library to API host project).
   - Ensure `FindSymbolCallers` and `GetDiffImpact` work seamlessly across project boundaries within the solution graph.

5. 🧪 **Goal 5: Automated Testing & Multi-Project Verification**
   - Create multi-project test fixtures (solution root with 2+ `.csproj` subfolders).
   - Test solution root discovery, relative path normalization, and cross-project symbol caller lookups.

---

## Proposed Changes

### Component 1: Solution Root Discovery

#### [NEW] [SolutionRootResolver.cs](file:///d:/Projects/C%23/CodeGraphMcp/src/CodeGraphMcp/Indexer/SolutionRootResolver.cs)
- Scans upward from a target directory to find the highest-level workspace root containing `.sln`, `.slnx`, `.git`, or `.agent-index`.
- Exposes `string ResolveRoot(string targetDirectory)` returning the normalized canonical root path.

---

### Component 2: Path Normalization & Multi-Project Indexer Updates

#### [NEW] [WorkspacePathNormalizer.cs](file:///d:/Projects/C%23/CodeGraphMcp/src/CodeGraphMcp/Indexer/WorkspacePathNormalizer.cs)
- Standardizes paths to absolute normalized format or relative forward-slash format (`/`).
- Resolves relative paths relative to `SolutionRootResolver.ResolveRoot(targetDirectory)`.

#### [MODIFY] [IndexOrchestrator.cs](file:///d:/Projects/C%23/CodeGraphMcp/src/CodeGraphMcp/Indexer/IndexOrchestrator.cs)
- Update `IndexAsync(string targetDirectory)` to resolve the true solution root via `SolutionRootResolver`.
- Normalize all enumerated file paths to forward-slash relative paths anchored at the solution root.
- Detect project manifest files (`.csproj`, `tsconfig.json`) to tag file entries with project context.

---

### Component 3: Database & Reference Resolution Refinements

#### [MODIFY] [ReferenceQueryRepository.cs](file:///d:/Projects/C%23/CodeGraphMcp/src/CodeGraphMcp/Db/Queries/ReferenceQueryRepository.cs)
- Update reference lookups to query across project boundaries within the solution graph.
- Support relative path resolution when navigating symbol callers across project subdirectories.

---

## Verification Plan

### Automated Tests

1. **Solution Root Resolution Tests**:
   - Verify `SolutionRootResolver` correctly identifies the solution root when given a subfolder path containing a `.csproj` file.
   - Test fallback to target directory when no solution file or `.git` root exists.

2. **Path Normalization Tests**:
   - Verify Windows backslash paths (`src\ProjectA\Class1.cs`) normalize to Unix-style forward slashes (`src/ProjectA/Class1.cs`).

3. **Multi-Project Indexing & Caller Resolution Tests**:
   - Create a temporary multi-project solution structure (`Root/ProjectA/Service.cs` and `Root/ProjectB/Client.cs`).
   - Run `IndexOrchestrator.IndexAsync` on `Root/ProjectB`.
   - Verify both projects are indexed under `Root` and caller references between `Client` and `Service` resolve correctly.

### Manual Verification
- Test `CodeGraphMcp` against a multi-project C# solution directory by invoking tool commands pointing to nested subdirectories, verifying that symbol graphs properly span across solution projects.
