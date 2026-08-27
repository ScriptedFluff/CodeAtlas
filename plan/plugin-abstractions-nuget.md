# Feature Specification: Plugin Abstractions & NuGet Packaging (`CodeGraphMcp.Abstractions`)

## 1. Overview & Objective

Extract core parser interfaces, data models, and contracts into a standalone, zero-dependency library project: **`CodeGraphMcp.Abstractions`**. 

This allows third-party developers, internal teams, and community contributors to build custom language parsers and indexers without referencing heavy internal engine dependencies (SQLite, LibGit2Sharp, Tree-Sitter native bindings, MCP transport layers). The abstractions library can then be built and published as a standalone NuGet package.

---

## 2. Solution Structure & Architecture

```mermaid
flowchart TD
    NuGet["📦 CodeGraphMcp.Abstractions (NuGet Package)<br><i>Targets netstandard2.0 / net8.0 (Pure BCL)</i>"]
    
    NuGet --> P1["CodeGraphMcp.Parser.CSharp (Built-in)"]
    NuGet --> P2["CodeGraphMcp.Parser.TypeScript (Built-in)"]
    NuGet --> P3["Custom Python / Rust Community Parsers"]
    
    P1 --> Engine["CodeGraphMcp.Core (Engine)"]
    P2 --> Engine
    P3 -.->|Drop-in Plugin| Engine
    
    Engine --> CLI["CodeGraphMcp (CLI & Server)"]
```

---

## 3. Types Relocated to `CodeGraphMcp.Abstractions`

| Type | Target Namespace | Purpose |
|---|---|---|
| **`ILanguageParser`** | `CodeGraphMcp.Abstractions` | Core parser interface (`Parse`, `ParseReferences`, `SupportedExtensions`, `LanguageId`). |
| **`Symbol`** | `CodeGraphMcp.Abstractions` | Immutable symbol definition record. |
| **`SymbolReference`** | `CodeGraphMcp.Abstractions` | Cross-file invocation/reference edge record. |
| **`SymbolType`** | `CodeGraphMcp.Abstractions` | Enum for symbol classifications (Class, Method, Property, Interface, etc.). |
| **`ParserMetadataAttribute`** | `CodeGraphMcp.Abstractions` | Declarative metadata attribute for automatic parser discovery. |

---

## 4. Project Configuration (`CodeGraphMcp.Abstractions.csproj`)

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFrameworks>net8.0;netstandard2.0</TargetFrameworks>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    
    <!-- NuGet Metadata -->
    <PackageId>CodeGraphMcp.Abstractions</PackageId>
    <Version>1.0.0</Version>
    <Authors>ScriptedFluff</Authors>
    <Description>Pure interfaces and contract models for extending the CodeGraphMcp indexing engine.</Description>
    <PackageLicenseExpression>MIT</PackageLicenseExpression>
    <RepositoryUrl>https://github.com/scriptedfluff/CodeGraphMcp</RepositoryUrl>
    <GeneratePackageOnBuild>true</GeneratePackageOnBuild>
  </PropertyGroup>
</Project>
```

---

## 5. Benefits
- **Zero-Dependency SDK**: Parser developers only need basic .NET types.
- **Contract Stability**: Isolates public plugin APIs from engine refactoring or database schema updates.
- **NuGet Distribution Ready**: Easily consumed by any .NET project via NuGet package manager.