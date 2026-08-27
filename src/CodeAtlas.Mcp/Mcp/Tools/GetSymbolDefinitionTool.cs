using System.ComponentModel;
using CodeAtlas.Core.Db;
using CodeAtlas.Core.Git;
using CodeAtlas.Core.Indexer;
using CodeAtlas.Models;
using ModelContextProtocol.Server;

namespace CodeAtlas.Mcp.Tools;

public sealed record SymbolLocationEntry(
    string FilePath,
    int StartLine,
    int EndLine,
    string? Signature,
    string? SourceCode);

public sealed record SymbolDefinitionResult(
    string SymbolName,
    string SymbolType,
    string FilePath,
    int StartLine,
    int EndLine,
    string? Signature,
    string? DocComment,
    string? SourceCode,
    bool IsLive,
    IReadOnlyList<CallerEntry>? References = null,
    bool IsPartial = false,
    IReadOnlyList<SymbolLocationEntry>? Locations = null);

[McpServerToolType]
public sealed class GetSymbolDefinitionTool(
    IDynamicWorkspaceService workspaceService,
    IWorkspaceBufferManager bufferManager)
{
    [McpServerTool]
    [Description("""
        Retrieves the exact source code of a named symbol (class, method, interface, property, etc.)
        along with its file path, precise line range (StartLine to EndLine), signature, and doc comment.
        Automatically scopes to the current active workspace project index without needing manual directory restrictions.
        Returns the LIVE uncommitted version if the file has unsaved changes.
        Use this instead of reading entire source files.
        Use % as a wildcard in symbolName (e.g. 'GetUser%' matches GetUserRequest, GetUserById, etc.).
        """)]
    public async Task<SymbolDefinitionResult?> GetSymbolDefinitionAsync(
        [Description("Symbol name to look up. Use % as wildcard (SQL LIKE).")] string symbolName,
        [Description("Optional: Class, Method, Interface, Property, Enum, Function")] string? symbolType = null,
        [Description("Optional: filter by relative file path substring")] string? filePath = null,
        [Description("Optional: target repository/workspace directory to query (defaults to active workspace)")] string? targetDirectory = null,
        [Description("Optional: set to true to include incoming callers/reference sites in the result")] bool withReferences = false,
        CancellationToken ct = default)
    {
        var context = await workspaceService.GetOrIndexWorkspaceAsync(targetDirectory, ct);
        var db = context.Db;
        var buffer = bufferManager.GetOrCreateBuffer(context.WorkspaceRoot);

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
                    var callers = withReferences ? db.References.FindCallers(hit.SymbolName) : null;
                    List<SymbolLocationEntry>? locations = null;
                    var matchingHits = dirtyHits
                        .Where(s => s.SymbolName.Equals(hit.SymbolName, StringComparison.OrdinalIgnoreCase) &&
                                    s.SymbolType == hit.SymbolType)
                        .ToList();

                    if (matchingHits.Count > 1 || hit.IsPartial)
                    {
                        locations = matchingHits.Select(loc => new SymbolLocationEntry(
                            loc.FilePath,
                            loc.StartLine,
                            loc.EndLine,
                            loc.Signature,
                            ReadSourceRange(context.WorkspaceRoot, loc)
                        )).ToList();
                    }

                    return new SymbolDefinitionResult(
                        hit.SymbolName,
                        hit.SymbolType.ToString(),
                        hit.FilePath,
                        hit.StartLine,
                        hit.EndLine,
                        hit.Signature,
                        hit.DocComment,
                        ReadSourceRange(context.WorkspaceRoot, hit),
                        IsLive: true,
                        References: callers,
                        IsPartial: hit.IsPartial || (matchingHits.Count > 1),
                        Locations: locations);
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

        var dbCallers = withReferences ? db.References.FindCallers(match.SymbolName) : null;

        List<SymbolLocationEntry>? dbLocations = null;
        var partialHits = results
            .Where(s => s.SymbolName.Equals(match.SymbolName, StringComparison.OrdinalIgnoreCase) &&
                        s.SymbolType == match.SymbolType)
            .ToList();

        if (partialHits.Count > 1 || match.IsPartial)
        {
            dbLocations = partialHits.Select(loc => new SymbolLocationEntry(
                loc.FilePath,
                loc.StartLine,
                loc.EndLine,
                loc.Signature,
                ReadSourceRange(context.WorkspaceRoot, loc)
            )).ToList();
        }

        return new SymbolDefinitionResult(
            match.SymbolName,
            match.SymbolType.ToString(),
            match.FilePath,
            match.StartLine,
            match.EndLine,
            match.Signature,
            match.DocComment,
            ReadSourceRange(context.WorkspaceRoot, match),
            IsLive: false,
            References: dbCallers,
            IsPartial: match.IsPartial || (partialHits.Count > 1),
            Locations: dbLocations);
    }

    private static string? ReadSourceRange(string workspaceRoot, Symbol sym)
    {
        try
        {
            var fullPath = Path.Combine(workspaceRoot, sym.FilePath);
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
