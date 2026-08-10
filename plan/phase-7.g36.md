# Phase 7 Implementation Plan: Advanced Graph, Multi-Language & Infrastructure

Phase 7 addresses performance, multi-language coverage, symbol resolution accuracy, and modularity enhancements.

---

## 1. 🏆 .gitignore Support & Filtering
- **Goal**: Implement standard `.gitignore` pattern matching supporting root and nested `.gitignore` files.
- **Integration**: Filter files in `IndexOrchestrator` scanning, `FileWatcher`, and `DeltaEngine` to skip `bin/`, `obj/`, `node_modules/`, `.git/`, `.agent-index/`, and user-ignored build artifacts.

## 2. 🥈 Language-Agnostic Multi-File Symbol Declaration Merging
- **Goal**: Support symbols split across multiple files in a language-agnostic way (e.g., C# `partial` types, TypeScript interface/namespace merging, module augmentations).
- **Database & Graph**: Update `SymbolDb` and reference resolution logic so multi-file declarations aggregate member definitions and declaration sites without overwriting each other in the reference graph.

## 3. 🥉 JavaScript & JSX Support
- **Goal**: Extend language parsing to `.js`, `.jsx`, `.mjs`, and `.cjs` files.
- **Implementation**: Reuse/adapt Tree-Sitter AST parsing from `TypeScriptParser` for JS/JSX function declarations, ES6 modules, CommonJS `require`, and method invocations.

## 4. 📦 Modular / Drop-in Parser Architecture
- **Goal**: Introduce a `ParserRegistry` to dynamically register and discover `ILanguageParser` implementations.
- **Future Readiness**: Decouples language parsers from hardcoded orchestrator arrays, enabling an extensible plugin/loader architecture.
