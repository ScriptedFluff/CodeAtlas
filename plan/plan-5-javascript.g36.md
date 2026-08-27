# Phase 5-3 Implementation Plan: JavaScript Support

Phase 5-3 extends CodeAtlas's multi-language parsing capability by introducing full JavaScript support across standard JS (`.js`), React JSX (`.jsx`), ES Modules (`.mjs`), and CommonJS (`.cjs`) source files.

---

## Strategic Objectives & Language Coverage

1. **Supported File Extensions**: `.js`, `.jsx`, `.mjs`, `.cjs`
2. **Database Language Identifier**: `"javascript"`
3. **AST Parser Engine**: Tree-Sitter (`tree-sitter-typescript` / TSX grammar, which provides seamless parsing for standard JS, ES6+, and React JSX)
4. **Symbol & Reference Scope**:
   - Class declarations, constructor methods, and class methods
   - Function declarations, generator functions, and top-level arrow functions (`const foo = () => ...`)
   - Import statements (`import { x } from './mod'`) and CommonJS `require(...)` calls
   - Invocation expressions (`foo()`, `obj.method()`) and constructor instantiations (`new MyClass()`)

---

## User Review Required

> [!NOTE]
> **Tree-Sitter Grammar Reuse**: JavaScript syntax is a strict subset of TypeScript and TSX. Utilizing the `tree-sitter-typescript` / TSX grammar for JavaScript enables unified parsing for both `.js` and `.jsx` (React components) without needing separate native binaries.

> [!IMPORTANT]
> **Top-Level Arrow Function Extraction**: In idiomatic modern JavaScript/React, functions are frequently declared using `const MyComponent = () => { ... }` or `export const helper = function() { ... }`. The `JavaScriptParser` will extract `variable_declarator` nodes whose values are `arrow_function` or `function_expression` as `SymbolType.Function`.

---

## Proposed Changes

### Component 1: `JavaScriptParser` Implementation

#### [NEW] [JavaScriptParser.cs](file:///d:/Projects/C%23/CodeAtlas/src/CodeAtlas/Parser/JavaScriptParser.cs)
- Implement `ILanguageParser` interface for JavaScript.
- Define `FileExtensions => [".js", ".jsx", ".mjs", ".cjs"]`.
- Define `Language => "javascript"`.
- Implement `Parse(string filePath, string source)`:
  - Construct Tree-Sitter parser with TS/TSX grammar.
  - Query AST for:
    - `(class_declaration name: (identifier) @class.name) @class.def`
    - `(function_declaration name: (identifier) @function.name) @function.def`
    - `(generator_function_declaration name: (identifier) @function.name) @function.def`
    - `(method_definition name: (property_identifier) @method.name) @method.def`
    - `(variable_declarator name: (identifier) @function.name value: (arrow_function)) @function.def`
    - `(variable_declarator name: (identifier) @function.name value: (function_expression)) @function.def`
  - Yield extracted `Symbol` instances with accurate byte/line ranges and doc comments (JSDoc `/** ... */`).
- Implement `ParseReferences(string filePath, string source)`:
  - Traverse AST nodes recursively for:
    - `call_expression`: Detect regular calls (`func()`), member calls (`object.method()`), and CommonJS `require('module')` imports.
    - `new_expression`: Detect object instantiations (`new Service()`).
    - `import_statement`: Extract ES6 module import specifiers (`import { target } from 'source'`).

---

### Component 2: Indexer Orchestration Integration

#### [MODIFY] [IndexOrchestrator.cs](file:///d:/Projects/C%23/CodeAtlas/src/CodeAtlas/Indexer/IndexOrchestrator.cs)
- Add `new JavaScriptParser()` to `BuildExtensionMap()`.
- Automatically route `.js`, `.jsx`, `.mjs`, and `.cjs` files through `JavaScriptParser`.

---

### Component 3: Test Suite

#### [NEW] [JavaScriptParserTests.cs](file:///d:/Projects/C%23/CodeAtlas/tests/CodeAtlas.Tests/JavaScriptParserTests.cs)
- Unit tests covering:
  1. **Standard Function & Class Parsing**: Verifies parsing of classes, methods, standard functions, and arrow functions in `.js` files.
  2. **React JSX Component Parsing**: Verifies parsing of JSX elements and functional components in `.jsx` files.
  3. **Module Import & Require Extraction**: Verifies `import` and `require()` calls are recorded as `Import` references.
  4. **Call & New References**: Verifies method calls and constructor invocations generate correct `SymbolReference` records.
  5. **Error Resilience**: Verifies syntax errors or invalid JS strings do not throw exceptions.

---

## Verification Plan

### Automated Tests
- Run unit tests via dotnet CLI:
  ```powershell
  dotnet test tests/CodeAtlas.Tests/CodeAtlas.Tests.csproj --filter "FullyQualifiedName~JavaScriptParserTests"
  ```
- Run full unit test suite:
  ```powershell
  dotnet test
  ```

### Manual Verification
- Index a repository containing JavaScript & React JSX files using `CodeAtlas`.
- Verify database entries in `symbols` and `symbol_references` tables for JavaScript symbols.
