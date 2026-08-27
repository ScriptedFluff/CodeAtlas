using CodeAtlas.Core.Git;

namespace CodeAtlas.Core.Config;

public record WorkspaceInfo(string TargetDirectory, string WorkspaceRoot, string DbPath);

public static class WorkspacePathResolver
{
    private static readonly string[] BoundaryFiles =
    [
        "package.json",
        "tsconfig.json"
    ];

    public static string NormalizePath(string rawPath)
    {
        if (string.IsNullOrWhiteSpace(rawPath))
            return Environment.CurrentDirectory;

        var cleaned = rawPath.Trim().Trim('"', '\'', ' ', '\t');
        while (cleaned.Length > 0 && (cleaned[^1] == '"' || cleaned[^1] == '\''))
        {
            cleaned = cleaned[..^1].Trim();
        }

        return Path.GetFullPath(cleaned).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }


    public static WorkspaceInfo ResolveWorkspace(string? targetDirectory, string? dbDir = null)
    {
        var target = NormalizePath(targetDirectory ?? Environment.CurrentDirectory);
        if (!Directory.Exists(target) && File.Exists(target))
        {
            target = Path.GetDirectoryName(target) ?? target;
        }

        var workspaceRoot = FindWorkspaceRoot(target);
        var dbPath = StorageConfig.ResolveDbPath(dbDir, workspaceRoot);

        return new WorkspaceInfo(target, workspaceRoot, dbPath);
    }

    public static string FindWorkspaceRoot(string startDirectory)
    {
        var current = NormalizePath(startDirectory);
        var highestMatch = current;

        while (!string.IsNullOrEmpty(current))
        {
            if (HasBoundaryMarker(current))
            {
                highestMatch = current;
                // Keep walking up to find top-level solution or git root if nested
                var parent = Path.GetDirectoryName(current);
                if (parent is null || string.Equals(parent, current, StringComparison.OrdinalIgnoreCase))
                    break;

                current = parent;
                continue;
            }

            var nextParent = Path.GetDirectoryName(current);
            if (nextParent is null || string.Equals(nextParent, current, StringComparison.OrdinalIgnoreCase))
                break;

            current = nextParent;
        }

        return highestMatch;
    }

    private static bool HasBoundaryMarker(string dir)
    {
        if (!Directory.Exists(dir)) return false;

        // Check for .codeatlas folder
        if (Directory.Exists(Path.Combine(dir, ".codeatlas")))
            return true;

        // Check for .git folder or file (git worktree)
        if (Directory.Exists(Path.Combine(dir, ".git")) || File.Exists(Path.Combine(dir, ".git")))
            return true;

        // Check for .sln or .slnx files
        try
        {
            if (Directory.EnumerateFiles(dir, "*.sln").Any() || Directory.EnumerateFiles(dir, "*.slnx").Any())
                return true;
        }
        catch
        {
            // Ignore permission exceptions during enumeration
        }

        // Check for explicit boundary files (package.json, tsconfig.json)
        foreach (var file in BoundaryFiles)
        {
            if (File.Exists(Path.Combine(dir, file)))
                return true;
        }

        // Check for .csproj files
        try
        {
            if (Directory.EnumerateFiles(dir, "*.csproj").Any())
                return true;
        }
        catch
        {
            // Ignore permission exceptions
        }

        return false;
    }
}
