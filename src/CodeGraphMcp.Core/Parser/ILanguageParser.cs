using CodeGraphMcp.Models;

namespace CodeGraphMcp.Parser;

public interface ILanguageParser
{
    /// <summary>Metadata describing the parser (ID, name, version, description, author).</summary>
    ParserMetadata Metadata => new(Language, $"{Language} Parser", "1.0.0");

    /// <summary>File extensions this parser handles, e.g. [".cs"]</summary>
    IReadOnlyList<string> FileExtensions { get; }

    /// <summary>Language identifier stored in the DB, e.g. "csharp"</summary>
    string Language { get; }

    /// <summary>
    /// Parse the source file and yield extracted symbols.
    /// Implementation MUST NOT throw — catch internally and yield nothing on failure.
    /// Set <see cref="Symbol.IsPartial"/> to true if the language/symbol supports multi-file declaration merging.
    /// </summary>
    IEnumerable<Symbol> Parse(string filePath, string source);

    /// <summary>
    /// Extract cross-symbol references (method calls, type usages, inheritance).
    /// Default returns empty — opt-in per language.
    /// </summary>
    IEnumerable<SymbolReference> ParseReferences(string filePath, string source) => [];

    /// <summary>
    /// Parse the source file and extract both symbols and references in a single pass.
    /// </summary>
    FileParseResult ParseFile(string filePath, string source) =>
        new(Parse(filePath, source).ToList(), ParseReferences(filePath, source).ToList());
}

public sealed record FileParseResult(
    IReadOnlyList<Symbol> Symbols,
    IReadOnlyList<SymbolReference> References);

