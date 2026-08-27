using System.Security.Cryptography;
using System.Text;
using CodeAtlas.Core.Git;
using Microsoft.Data.Sqlite;

namespace CodeAtlas.Core.Config;

public static class StorageConfig
{
    /// <summary>
    /// Resolves the SQLite database file path using a strict priority chain:
    ///   1. --db-dir &lt;path&gt; (explicit override)
    ///   2. Default: &lt;repo&gt;/.codeatlas/atlas.db (stored inside target repository)
    /// Automatically detects and renames legacy codegraph.db / code_graph.db or .codegraph/ caches to atlas.db.
    /// </summary>
    public static string ResolveDbPath(string? dbDir, string repoPath)
    {
        // Normalise so path comparison is stable regardless of trailing slashes or letter casing
        var normalisedRepo = Path.GetFullPath(repoPath)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .ToLowerInvariant();

        if (dbDir is not null)
        {
            Directory.CreateDirectory(dbDir);
#pragma warning disable CS0618 // Temporary legacy migration scheduled for removal
            TryMigrateLegacyDbInDirectory(dbDir);
#pragma warning restore CS0618
            return Path.Combine(dbDir, $"{HashPath(normalisedRepo)}.db");
        }

        // 1. Check & migrate legacy files in target directory (.codeatlas and .codegraph)
#pragma warning disable CS0618
        TryMigrateLegacyRepoStorage(normalisedRepo);
#pragma warning restore CS0618

        var localDbPath = Path.Combine(normalisedRepo, ".codeatlas", "atlas.db");
        if (File.Exists(localDbPath))
            return localDbPath;

        // 2. Check & migrate legacy files in parent git root (if repoPath is a subdirectory)
        var gitRoot = GitHelpers.FindGitRoot(normalisedRepo);
        if (gitRoot is not null && !string.Equals(gitRoot, normalisedRepo, StringComparison.OrdinalIgnoreCase))
        {
#pragma warning disable CS0618
            TryMigrateLegacyRepoStorage(gitRoot);
#pragma warning restore CS0618

            var gitRootLocalDbPath = Path.Combine(gitRoot, ".codeatlas", "atlas.db");
            if (File.Exists(gitRootLocalDbPath))
                return gitRootLocalDbPath;
        }

        // Default: Create local repository database inside .codeatlas directory
        var localDir = Path.Combine(normalisedRepo, ".codeatlas");
        Directory.CreateDirectory(localDir);
        AppendGitIgnore(normalisedRepo);
        return localDbPath;
    }

    /// <summary>
    /// Checks for legacy .codegraph directory or codegraph.db / code_graph.db files and renames them to atlas.db.
    /// </summary>
    [Obsolete("Temporary backward-compatibility migration for legacy codegraph.db / .codegraph/ directories. Scheduled for removal in a future release.")]
    public static void TryMigrateLegacyRepoStorage(string repoPath)
    {
        var codeatlasDir = Path.Combine(repoPath, ".codeatlas");
        var codegraphDir = Path.Combine(repoPath, ".codegraph");

        // Case A: .codegraph exists but .codeatlas does not -> rename the entire folder
        if (Directory.Exists(codegraphDir) && !Directory.Exists(codeatlasDir))
        {
            try
            {
                SqliteConnection.ClearAllPools();
                Directory.Move(codegraphDir, codeatlasDir);
            }
            catch
            {
                // If directory move fails (e.g. locks), continue with in-place file migration
            }
        }

        // Case B: Migrate any legacy database files in .codeatlas folder
        if (Directory.Exists(codeatlasDir))
        {
            TryMigrateLegacyDbInDirectory(codeatlasDir);
        }

        // Case C: If .codegraph still exists (e.g. .codeatlas already existed), migrate files from .codegraph
        if (Directory.Exists(codegraphDir))
        {
            TryMigrateLegacyDbInDirectory(codegraphDir);

            // If .codeatlas exists and has no atlas.db but .codegraph has atlas.db, move it across
            var sourceDb = Path.Combine(codegraphDir, "atlas.db");
            var destDb = Path.Combine(codeatlasDir, "atlas.db");
            if (File.Exists(sourceDb) && !File.Exists(destDb) && Directory.Exists(codeatlasDir))
            {
                MoveDbWithJournals(sourceDb, destDb);
            }
        }
    }

    /// <summary>
    /// Renames codegraph.db, code_graph.db, or similar legacy files inside a directory to atlas.db,
    /// moving companion WAL and SHM journal files alongside it.
    /// </summary>
    [Obsolete("Temporary backward-compatibility migration for legacy codegraph.db / .codegraph/ directories. Scheduled for removal in a future release.")]
    public static bool TryMigrateLegacyDbInDirectory(string dir)
    {
        if (!Directory.Exists(dir))
            return false;

        var targetDb = Path.Combine(dir, "atlas.db");
        if (File.Exists(targetDb))
            return false;

        string[] legacyNames = ["codegraph.db", "code_graph.db"];
        foreach (var legacyName in legacyNames)
        {
            var legacyDb = Path.Combine(dir, legacyName);
            if (File.Exists(legacyDb))
            {
                return MoveDbWithJournals(legacyDb, targetDb);
            }
        }

        return false;
    }

    private static bool MoveDbWithJournals(string sourceDbPath, string targetDbPath)
    {
        try
        {
            SqliteConnection.ClearAllPools();

            File.Move(sourceDbPath, targetDbPath, overwrite: true);

            var sourceWal = sourceDbPath + "-wal";
            var targetWal = targetDbPath + "-wal";
            if (File.Exists(sourceWal))
            {
                File.Move(sourceWal, targetWal, overwrite: true);
            }

            var sourceShm = sourceDbPath + "-shm";
            var targetShm = targetDbPath + "-shm";
            if (File.Exists(sourceShm))
            {
                File.Move(sourceShm, targetShm, overwrite: true);
            }

            return true;
        }
        catch
        {
            return false;
        }
    }

    private static string HashPath(string path) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(path)))[..16];

    private static void AppendGitIgnore(string repoPath)
    {
        var gitIgnorePath = Path.Combine(repoPath, ".gitignore");
        const string entry = "\n# CodeAtlas local index & settings\n.codeatlas/\n";

        if (!File.Exists(gitIgnorePath))
        {
            File.WriteAllText(gitIgnorePath, entry);
            return;
        }

        var content = File.ReadAllText(gitIgnorePath);
        if (!content.Contains(".codeatlas/"))
            File.AppendAllText(gitIgnorePath, entry);
    }
}
