using System.ComponentModel;
using CodeGraphMcp.Db;
using CodeGraphMcp.Git;
using ModelContextProtocol.Server;

namespace CodeGraphMcp.Mcp.Tools;

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
public sealed class GetDiffImpactTool(CodeGraphDb db, RepoContext repo)
{
    [McpServerTool]
    [Description("""
        Analyzes the impact of recent git changes: lists files changed since the last index run,
        the number of symbols each contains, and currently dirty (uncommitted) files.
        Use this before recommending refactors or when assessing the blast radius of a change.
        """)]
    public DiffImpactResult GetDiffImpact(
        [Description("Optional: compare from this specific commit hash instead of the last indexed commit")] string? sinceCommit = null)
    {
        var lastIndexedHash = db.IndexState.GetLastCommitHash();
        var headHash        = GitHelpers.GetHeadCommitHash(repo.RepoPath) ?? "unknown";
        var branch          = GitHelpers.GetCurrentBranch(repo.RepoPath) ?? "unknown";

        var baseHash = sinceCommit ?? lastIndexedHash;

        IReadOnlyList<string> changedFiles = baseHash is null
            ? []
            : GitHelpers.GetChangedFiles(repo.RepoPath, baseHash);

        var dirtyFiles = GitHelpers.GetDirtyFiles(repo.RepoPath);

        var changedImpacts = changedFiles
            .Select(f =>
            {
                var norm = f.Replace('\\', '/');
                var count = db.Symbols.CountByFile(norm);
                // Also check with original path separators if norm returned 0
                if (count == 0 && norm != f)
                    count = db.Symbols.CountByFile(f);
                return new FileImpact(f, count, IsDirty: false);
            })
            .ToList();

        // Dirty files not already captured in changedFiles
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
