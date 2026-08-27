using System.Reflection;
using CodeAtlas.Models;

namespace CodeAtlas.Parser;

public interface ILanguageParser
{
    /// <summary>Metadata describing the parser (ID, name, version, description, author).</summary>
#if NETSTANDARD2_0
    ParserMetadata Metadata { get; }
#else
    ParserMetadata Metadata =>
        GetType().GetCustomAttribute<ParserMetadataAttribute>()?.ToMetadata()
        ?? new ParserMetadata(Language, $"{Language} Parser", "1.0.0");
#endif

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
#if NETSTANDARD2_0
    IEnumerable<SymbolReference> ParseReferences(string filePath, string source);
#else
    IEnumerable<SymbolReference> ParseReferences(string filePath, string source) => [];
#endif

    /// <summary>
    /// Parse the source file and extract both symbols and references in a single pass.
    /// </summary>
#if NETSTANDARD2_0
    FileParseResult ParseFile(string filePath, string source);
#else
    FileParseResult ParseFile(string filePath, string source) =>
        new(Parse(filePath, source).ToList(), ParseReferences(filePath, source).ToList());
#endif
}

/// <summary>
/// Result of a single-pass file parse containing extracted symbols and reference edges.
/// </summary>
public sealed record FileParseResult(
    IReadOnlyList<Symbol> Symbols,
    IReadOnlyList<SymbolReference> References);

/// <summary>
/// Convenient base class implementing <see cref="ILanguageParser"/> with default behaviors.
/// </summary>
public abstract class LanguageParserBase : ILanguageParser
{
    public virtual ParserMetadata Metadata =>
        GetType().GetCustomAttribute<ParserMetadataAttribute>()?.ToMetadata()
        ?? new ParserMetadata(Language, $"{Language} Parser", "1.0.0");

    public abstract IReadOnlyList<string> FileExtensions { get; }
    public abstract string Language { get; }

    public abstract IEnumerable<Symbol> Parse(string filePath, string source);

    public virtual IEnumerable<SymbolReference> ParseReferences(string filePath, string source) =>
        Array.Empty<SymbolReference>();

    public virtual FileParseResult ParseFile(string filePath, string source) =>
        new(Parse(filePath, source).ToList(), ParseReferences(filePath, source).ToList());
}
