using System.ComponentModel;
using CodeGraphMcp.Db;
using ModelContextProtocol.Server;

namespace CodeGraphMcp.Mcp.Tools;

[McpServerToolType]
public sealed class PruneIndexedWorktreesTool
{
    [McpServerTool]
    [Description("""
        Scans the index storage registry for database caches whose underlying repository folders or git worktrees no longer exist on disk.
        Purges obsolete .db cache files, freeing disk space and removing stale workspace indexes.
        Returns a summary of pruned databases and total bytes freed.
        """)]
    public Task<PruneWorktreesResult> PruneIndexedWorktreesAsync(
        [Description("Optional: override index database directory to prune (defaults to global appdata store %LOCALAPPDATA%/CodeGraph/dbs)")] string? dbDir = null,
        [Description("Optional: if true, preview candidate databases to prune without performing actual file deletion")] bool dryRun = false,
        CancellationToken ct = default)
    {
        return WorktreePruner.PruneIndexedWorktreesAsync(dbDir, dryRun, ct);
    }
}
