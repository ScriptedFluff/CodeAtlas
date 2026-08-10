using System.ComponentModel;
using CodeGraphMcp.Db;
using CodeGraphMcp.Git;
using CodeGraphMcp.Models;
using ModelContextProtocol.Server;

namespace CodeGraphMcp.Mcp.Tools;

public sealed record SymbolDefinitionResult(
    string SymbolName,
    string SymbolType,
    string FilePath,
    int StartLine,
    int EndLine,
    string? Signature,
    string? DocComment,
    string? SourceCode,
    bool IsLive);

[McpServerToolType]
public sealed class GetSymbolDefinitionTool(
    CodeGraphDb db,
    DirtyFileBuffer buffer,
    RepoContext repo)
{
    [McpServerTool]
    [Description("""
        Retrieves the exact source code of a named symbol (class, method, interface, property, etc.)
        along with its file path, line range, signature, and doc comment.
        Returns the LIVE uncommitted version if the file has unsaved changes.
        Use this instead of reading entire source files.
        Use % as a wildcard in symbolName (e.g. 'GetUser%' matches GetUserRequest, GetUserById, etc.).
        """)]
    public SymbolDefinitionResult? GetSymbolDefinition(
        [Description("Symbol name to look up. Use % as wildcard (SQL LIKE).")] string symbolName,
        [Description("Optional: Class, Method, Interface, Property, Enum, Function")] string? symbolType = null,
        [Description("Optional: filter by relative file path substring")] string? filePath = null)
    {
        // 1. Check dirty buffer first (exact name match only — buffer doesn't support wildcards)
        if (!symbolName.Contains('%'))
        {
            var dirtyHits = buffer.FindByName(symbolName);
            if (dirtyHits is not null && dirtyHits.Count > 0)
            {
                var hit = dirtyHits
                    .Where(s => symbolType is null
                        || s.SymbolType.ToString().Equals(symbolType, StringComparison.OrdinalIgnoreCase))
                    .Where(s => filePath is null
                        || s.FilePath.Contains(filePath, StringComparison.OrdinalIgnoreCase))
                    .FirstOrDefault();

                if (hit is not null)
                {
                    return new SymbolDefinitionResult(
                        hit.SymbolName,
                        hit.SymbolType.ToString(),
                        hit.FilePath,
                        hit.StartLine,
                        hit.EndLine,
                        hit.Signature,
                        hit.DocComment,
                        ReadSourceRange(hit),
                        IsLive: true);
                }
            }
        }

        // 2. Fall back to SQLite (supports % wildcard via LIKE)
        var results = db.Symbols.FindByName(symbolName);
        var match = results
            .Where(s => symbolType is null
                || s.SymbolType.ToString().Equals(symbolType, StringComparison.OrdinalIgnoreCase))
            .Where(s => filePath is null
                || s.FilePath.Contains(filePath, StringComparison.OrdinalIgnoreCase))
            .FirstOrDefault();

        if (match is null) return null;

        return new SymbolDefinitionResult(
            match.SymbolName,
            match.SymbolType.ToString(),
            match.FilePath,
            match.StartLine,
            match.EndLine,
            match.Signature,
            match.DocComment,
            ReadSourceRange(match),
            IsLive: false);
    }

    private string? ReadSourceRange(Symbol sym)
    {
        try
        {
            var fullPath = Path.Combine(repo.RepoPath, sym.FilePath);
            if (!File.Exists(fullPath)) return null;
            var lines = File.ReadAllLines(fullPath);
            var start = Math.Max(0, sym.StartLine - 1);
            var end   = Math.Min(lines.Length - 1, sym.EndLine - 1);
            if (start > end) return null;
            return string.Join('\n', lines[start..(end + 1)]);
        }
        catch
        {
            return null;
        }
    }
}
