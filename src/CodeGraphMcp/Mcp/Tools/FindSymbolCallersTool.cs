using System.ComponentModel;
using CodeGraphMcp.Db;
using CodeGraphMcp.Models;
using ModelContextProtocol.Server;

namespace CodeGraphMcp.Mcp.Tools;

public sealed record FindCallersResult(
    string TargetSymbol,
    IReadOnlyList<CallerEntry> Callers,
    string? Note);

[McpServerToolType]
public sealed class FindSymbolCallersTool(CodeGraphDb db)
{
    [McpServerTool]
    [Description("""
        Finds all callers of a named symbol by querying the symbol_references graph table.
        Returns caller name, file path, and line number for each reference.
        Note: the symbol_references table is populated during indexing when reference analysis is active.
        Returns an empty list with a note if the table has not yet been populated.
        """)]
    public FindCallersResult FindSymbolCallers(
        [Description("Symbol name to find callers of (exact, case-insensitive)")] string symbolName)
    {
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
