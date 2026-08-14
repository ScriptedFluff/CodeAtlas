using System.ComponentModel;
using CodeGraphMcp.CrossStack;
using CodeGraphMcp.Db;
using CodeGraphMcp.Indexer;
using ModelContextProtocol.Server;

namespace CodeGraphMcp.Mcp.Tools;

public sealed record DtoContractResult(
    string DtoName,
    string? CsFilePath,
    string? CsLineRange,
    string? TsFilePath,
    string? TsLineRange,
    string? AngularComponentFile,
    string? Note);

[McpServerToolType]
public sealed class GetServiceStackDtoContractTool(
    IDynamicWorkspaceService workspaceService,
    IWorkspaceLinkerManager? linkerManager = null)
{
    [McpServerTool]
    [Description("""
        Returns the full cross-stack contract for a ServiceStack DTO:
          - The C# request/response class (file path and line range)
          - The matching TypeScript interface in clevernpm (if indexed)
          - The Angular component that consumes it (if detectable)
        Call this before working on any ServiceStack endpoint to understand the
        full stack without manually reading multiple files.
        """)]
    public async Task<DtoContractResult> GetServiceStackDtoContractAsync(
        [Description("C# DTO class name, e.g. 'GetUserBillingRequest'")] string dtoName,
        [Description("Optional: target repository/workspace directory to query (defaults to active workspace)")] string? targetDirectory = null,
        CancellationToken ct = default)
    {
        var context = await workspaceService.GetOrIndexWorkspaceAsync(targetDirectory, ct);
        var db = context.Db;

        var cached = db.CrossStack.GetMapping(dtoName);
        if (cached is not null)
            return Map(cached);

        var linker = linkerManager?.GetOrCreateLinker(context.WorkspaceRoot, db) ?? new ServiceStackLinker(db);
        linker.EnsureLinked();

        var fresh = db.CrossStack.GetMapping(dtoName);
        if (fresh is null)
        {
            return new DtoContractResult(
                DtoName:             dtoName,
                CsFilePath:          null,
                CsLineRange:         null,
                TsFilePath:          null,
                TsLineRange:         null,
                AngularComponentFile: null,
                Note: $"'{dtoName}' not found in the index. Run 'index' to scan the repo first, then retry.");
        }

        return Map(fresh);
    }

    private static DtoContractResult Map(Models.CrossStackMapping m) =>
        new(
            DtoName:             m.DtoName,
            CsFilePath:          m.CsFilePath,
            CsLineRange:         m.CsStartLine.HasValue ? $"{m.CsStartLine}–{m.CsEndLine}" : null,
            TsFilePath:          m.TsFilePath,
            TsLineRange:         m.TsStartLine.HasValue ? $"{m.TsStartLine}–{m.TsEndLine}" : null,
            AngularComponentFile: m.AngularComponentPath,
            Note:                null);
}
