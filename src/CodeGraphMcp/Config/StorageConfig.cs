using System.Security.Cryptography;
using System.Text;
using CodeGraphMcp.Git;

namespace CodeGraphMcp.Config;

public static class StorageConfig
{
    /// <summary>
    /// Resolves the SQLite database file path using a strict priority chain:
    ///   1. --db-dir &lt;path&gt; (explicit override)
    ///   2. Default: &lt;repo&gt;/.codegraph/code_graph.db (stored inside target repository)
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
            return Path.Combine(dbDir, $"{HashPath(normalisedRepo)}.db");
        }

        var localDbPath = Path.Combine(normalisedRepo, ".codegraph", "code_graph.db");

        // Check if existing local DB file exists in target directory (.codegraph or legacy .agent-index)
        if (File.Exists(localDbPath))
            return localDbPath;

        var legacyLocalDbPath = Path.Combine(normalisedRepo, ".agent-index", "code_graph.db");
        if (File.Exists(legacyLocalDbPath))
            return legacyLocalDbPath;

        // Check if existing local DB file exists in parent git root (if repoPath is a subdirectory)
        var gitRoot = GitHelpers.FindGitRoot(normalisedRepo);
        if (gitRoot is not null && !string.Equals(gitRoot, normalisedRepo, StringComparison.OrdinalIgnoreCase))
        {
            var gitRootLocalDbPath = Path.Combine(gitRoot, ".codegraph", "code_graph.db");
            if (File.Exists(gitRootLocalDbPath))
                return gitRootLocalDbPath;

            var gitRootLegacyDbPath = Path.Combine(gitRoot, ".agent-index", "code_graph.db");
            if (File.Exists(gitRootLegacyDbPath))
                return gitRootLegacyDbPath;
        }

        // Default: Create local repository database inside .codegraph directory
        var localDir = Path.Combine(normalisedRepo, ".codegraph");
        Directory.CreateDirectory(localDir);
        AppendGitIgnore(normalisedRepo);
        return localDbPath;
    }

    private static string HashPath(string path) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(path)))[..16];

    private static void AppendGitIgnore(string repoPath)
    {
        var gitIgnorePath = Path.Combine(repoPath, ".gitignore");
        const string entry = "\n# CodeGraphMcp local index & settings\n.codegraph/\n";

        if (!File.Exists(gitIgnorePath))
        {
            File.WriteAllText(gitIgnorePath, entry);
            return;
        }

        var content = File.ReadAllText(gitIgnorePath);
        if (!content.Contains(".codegraph/"))
            File.AppendAllText(gitIgnorePath, entry);
    }
}
