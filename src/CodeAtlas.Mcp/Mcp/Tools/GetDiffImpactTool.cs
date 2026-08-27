using System.ComponentModel;
using CodeAtlas.Core.Db;
using CodeAtlas.Core.Git;
using CodeAtlas.Core.Indexer;
using ModelContextProtocol.Server;

namespace CodeAtlas.Mcp.Tools;

public sealed record FileImpact(
    string RelativePath,
    int SymbolCount,
    bool IsDirty);

public sealed record DiffImpactResult(
    string Branch,
    string HeadCommit,
    string LastIndexedCommit,
    IReadOnlyList<FileImpact> ChangedFiles,
    IReadOnlyList<FileImpact> DirtyFiles,
    int TotalSymbolsAffected);

[McpServerToolType]
public sealed class GetDiffImpactTool(IDynamicWorkspaceService workspaceService)
{
    [McpServerTool]
    [Description("""
        Analyzes the impact of recent git changes: lists files changed since the last index run,
        the number of symbols each contains, and currently dirty (uncommitted) files.
        Use this before recommending refactors or when assessing the blast radius of a change.
        """)]
    public async Task<DiffImpactResult> GetDiffImpactAsync(
        [Description("Optional: compare from this specific commit hash instead of the last indexed commit")] string? sinceCommit = null,
        [Description("Optional: target repository/workspace directory to query (defaults to active workspace)")] string? targetDirectory = null,
        CancellationToken ct = default)
    {
        var context = await workspaceService.GetOrIndexWorkspaceAsync(targetDirectory, ct);
        var db = context.Db;
        var repoPath = context.WorkspaceRoot;

        var lastIndexedHash = db.IndexState.GetLastCommitHash();
        var headHash        = GitHelpers.GetHeadCommitHash(repoPath) ?? "unknown";
        var branch          = GitHelpers.GetCurrentBranch(repoPath) ?? "unknown";

        var baseHash = sinceCommit ?? lastIndexedHash;

        IReadOnlyList<string> changedFiles = baseHash is null
            ? []
            : GitHelpers.GetChangedFiles(repoPath, baseHash);

        var dirtyFiles = GitHelpers.GetDirtyFiles(repoPath);

        var changedImpacts = changedFiles
            .Select(f =>
            {
                var norm = f.Replace('\\', '/');
                var count = db.Symbols.CountByFile(norm);
                if (count == 0 && norm != f)
                    count = db.Symbols.CountByFile(f);
                return new FileImpact(f, count, IsDirty: false);
            })
            .ToList();

        var changedSet = changedFiles.Select(f => f.Replace('\\', '/')).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var dirtyImpacts = dirtyFiles
            .Where(f => !changedSet.Contains(f.Replace('\\', '/')))
            .Select(f => new FileImpact(f, 0, IsDirty: true))
            .Take(20)
            .ToList();

        return new DiffImpactResult(
            Branch:              branch,
            HeadCommit:          headHash,
            LastIndexedCommit:   lastIndexedHash ?? "none",
            ChangedFiles:        changedImpacts,
            DirtyFiles:          dirtyImpacts,
            TotalSymbolsAffected: changedImpacts.Sum(f => f.SymbolCount));
    }
}
