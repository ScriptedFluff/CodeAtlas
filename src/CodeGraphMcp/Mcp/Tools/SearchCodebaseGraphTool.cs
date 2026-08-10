using System.ComponentModel;
using CodeGraphMcp.Db;
using CodeGraphMcp.Git;
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
public sealed class SearchCodebaseGraphTool(CodeGraphDb db, DirtyFileBuffer buffer)
{
    [McpServerTool]
    [Description("""
        Searches the codebase knowledge graph for symbols matching a name pattern.
        Returns up to 20 matches with file path, line numbers, type, language, and doc comments.
        Includes live uncommitted symbols from the dirty file buffer.
        Use % as a wildcard: 'User%' matches UserService, UserModel, etc.
        Exact names also work: 'IndexOrchestrator' returns only that symbol.
        """)]
    public SearchResult SearchCodebaseGraph(
        [Description("Symbol name pattern. Use % as wildcard (SQL LIKE style).")] string pattern,
        [Description("Optional: Class, Method, Interface, Property, Enum, Function")] string? symbolType = null,
        [Description("Optional: csharp, typescript, html, powershell")] string? language = null)
    {
        // 1. SQLite results (LIKE supports % wildcards natively)
        var dbMatches = db.Symbols.FindByName(pattern)
            .Where(s => symbolType is null
                || s.SymbolType.ToString().Equals(symbolType, StringComparison.OrdinalIgnoreCase))
            .Where(s => language is null
                || (s.Language ?? "").Equals(language, StringComparison.OrdinalIgnoreCase))
            .Select(s => new SearchMatch(
                s.SymbolName, s.SymbolType.ToString(), s.Language,
                s.FilePath, s.StartLine, s.EndLine, s.DocComment, IsLive: false))
            .ToList();

        // 2. Dirty buffer results
        //    FindByName does exact match — only viable for non-wildcard patterns.
        //    For wildcard patterns, do a manual substring scan across all cached dirty symbols.
        List<SearchMatch> dirtyMatches;
        if (!pattern.Contains('%'))
        {
            var hits = buffer.FindByName(pattern, language) ?? [];
            dirtyMatches = hits
                .Where(s => symbolType is null
                    || s.SymbolType.ToString().Equals(symbolType, StringComparison.OrdinalIgnoreCase))
                .Select(s => new SearchMatch(
                    s.SymbolName, s.SymbolType.ToString(), s.Language,
                    s.FilePath, s.StartLine, s.EndLine, s.DocComment, IsLive: true))
                .ToList();
        }
        else
        {
            // Wildcard: convert SQL LIKE pattern (% -> .*) to Regex for accurate matching
            var regexPattern = "^" + System.Text.RegularExpressions.Regex.Escape(pattern).Replace("\\%", ".*") + "$";
            var regex = new System.Text.RegularExpressions.Regex(regexPattern, System.Text.RegularExpressions.RegexOptions.IgnoreCase);

            dirtyMatches = buffer
                .GetAllDirtySymbols()
                .Where(s => regex.IsMatch(s.SymbolName)
                    && (symbolType is null
                        || s.SymbolType.ToString().Equals(symbolType, StringComparison.OrdinalIgnoreCase))
                    && (language is null
                        || (s.Language ?? "").Equals(language, StringComparison.OrdinalIgnoreCase)))
                .Select(s => new SearchMatch(
                    s.SymbolName, s.SymbolType.ToString(), s.Language,
                    s.FilePath, s.StartLine, s.EndLine, s.DocComment, IsLive: true))
                .ToList();
        }

        // 3. Merge: dirty buffer takes precedence over SQLite for same (FilePath, SymbolName).
        //    Normalize path separators — dirty buffer uses '/' but SQLite may store '\'.
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
