using System.ComponentModel;
using CodeAtlas.Core.Db;
using CodeAtlas.Core.Indexer;
using CodeAtlas.Models;
using ModelContextProtocol.Server;

namespace CodeAtlas.Mcp.Tools;

public sealed record FindCallersResult(
    string TargetSymbol,
    IReadOnlyList<CallerEntry> Callers,
    string? Note);

[McpServerToolType]
public sealed class FindSymbolCallersTool(IDynamicWorkspaceService workspaceService)
{
    [McpServerTool]
    [Description("""
        Finds all callers of a named symbol by querying the symbol_references graph table.
        Returns caller name, file path, and line number for each reference.
        Note: the symbol_references table is populated during indexing when reference analysis is active.
        Returns an empty list with a note if the table has not yet been populated.
        """)]
    public async Task<FindCallersResult> FindSymbolCallersAsync(
        [Description("Symbol name to find callers of (exact, case-insensitive)")] string symbolName,
        [Description("Optional: target repository/workspace directory to query (defaults to active workspace)")] string? targetDirectory = null,
        CancellationToken ct = default)
    {
        var context = await workspaceService.GetOrIndexWorkspaceAsync(targetDirectory, ct);
        var db = context.Db;
        var callers = db.References.FindCallers(symbolName);

        if (callers.Count == 0)
        {
            return new FindCallersResult(
                symbolName,
                [],
                "No callers found in index for this symbol.");
        }

        return new FindCallersResult(symbolName, callers, null);
    }
}
