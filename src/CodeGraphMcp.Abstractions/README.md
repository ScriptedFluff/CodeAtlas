# CodeGraphMcp.Abstractions

`CodeGraphMcp.Abstractions` provides zero-dependency interfaces, attributes, and data models for authoring language parsers and AST indexers compatible with the [CodeGraphMcp](https://github.com/ScriptedFluff/CodeGraphMcp) indexing engine.

## Key Types

- **`ILanguageParser`**: The core parser interface implemented by plugin parsers (`Parse`, `ParseReferences`, `ParseFile`, `Language`, `FileExtensions`, `Metadata`).
- **`Symbol`**: Immutable record representing a code symbol definition (class, method, interface, property, etc.).
- **`SymbolReference`**: Record representing a cross-file or cross-symbol invocation / reference edge.
- **`SymbolType`**: Enum classifying symbols (`Class`, `Interface`, `Struct`, `Enum`, `Method`, `Property`, `Field`, `Function`, `TypeAlias`, `Variable`).
- **`ParserMetadata`**: Metadata record holding the parser ID, human-readable name, version, description, and author.
- **`ParserMetadataAttribute`**: Declarative attribute for marking parser implementations.

## How to Author a Custom Parser Plugin

1. Create a class library targeting `net10.0` or `net8.0`.
2. Add a package reference to `CodeGraphMcp.Abstractions`:
   ```xml
   <PackageReference Include="CodeGraphMcp.Abstractions" Version="1.0.0" />
   ```
3. Implement `ILanguageParser`:
   ```csharp
   using CodeGraphMcp.Models;
   using CodeGraphMcp.Parser;

   [ParserMetadata("python", "Python Parser", "1.0.0", Description = "Custom Python AST parser", Author = "Community")]
   public sealed class PythonParser : ILanguageParser
   {
       public IReadOnlyList<string> FileExtensions => [".py"];
       public string Language => "python";

       public IEnumerable<Symbol> Parse(string filePath, string source)
       {
           // Extract and yield Symbol definitions
           yield break;
       }

       public IEnumerable<SymbolReference> ParseReferences(string filePath, string source)
       {
           // Extract and yield cross-file references
           yield break;
       }
   }
   ```
4. Build the output DLL into your CodeGraphMcp `Parsers/Python/` directory. The engine will discover and load the parser via its isolated `AssemblyLoadContext`.
