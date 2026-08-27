using System.Diagnostics;

namespace CodeAtlas.Core.Git;

/// <summary>
/// Thin shell-based git helpers.
/// Shelling out guarantees 100% Native AOT compatibility without third-party native DLL dependencies.
/// All methods return null/empty on failure — never throw.
/// </summary>
public static class GitHelpers
{
    public static string? GetHeadCommitHash(string repoPath) =>
        RunGit(repoPath, "rev-parse HEAD")?.Trim();

    public static string? GetCurrentBranch(string repoPath) =>
        RunGit(repoPath, "rev-parse --abbrev-ref HEAD")?.Trim();

    /// <summary>Checks if a commit hash exists in the local git object database.</summary>
    public static bool CommitExists(string repoPath, string commitHash)
    {
        if (string.IsNullOrWhiteSpace(commitHash)) return false;
        var res = RunGit(repoPath, $"cat-file -t {commitHash}");
        return res is not null && string.Equals(res.Trim(), "commit", StringComparison.OrdinalIgnoreCase);
    }


    /// <summary>
    /// Returns relative file paths that changed between sinceCommit and HEAD.
    /// Includes renames, modifications, and deletions.
    /// </summary>
    public static IReadOnlyList<string> GetChangedFiles(string repoPath, string sinceCommit)
    {
        var result = RunGit(repoPath, $"diff --name-only {sinceCommit} HEAD");
        if (result is null) return [];

        return result
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(f => !string.IsNullOrWhiteSpace(f))
            .Select(raw => NormalizePathRelativeToRepo(repoPath, raw))
            .Where(f => !f.StartsWith("../", StringComparison.Ordinal)) // filter out changes outside target repoPath
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// Returns relative paths of files that have uncommitted changes (modified or new in working tree).
    /// Uses `git status --porcelain -uall` to expand directory structures into individual files.
    /// </summary>
    public static IReadOnlyList<string> GetDirtyFiles(string repoPath)
    {
        var result = RunGit(repoPath, "status --porcelain -uall");
        if (result is null) return [];

        return result
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Where(line => line.Length >= 3)
            .Select(line =>
            {
                var path = line[3..].Trim();
                var arrowIdx = path.IndexOf(" -> ", StringComparison.Ordinal);
                return arrowIdx >= 0 ? path[(arrowIdx + 4)..].Trim() : path;
            })
            .Where(f => !string.IsNullOrWhiteSpace(f))
            .Select(raw => NormalizePathRelativeToRepo(repoPath, raw))
            .Where(f => !f.StartsWith("../", StringComparison.Ordinal))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string NormalizePathRelativeToRepo(string repoPath, string rawPath)
    {
        var normRepo = Path.GetFullPath(repoPath).TrimEnd(Path.DirectorySeparatorChar);
        var gitRoot  = FindGitRoot(normRepo);

        // 1. Try direct combine with repoPath
        var pathFromRepo = Path.GetFullPath(Path.Combine(normRepo, rawPath));
        if (File.Exists(pathFromRepo) || Directory.Exists(pathFromRepo))
        {
            return Path.GetRelativePath(normRepo, pathFromRepo).Replace('\\', '/');
        }

        // 2. Try combine with gitRoot if repoPath is a subdirectory
        if (gitRoot is not null)
        {
            var pathFromGitRoot = Path.GetFullPath(Path.Combine(gitRoot, rawPath));
            if (pathFromGitRoot.StartsWith(normRepo, StringComparison.OrdinalIgnoreCase))
            {
                return Path.GetRelativePath(normRepo, pathFromGitRoot).Replace('\\', '/');
            }
        }

        // Fallback: trim leading slashes
        return rawPath.Replace('\\', '/').TrimStart('/');
    }

    /// <summary>True if the given path is inside a git repository.</summary>
    public static bool IsGitRepo(string repoPath) =>
        RunGit(repoPath, "rev-parse --git-dir") is not null;

    /// <summary>Finds the nearest directory containing a .git folder or file by walking upwards.</summary>
    public static string? FindGitRoot(string startPath)
    {
        var dir = new DirectoryInfo(startPath);
        while (dir is not null)
        {
            var gitPath = Path.Combine(dir.FullName, ".git");
            if (Directory.Exists(gitPath) || File.Exists(gitPath))
                return dir.FullName;
            dir = dir.Parent;
        }
        return null;
    }

    private static readonly string GitExecutable = ResolveGitExecutable();

    private static string ResolveGitExecutable()
    {
        try
        {
            using var p = Process.Start(new ProcessStartInfo("git", "--version") { UseShellExecute = false, CreateNoWindow = true });
            p?.WaitForExit(2000);
            if (p?.ExitCode == 0) return "git";
        }
        catch { }

        string[] candidatePaths =
        [
            @"C:\Program Files\Git\cmd\git.exe",
            @"C:\Program Files\Git\bin\git.exe",
            @"C:\Program Files (x86)\Git\cmd\git.exe",
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Programs\Git\cmd\git.exe")
        ];

        foreach (var path in candidatePaths)
        {
            if (File.Exists(path)) return path;
        }

        return "git";
    }

    private static string? RunGit(string cwd, string args)
    {
        try
        {
            var cleanCwd = cwd.Trim().Trim('"');
            if (!Directory.Exists(cleanCwd)) return null;

            var psi = new ProcessStartInfo(GitExecutable, args)
            {
                WorkingDirectory       = cleanCwd,
                RedirectStandardOutput = true,
                RedirectStandardError  = true,
                UseShellExecute        = false,
                CreateNoWindow         = true
            };
            using var p = Process.Start(psi);
            if (p is null) return null;

            var stdoutTask = p.StandardOutput.ReadToEndAsync();
            var stderrTask = p.StandardError.ReadToEndAsync();

            if (!p.WaitForExit(60000))
            {
                try { p.Kill(); } catch { }
                return null;
            }

            Task.WaitAll(stdoutTask, stderrTask);
            return p.ExitCode == 0 ? stdoutTask.Result : null;
        }
        catch
        {
            return null;
        }
    }
}
