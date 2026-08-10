using CodeGraphMcp.Models;

namespace CodeGraphMcp.Parser;

public interface ILanguageParser
{
    /// <summary>File extensions this parser handles, e.g. [".cs"]</summary>
    IReadOnlyList<string> FileExtensions { get; }

    /// <summary>Language identifier stored in the DB, e.g. "csharp"</summary>
    string Language { get; }

    /// <summary>
    /// Parse the source file and yield extracted symbols.
    /// Implementation MUST NOT throw — catch internally and yield nothing on failure.
    /// </summary>
    IEnumerable<Symbol> Parse(string filePath, string source);

    /// <summary>
    /// Extract cross-symbol references (method calls, type usages, inheritance).
    /// Default returns empty — opt-in per language.
    /// </summary>
    IEnumerable<SymbolReference> ParseReferences(string filePath, string source) => [];
}
