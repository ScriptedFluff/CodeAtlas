using System.ComponentModel;
using CodeAtlas.Core.Git;
using CodeAtlas.Core.Indexer;
using CodeAtlas.Parser;
using ModelContextProtocol.Server;

namespace CodeAtlas.Mcp.Tools;

public sealed record SyncGitDiffResult(
    string Branch,
    string HeadCommit,
    int FilesChanged,
    int FilesReindexed,
    int SymbolsAdded,
    int SymbolsDeleted,
    long ElapsedMs,
    string Mode,
    string? Message = null);

[McpServerToolType]
public sealed class SyncGitDiffTool(
    IDynamicWorkspaceService workspaceService,
    IWorkspaceBufferManager bufferManager,
    IParserRegistry parserRegistry)
{
    [McpServerTool]
    [Description("""
        Performs a fast incremental delta sync of all git-modified and dirty files against the local SQLite index.
        Re-indexes only changed/dirty files and purges deleted files without performing a full repository scan.
        """)]
    public async Task<SyncGitDiffResult> SyncGitDiffAsync(
        [Description("Optional: target repository/workspace directory (defaults to active workspace)")] string? targetDirectory = null,
        [Description("Optional: if true, ignores .gitignore filtering during delta sync")] bool noIgnore = false,
        CancellationToken ct = default)
    {
        var context = await workspaceService.GetOrIndexWorkspaceAsync(targetDirectory, ct);
        var db = context.Db;
        var repoPath = context.WorkspaceRoot;

        var deltaEngine = new DeltaEngine(repoPath, db, parserRegistry);
        var deltaResult = await deltaEngine.RunAsync(noIgnore: noIgnore, verbose: false, ct: ct);

        var buffer = bufferManager.GetOrCreateBuffer(repoPath);
        buffer.Refresh();

        var branch   = GitHelpers.GetCurrentBranch(repoPath) ?? "unknown";
        var headHash = GitHelpers.GetHeadCommitHash(repoPath) ?? "unknown";

        return new SyncGitDiffResult(
            Branch:         branch,
            HeadCommit:     headHash,
            FilesChanged:   deltaResult.FilesChanged,
            FilesReindexed: deltaResult.FilesReindexed,
            SymbolsAdded:   deltaResult.SymbolsAdded,
            SymbolsDeleted: deltaResult.SymbolsDeleted,
            ElapsedMs:      deltaResult.ElapsedMs,
            Mode:           deltaResult.Mode,
            Message:        $"Delta sync completed in {deltaResult.ElapsedMs}ms ({deltaResult.Mode}).");
    }
}
