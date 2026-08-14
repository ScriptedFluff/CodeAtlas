using CodeGraphMcp.Git;
using Microsoft.Data.Sqlite;

namespace CodeGraphMcp.Db;

/// <summary>
/// Discovers existing indexed databases for the same repository across local .agent-index
/// folders or global storage, seeding new worktree databases to enable delta re-indexing.
/// </summary>
public sealed record SeedResult(string SourceDbPath, string SourceCommitHash);

public static class DbSeedingService
{
    public static SeedResult? TrySeedDatabase(string repoPath, CodeGraphDb targetDb)
    {
        try
        {
            var normTarget = Path.GetFullPath(targetDb.DbFilePath);

            var candidates = FindCandidateDbFiles(repoPath, normTarget);

            foreach (var candidate in candidates)
            {
                if (!File.Exists(candidate)) continue;
                if (string.Equals(Path.GetFullPath(candidate), normTarget, StringComparison.OrdinalIgnoreCase)) continue;

                var commitHash = GetCommitHashFromDb(candidate);
                if (string.IsNullOrWhiteSpace(commitHash)) continue;

                if (GitHelpers.CommitExists(repoPath, commitHash))
                {
                    using var candidateConn = new SqliteConnection($"Data Source={candidate};Mode=ReadOnly;");
                    candidateConn.Open();
                    candidateConn.BackupDatabase(targetDb.Connection);
                    return new SeedResult(candidate, commitHash);
                }

            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[DbSeedingService] Warning: Seeding check failed: {ex.Message}");
        }

        return null;
    }



    private static IEnumerable<string> FindCandidateDbFiles(string repoPath, string targetDbPath)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // 1. Global DB directory
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var globalDir = Path.Combine(appData, "CodeGraph", "dbs");
        if (Directory.Exists(globalDir))
        {
            foreach (var f in Directory.EnumerateFiles(globalDir, "*.db"))
            {
                set.Add(f);
            }
        }

        // 2. Git root local DB
        var gitRoot = GitHelpers.FindGitRoot(repoPath);
        if (gitRoot is not null)
        {
            var localDb = Path.Combine(gitRoot, ".codegraph", "code_graph.db");
            if (File.Exists(localDb)) set.Add(localDb);

            // Also check sibling worktrees under git root parent if applicable
            var parentDir = Path.GetDirectoryName(gitRoot);
            if (parentDir is not null && Directory.Exists(parentDir))
            {
                try
                {
                    foreach (var sub in Directory.EnumerateDirectories(parentDir))
                    {
                        var siblingDb = Path.Combine(sub, ".codegraph", "code_graph.db");
                        if (File.Exists(siblingDb)) set.Add(siblingDb);
                    }
                }
                catch { }
            }
        }

        return set;
    }

    private static string? GetCommitHashFromDb(string dbPath)
    {
        try
        {
            using var conn = new SqliteConnection($"Data Source={dbPath};Mode=ReadOnly;");
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT last_commit_hash FROM index_state LIMIT 1;";
            var res = cmd.ExecuteScalar();
            return res is DBNull or null ? null : res.ToString();
        }
        catch
        {
            return null;
        }
    }
}
