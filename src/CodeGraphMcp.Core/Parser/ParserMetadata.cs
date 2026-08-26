namespace CodeGraphMcp.Parser;

/// <summary>
/// Metadata describing a language parser plugin, its identity, version, and author information.
/// </summary>
public sealed record ParserMetadata(
    string Id,
    string Name,
    string Version,
    string? Description = null,
    string? Author = null);
