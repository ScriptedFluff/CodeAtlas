using System.Security.Cryptography;
using System.Text;
using CodeGraphMcp.Git;

namespace CodeGraphMcp.Config;

public static class StorageConfig
{
    /// <summary>
    /// Resolves the SQLite database file path using a strict priority chain:
    ///   1. --db-dir &lt;path&gt; (explicit override)
    ///   2. --store-local  (.agent-index/ inside the repo)
    ///   3. Default: %LOCALAPPDATA%\CodeGraph\dbs\&lt;hash&gt;.db
    /// </summary>
    public static string ResolveDbPath(string? dbDir, bool storeLocal, string repoPath)
    {
        // Normalise so the hash is stable regardless of trailing slashes or letter casing
        var normalisedRepo = Path.GetFullPath(repoPath)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .ToLowerInvariant();


        if (dbDir is not null)
        {
            Directory.CreateDirectory(dbDir);
            return Path.Combine(dbDir, $"{HashPath(normalisedRepo)}.db");
        }

        var localDbPath = Path.Combine(normalisedRepo, ".agent-index", "code_graph.db");

        // Check if existing local DB file exists in target directory
        if (File.Exists(localDbPath))
            return localDbPath;

        // Check if existing local DB file exists in parent git root (if repoPath is a subdirectory)
        var gitRoot = GitHelpers.FindGitRoot(normalisedRepo);
        if (gitRoot is not null && !string.Equals(gitRoot, normalisedRepo, StringComparison.OrdinalIgnoreCase))
        {
            var gitRootLocalDbPath = Path.Combine(gitRoot, ".agent-index", "code_graph.db");
            if (File.Exists(gitRootLocalDbPath))
                return gitRootLocalDbPath;
        }

        // Create local repo DB if explicitly requested via --store-local
        if (storeLocal)
        {
            var localDir = Path.Combine(normalisedRepo, ".agent-index");
            Directory.CreateDirectory(localDir);
            AppendGitIgnore(normalisedRepo);
            return localDbPath;
        }

        // Default: global LOCALAPPDATA store
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var globalDir = Path.Combine(appData, "CodeGraph", "dbs");
        Directory.CreateDirectory(globalDir);
        return Path.Combine(globalDir, $"{HashPath(normalisedRepo)}.db");
    }

    private static string HashPath(string path) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(path)))[..16];

    private static void AppendGitIgnore(string repoPath)
    {
        var gitIgnorePath = Path.Combine(repoPath, ".gitignore");
        const string entry = "\n# CodeGraphMcp local index\n.agent-index/\n";

        if (!File.Exists(gitIgnorePath))
        {
            File.WriteAllText(gitIgnorePath, entry);
            return;
        }

        var content = File.ReadAllText(gitIgnorePath);
        if (!content.Contains(".agent-index/"))
            File.AppendAllText(gitIgnorePath, entry);
    }
}
