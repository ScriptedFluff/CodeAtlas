namespace CodeAtlas.Parser;

/// <summary>
/// Registry of active language parsers supporting extension, file path, and ID lookups.
/// </summary>
public interface IParserRegistry
{
    /// <summary>All currently registered parsers.</summary>
    IReadOnlyList<ILanguageParser> GetAllParsers();

    /// <summary>Gets the parser registered for a specific file extension (e.g. ".cs").</summary>
    ILanguageParser? GetParserForExtension(string extension);

    /// <summary>Gets the parser registered for a given file path by inspecting its extension.</summary>
    ILanguageParser? GetParserForFile(string filePath);

    /// <summary>Gets a parser by its unique ID or language name (e.g. "csharp").</summary>
    ILanguageParser? GetParserById(string id);

    /// <summary>Returns all unique file extensions supported by the registry.</summary>
    IReadOnlyList<string> GetSupportedExtensions();
}
