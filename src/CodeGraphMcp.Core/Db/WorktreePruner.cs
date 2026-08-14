using System.ComponentModel;
using Microsoft.Data.Sqlite;

namespace CodeGraphMcp.Db;

public sealed record PrunedWorktreeEntry(
    string DatabasePath,
    string? InferredWorkspaceRoot,
    long BytesFreed);

public sealed record PruneWorktreesResult(
    int PrunedDatabaseCount,
    long TotalBytesFreed,
    IReadOnlyList<PrunedWorktreeEntry> PrunedEntries);

public static class WorktreePruner
{
    public static Task<PruneWorktreesResult> PruneIndexedWorktreesAsync(
        string? dbDir = null,
        bool dryRun = false,
        CancellationToken ct = default)
    {
        var targetDir = dbDir;
        if (string.IsNullOrWhiteSpace(targetDir))
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            targetDir = Path.Combine(appData, "CodeGraph", "dbs");
        }

        if (!Directory.Exists(targetDir))
        {
            return Task.FromResult(new PruneWorktreesResult(0, 0, []));
        }

        var prunedEntries = new List<PrunedWorktreeEntry>();
        long totalFreedBytes = 0;

        SqliteConnection.ClearAllPools();

        var dbFiles = Directory.GetFiles(targetDir, "*.db", SearchOption.TopDirectoryOnly);
        foreach (var dbFile in dbFiles)
        {
            ct.ThrowIfCancellationRequested();

            string? inferredRoot = null;
            bool isOrphaned = false;

            try
            {
                var builder = new SqliteConnectionStringBuilder
                {
                    DataSource = dbFile,
                    Mode = SqliteOpenMode.ReadOnly
                };

                using var conn = new SqliteConnection(builder.ConnectionString);
                conn.Open();

                using var cmd = conn.CreateCommand();
                cmd.CommandText = "SELECT file_path FROM symbols LIMIT 10;";
                using var reader = cmd.ExecuteReader();

                var samplePaths = new List<string>();
                while (reader.Read())
                {
                    if (!reader.IsDBNull(0))
                    {
                        samplePaths.Add(reader.GetString(0));
                    }
                }

                if (samplePaths.Count > 0)
                {
                    var firstPath = samplePaths[0];
                    var rootCandidate = Path.GetPathRoot(firstPath);
                    var parts = firstPath.Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries);

                    if (parts.Length > 0 && !string.IsNullOrEmpty(rootCandidate))
                    {
                        var drive = parts[0].EndsWith(':') ? parts[0] + "\\" : parts[0];
                        if (parts.Length > 2)
                        {
                            var candidate = Path.Combine(drive, parts[1], parts[2]);
                            if (!Directory.Exists(candidate))
                            {
                                var parentCandidate = Path.Combine(drive, parts[1]);
                                inferredRoot = Directory.Exists(parentCandidate) ? candidate : parentCandidate;
                            }
                            else
                            {
                                inferredRoot = candidate;
                            }
                        }
                        else
                        {
                            inferredRoot = Path.GetDirectoryName(firstPath);
                        }
                    }

                    if (samplePaths.All(p => !File.Exists(p) && !Directory.Exists(Path.GetDirectoryName(p) ?? "")))
                    {
                        isOrphaned = true;
                    }
                }
                else
                {
                    isOrphaned = true;
                }
            }
            catch
            {
                isOrphaned = true;
            }

            if (isOrphaned)
            {
                SqliteConnection.ClearAllPools();

                long bytes = 0;
                var fi = new FileInfo(dbFile);
                if (fi.Exists) bytes += fi.Length;

                var walPath = dbFile + "-wal";
                var shmPath = dbFile + "-shm";
                var walFi = new FileInfo(walPath);
                if (walFi.Exists) bytes += walFi.Length;
                var shmFi = new FileInfo(shmPath);
                if (shmFi.Exists) bytes += shmFi.Length;

                if (!dryRun)
                {
                    try
                    {
                        if (File.Exists(dbFile)) File.Delete(dbFile);
                        if (File.Exists(walPath)) File.Delete(walPath);
                        if (File.Exists(shmPath)) File.Delete(shmPath);
                    }
                    catch
                    {
                        // Ignore locked files
                        continue;
                    }
                }

                totalFreedBytes += bytes;
                prunedEntries.Add(new PrunedWorktreeEntry(dbFile, inferredRoot, bytes));
            }
        }

        return Task.FromResult(new PruneWorktreesResult(
            prunedEntries.Count,
            totalFreedBytes,
            prunedEntries));
    }
}
