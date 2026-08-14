using System.ComponentModel;
using CodeGraphMcp.Parser;
using ModelContextProtocol.Server;

namespace CodeGraphMcp.Mcp.Tools;

public sealed record SupportedLanguageInfo(
    string Language,
    IReadOnlyList<string> FileExtensions);

public sealed record SupportedLanguagesResult(
    IReadOnlyList<SupportedLanguageInfo> SupportedLanguages);

[McpServerToolType]
public sealed class GetSupportedLanguagesTool(IReadOnlyList<ILanguageParser> parsers)
{
    [McpServerTool]
    [Description("""
        Lists the programming languages and file extensions currently supported by CodeGraphMcp.
        Allows AI agents to dynamically discover indexing capabilities at session initialization.
        """)]
    public Task<SupportedLanguagesResult> GetSupportedLanguagesAsync(CancellationToken ct = default)
    {
        var languages = parsers
            .Select(p => new SupportedLanguageInfo(p.Language, p.FileExtensions))
            .DistinctBy(l => l.Language, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return Task.FromResult(new SupportedLanguagesResult(languages));
    }
}
