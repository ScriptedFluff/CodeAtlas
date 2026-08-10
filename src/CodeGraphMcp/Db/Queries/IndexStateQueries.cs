using Microsoft.Data.Sqlite;

namespace CodeGraphMcp.Db.Queries;

public sealed class IndexStateQueries(SqliteConnection connection)
{
    /// <summary>Returns the last successfully indexed commit hash, or null if never indexed.</summary>
    public string? GetLastCommitHash()
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT last_commit_hash FROM index_state ORDER BY indexed_at DESC LIMIT 1;";
        return cmd.ExecuteScalar() as string;
    }

    /// <summary>Returns the current branch name from the last index run.</summary>
    public string? GetLastBranch()
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT branch FROM index_state ORDER BY indexed_at DESC LIMIT 1;";
        return cmd.ExecuteScalar() as string;
    }

    /// <summary>
    /// Upserts the index state entry.
    /// Uses last_commit_hash as primary key so each commit has a record.
    /// </summary>
    public void SetIndexState(string branch, string commitHash, int dirtyFilesCount = 0)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            INSERT INTO index_state (branch, last_commit_hash, indexed_at, dirty_files_count)
            VALUES (@branch, @hash, @now, @dirty)
            ON CONFLICT(last_commit_hash) DO UPDATE SET
                branch            = excluded.branch,
                indexed_at        = excluded.indexed_at,
                dirty_files_count = excluded.dirty_files_count;
            """;
        cmd.Parameters.AddWithValue("@branch", branch);
        cmd.Parameters.AddWithValue("@hash",   commitHash);
        cmd.Parameters.AddWithValue("@now",    DateTimeOffset.UtcNow.ToString("O"));
        cmd.Parameters.AddWithValue("@dirty",  dirtyFilesCount);
        cmd.ExecuteNonQuery();
    }

    /// <summary>True if any index state row exists for this repo.</summary>
    public bool HasBeenIndexed()
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM index_state;";
        return (long)(cmd.ExecuteScalar() ?? 0L) > 0;
    }
}
