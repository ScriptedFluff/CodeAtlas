using System.ComponentModel;
using CodeGraphMcp.Db;
using CodeGraphMcp.Git;
using CodeGraphMcp.Indexer;
using CodeGraphMcp.Models;
using ModelContextProtocol.Server;

namespace CodeGraphMcp.Mcp.Tools;

public sealed record SearchMatch(
    string SymbolName,
    string SymbolType,
    string? Language,
    string FilePath,
    int StartLine,
    int EndLine,
    string? DocComment,
    bool IsLive);

public sealed record SearchResult(
    string Pattern,
    IReadOnlyList<SearchMatch> Matches);

[McpServerToolType]
public sealed class SearchCodebaseGraphTool(
    IDynamicWorkspaceService workspaceService,
    IWorkspaceBufferManager bufferManager)
{
    [McpServerTool]
    [Description("""
        Searches the codebase knowledge graph for symbols matching a name pattern.
        Automatically scopes to the current active workspace project index without needing manual directory restrictions.
        Returns up to 20 matches with file path, precise StartLine and EndLine range, symbol type, language, and doc comments. Use line ranges for targeted file reading.
        Includes live uncommitted symbols from the dirty file buffer.
        Use % as a wildcard: 'User%' matches UserService, UserModel, etc.
        Exact names also work: 'IndexOrchestrator' returns only that symbol.
        Use exactMatch: true to match symbol names exactly (case-insensitive).
        Use regex: "..." to apply regular expression filtering on symbol names.
        """)]
    public async Task<SearchResult> SearchCodebaseGraphAsync(
        [Description("Symbol name pattern. Use % as wildcard (SQL LIKE style).")] string pattern,
        [Description("Optional: Class, Method, Interface, Property, Enum, Function")] string? symbolType = null,
        [Description("Optional: csharp, typescript, html, powershell")] string? language = null,
        [Description("Optional: target repository/workspace directory to query (defaults to active workspace)")] string? targetDirectory = null,
        [Description("Optional: if true, requires symbol name to match pattern exactly (case-insensitive) instead of substring matching")] bool exactMatch = false,
        [Description("Optional: regex pattern to post-filter symbol names")] string? regex = null,
        CancellationToken ct = default)
    {
        System.Text.RegularExpressions.Regex? compiledRegex = null;
        if (!string.IsNullOrWhiteSpace(regex))
        {
            try
            {
                compiledRegex = new System.Text.RegularExpressions.Regex(
                    regex,
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase,
                    TimeSpan.FromMilliseconds(500));
            }
            catch (ArgumentException ex)
            {
                throw new ArgumentException($"Invalid regex pattern '{regex}': {ex.Message}", nameof(regex), ex);
            }
        }

        var context = await workspaceService.GetOrIndexWorkspaceAsync(targetDirectory, ct);
        var db = context.Db;
        var buffer = bufferManager.GetOrCreateBuffer(context.WorkspaceRoot);

        // 1. SQLite results
        var dbMatches = db.Symbols.FindByName(pattern)
            .Where(s => !exactMatch || s.SymbolName.Equals(pattern, StringComparison.OrdinalIgnoreCase))
            .Where(s => symbolType is null
                || s.SymbolType.ToString().Equals(symbolType, StringComparison.OrdinalIgnoreCase))
            .Where(s => language is null
                || (s.Language ?? "").Equals(language, StringComparison.OrdinalIgnoreCase))
            .Where(s => compiledRegex is null || compiledRegex.IsMatch(s.SymbolName))
            .Select(s => new SearchMatch(
                s.SymbolName, s.SymbolType.ToString(), s.Language,
                s.FilePath, s.StartLine, s.EndLine, s.DocComment, IsLive: false))
            .ToList();

        // 2. Dirty buffer results
        List<SearchMatch> dirtyMatches;
        if (exactMatch)
        {
            var hits = buffer.FindByName(pattern, language) ?? [];
            dirtyMatches = hits
                .Where(s => s.SymbolName.Equals(pattern, StringComparison.OrdinalIgnoreCase))
                .Where(s => symbolType is null
                    || s.SymbolType.ToString().Equals(symbolType, StringComparison.OrdinalIgnoreCase))
                .Where(s => compiledRegex is null || compiledRegex.IsMatch(s.SymbolName))
                .Select(s => new SearchMatch(
                    s.SymbolName, s.SymbolType.ToString(), s.Language,
                    s.FilePath, s.StartLine, s.EndLine, s.DocComment, IsLive: true))
                .ToList();
        }
        else if (!pattern.Contains('%'))
        {
            var hits = buffer.FindByName(pattern, language) ?? [];
            dirtyMatches = hits
                .Where(s => symbolType is null
                    || s.SymbolType.ToString().Equals(symbolType, StringComparison.OrdinalIgnoreCase))
                .Where(s => compiledRegex is null || compiledRegex.IsMatch(s.SymbolName))
                .Select(s => new SearchMatch(
                    s.SymbolName, s.SymbolType.ToString(), s.Language,
                    s.FilePath, s.StartLine, s.EndLine, s.DocComment, IsLive: true))
                .ToList();
        }
        else
        {
            var regexPattern = "^" + System.Text.RegularExpressions.Regex.Escape(pattern).Replace("\\%", ".*") + "$";
            var wildcardRegex = new System.Text.RegularExpressions.Regex(regexPattern, System.Text.RegularExpressions.RegexOptions.IgnoreCase);

            dirtyMatches = buffer
                .GetAllDirtySymbols()
                .Where(s => wildcardRegex.IsMatch(s.SymbolName)
                    && (symbolType is null
                        || s.SymbolType.ToString().Equals(symbolType, StringComparison.OrdinalIgnoreCase))
                    && (language is null
                        || (s.Language ?? "").Equals(language, StringComparison.OrdinalIgnoreCase))
                    && (compiledRegex is null || compiledRegex.IsMatch(s.SymbolName)))
                .Select(s => new SearchMatch(
                    s.SymbolName, s.SymbolType.ToString(), s.Language,
                    s.FilePath, s.StartLine, s.EndLine, s.DocComment, IsLive: true))
                .ToList();
        }

        // 3. Merge: dirty buffer takes precedence over SQLite for same (FilePath, SymbolName).
        static string NormPath(string p) => p.Replace('\\', '/');
        var dirtyKeys = dirtyMatches
            .Select(m => (NormPath(m.FilePath), m.SymbolName))
            .ToHashSet();

        var merged = dirtyMatches
            .Concat(dbMatches.Where(d => !dirtyKeys.Contains((NormPath(d.FilePath), d.SymbolName))))
            .Take(20)
            .ToList();

        return new SearchResult(pattern, merged);
    }
}
