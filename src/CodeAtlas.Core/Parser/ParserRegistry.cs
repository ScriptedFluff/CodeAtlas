using CodeAtlas.Core.Plugins;

namespace CodeAtlas.Parser;

/// <summary>
/// Default thread-safe implementation of <see cref="IParserRegistry"/> managing active language parsers.
/// </summary>
public sealed class ParserRegistry : IParserRegistry
{
    private readonly List<ILanguageParser> _parsers = [];
    private readonly Dictionary<string, ILanguageParser> _extensionMap = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, ILanguageParser> _idMap = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _lock = new();

    public ParserRegistry() { }

    public ParserRegistry(IEnumerable<ILanguageParser> parsers)
    {
        ArgumentNullException.ThrowIfNull(parsers);
        foreach (var parser in parsers)
        {
            Register(parser);
        }
    }

    /// <summary>
    /// Registers a parser in the registry.
    /// </summary>
    public void Register(ILanguageParser parser)
    {
        ArgumentNullException.ThrowIfNull(parser);
        lock (_lock)
        {
            // If replacing an existing parser with same ID or Language, remove previous instance from _parsers
            _parsers.RemoveAll(p =>
                (!string.IsNullOrWhiteSpace(parser.Metadata?.Id) && string.Equals(p.Metadata?.Id, parser.Metadata?.Id, StringComparison.OrdinalIgnoreCase)) ||
                (!string.IsNullOrWhiteSpace(parser.Language) && string.Equals(p.Language, parser.Language, StringComparison.OrdinalIgnoreCase)));

            _parsers.Add(parser);
            if (!string.IsNullOrWhiteSpace(parser.Metadata?.Id))
            {
                _idMap[parser.Metadata.Id] = parser;
            }
            if (!string.IsNullOrWhiteSpace(parser.Language))
            {
                _idMap[parser.Language] = parser;
            }

            foreach (var ext in parser.FileExtensions)
            {
                var normalizedExt = ext.StartsWith('.') ? ext : "." + ext;
                _extensionMap[normalizedExt] = parser;
            }
        }
    }

    /// <summary>
    /// Creates a default parser registry by discovering installed drop-in parsers across multi-tier directories.
    /// </summary>
    public static ParserRegistry CreateDefault()
    {
        var loader = new DirectoryParserPluginLoader();
        var result = loader.DiscoverAndLoad();
        return (ParserRegistry)result.Registry;
    }

    /// <inheritdoc />
    public IReadOnlyList<ILanguageParser> GetAllParsers()
    {
        lock (_lock)
        {
            return _parsers.ToList();
        }
    }

    /// <inheritdoc />
    public ILanguageParser? GetParserForExtension(string extension)
    {
        if (string.IsNullOrWhiteSpace(extension)) return null;
        var normalized = extension.StartsWith('.') ? extension : "." + extension;
        lock (_lock)
        {
            return _extensionMap.GetValueOrDefault(normalized);
        }
    }

    /// <inheritdoc />
    public ILanguageParser? GetParserForFile(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath)) return null;
        var ext = Path.GetExtension(filePath);
        return string.IsNullOrEmpty(ext) ? null : GetParserForExtension(ext);
    }

    /// <inheritdoc />
    public ILanguageParser? GetParserById(string id)
    {
        if (string.IsNullOrWhiteSpace(id)) return null;
        lock (_lock)
        {
            return _idMap.GetValueOrDefault(id);
        }
    }

    /// <inheritdoc />
    public IReadOnlyList<string> GetSupportedExtensions()
    {
        lock (_lock)
        {
            return _extensionMap.Keys.ToList();
        }
    }
}
