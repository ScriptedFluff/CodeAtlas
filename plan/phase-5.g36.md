# Phase 5 Implementation Plan: Other Thoughts & Notes

Phase 5 addresses foundational performance, accuracy, and extensibility improvements identified during codebase analysis. This plan outlines the technical design, priority sequence, file modifications, and verification steps for implementing `.gitignore` support, C# multi-file partial class handling, JavaScript language support, and a modular parser architecture.

---

## Technical Overview & Strategic Sequencing

Based on system impact and integration fit with Phase 4 (Reference Graph, File Watcher, CLI Utilities), the recommended execution sequence is:

1. 🏆 **Phase 5-1: `.gitignore` Integration** (Top Priority)
   - *Rationale*: Integrates directly with `IndexOrchestrator` file scanning and `FileWatcher`. Prevents `bin/`, `obj/`, `node_modules/`, and temporary build artifacts from polluting the SQLite database or generating superfluous watcher events.
2. 🥈 **Phase 5-2: Multi-File Symbol Declaration Merging** (High Priority)
   - *Rationale*: Enhances symbol resolution accuracy in `REF-01` (Reference Graph). Handles symbols split across multiple files in a language-agnostic way (e.g., C# `partial` classes/interfaces, TypeScript interface/namespace declaration merging, module augmentations). Resolving references and definitions across merged declarations ensures exact caller lookups and complete symbol linking.
3. 🥉 **Phase 5-3: JavaScript Support** (Medium Priority)
   - *Rationale*: Extends parsing coverage to `.js`, `.jsx`, `.mjs`, `.cjs` files by leveraging existing Tree-Sitter TS/JS AST structures alongside `TypeScriptParser`.
4. 📦 **Phase 5-4: Modular / Drop-in Parser Architecture** (Future / Refactoring)
   - *Rationale*: Stabilizes parser registration via a `ParserRegistry` decoupler, transitioning from hardcoded instantiation arrays to extensible plugin-ready registration.

---

## User Review Required

> [!IMPORTANT]
> **.gitignore Parser Dependency Choice**: We can either adopt `Ignore` (a standard .NET library for `.gitignore` glob parsing) or implement a robust glob matcher using `Microsoft.Extensions.FileSystemGlobbing`. `Ignore` is standard for `.gitignore` specs including negations (`!`) and nested ignore files.

> [!NOTE]
> **Multi-File Declaration Merging in Database**: Declaration-merged symbols (e.g. C# partial types, TypeScript interface/namespace merging) share the exact same fully-qualified container scope across multiple file paths. We will update `SymbolDb` / `SymbolsTable` so that a symbol aggregates all declaration locations and member definitions across files during index build and reference resolution.

---

## Open Questions

> [!IMPORTANT]
> 1. Should nested `.gitignore` files (e.g., `src/subfolder/.gitignore`) be respected hierarchically alongside the repository root `.gitignore`? *(Recommended: Yes, standard git behavior)*
> 2. For JavaScript support, should `.jsx` files be parsed using the TSX/JSX Tree-Sitter grammar or standard JS grammar? *(Recommended: Use TSX/JSX parser to support React JSX seamlessly)*

---

## Proposed Changes

---

### Component 1: `.gitignore` Integration (`GitIgnoreMatcher` & `IndexOrchestrator`)

#### [NEW] [GitIgnoreMatcher.cs](file:///d:/Projects/C%23/CodeGraphMcp/src/CodeGraphMcp/Git/GitIgnoreMatcher.cs)
- Implement `.gitignore` parser supporting standard gitignore pattern rules (`*`, `**`, `?`, `/`, `!`).
- Support loading root `.gitignore` as well as nested `.gitignore` files found during directory traversal.
- Expose `bool IsIgnored(string relativeOrAbsolutePath)` method.

#### [MODIFY] [IndexOrchestrator.cs](file:///d:/Projects/C%23/CodeGraphMcp/src/CodeGraphMcp/Indexer/IndexOrchestrator.cs)
- Replace static `IgnoredDirs` check in `EnumerateFiles` with dynamic `GitIgnoreMatcher`.
- Ensure ignored directories (such as `bin`, `obj`, `node_modules`, `.git`, `.agent-index`) are pruned early during directory recursion.

#### [MODIFY] [FileWatcher.cs](file:///d:/Projects/C%23/CodeGraphMcp/src/CodeGraphMcp/Indexer/FileWatcher.cs)
- Check `GitIgnoreMatcher.IsIgnored(path)` before enqueuing file system change notifications to eliminate noise.

---

### Component 2: Language-Agnostic Multi-File Symbol Declaration Merging

#### [MODIFY] [ILanguageParser.cs](file:///d:/Projects/C%23/CodeGraphMcp/src/CodeGraphMcp/Parser/ILanguageParser.cs) / Symbol Metadata
- Introduce language-agnostic metadata on extracted `Symbol`s indicating declaration traits (e.g. `IsPartial` / `IsDeclarationMergeable`).

#### [MODIFY] [CSharpParser.cs](file:///d:/Projects/C%23/CodeGraphMcp/src/CodeGraphMcp/Parser/CSharpParser.cs) & [TypeScriptParser.cs](file:///d:/Projects/C%23/CodeGraphMcp/src/CodeGraphMcp/Parser/TypeScriptParser.cs)
- C#: Detect `partial` modifier on `class`, `struct`, `interface`, and `record` declarations across multiple files.
- TypeScript / JS: Detect multi-file declaration merging constructs (e.g. `interface`, `namespace`, type augmentation across files).

#### [MODIFY] [SymbolDb.cs](file:///d:/Projects/C%23/CodeGraphMcp/src/CodeGraphMcp/Db/SymbolDb.cs)
- Generalize database storage and lookup logic (`UpsertFile`, `ResolveId`, `GetPrimarySymbolIdForFile`) so multi-file declarations with identical fully-qualified names aggregate member definitions and locations across files without overwriting each other.
- Support multi-location resolution so reference graph lookups resolve caller/callee definitions across all merged declaration sites.

---

### Component 3: JavaScript Support (`JavaScriptParser`)

#### [NEW] [JavaScriptParser.cs](file:///d:/Projects/C%23/CodeGraphMcp/src/CodeGraphMcp/Parser/JavaScriptParser.cs)
- Implement `ILanguageParser` handling `.js`, `.jsx`, `.mjs`, `.cjs`.
- Share/reuse Tree-Sitter parsing logic with `TypeScriptParser` for AST node extraction (functions, classes, exports, imports, calls).

#### [MODIFY] [IndexOrchestrator.cs](file:///d:/Projects/C%23/CodeGraphMcp/src/CodeGraphMcp/Indexer/IndexOrchestrator.cs)
- Register `JavaScriptParser` in `BuildExtensionMap()`.

---

### Component 4: Modular / Drop-in Parser Architecture

#### [NEW] [ParserRegistry.cs](file:///d:/Projects/C%23/CodeGraphMcp/src/CodeGraphMcp/Parser/ParserRegistry.cs)
- Create dynamic registry `IParserRegistry` to register, resolve, and list installed `ILanguageParser` instances.
- Support plugin/loader extensibility by decoupling language parser registration from hardcoded `IndexOrchestrator` arrays.

#### [MODIFY] [ILanguageParser.cs](file:///d:/Projects/C%23/CodeGraphMcp/src/CodeGraphMcp/Parser/ILanguageParser.cs)
- Add parser metadata properties (e.g., `DisplayName`, `Version`, `Capabilities`) to facilitate modular discovery.

---

## Verification Plan

### Automated Tests
- **`.gitignore` Unit & Integration Tests**:
  - Test pattern matching for root `.gitignore`, wildcard paths (`*.user`), directory ignores (`bin/`, `obj/`), and nested ignore files.
  - Verify `IndexOrchestrator` skips ignored files when indexing test projects.
- **Multi-File Symbol Declaration Merging Tests**:
  - Parse multiple files containing merged declarations (e.g., C# `partial` classes `PartialTest.cs` and `PartialTest.Other.cs`, or TypeScript `interface` / `namespace` merging across files).
  - Verify references to members declared in either file correctly resolve to the unified symbol scope and link all declaration sites.
- **JavaScript Parser Tests**:
  - Test parsing `.js` and `.jsx` sample files. Verify function declarations, ES6 modules, CommonJS `require`, and method invocations are indexed and linked.
- **Parser Registry Tests**:
  - Test dynamically registering and retrieving parsers by file extension.

### Manual Verification
- Run `dotnet test` across the full solution.
- Run `CodeGraphMcp` indexer on a sample repository containing `.gitignore`, C# partial classes, and JS files, verifying database output with `sqlite3` CLI or diagnostic queries.
