using System.Text.RegularExpressions;

namespace CodeGraphMcp.Git;

/// <summary>
/// Native AOT compatible .gitignore pattern matcher supporting standard gitignore rules:
/// - Comments (#) and blank lines
/// - Negation (!)
/// - Directory anchoring (leading /) and directory target matching (trailing /)
/// - Wildcards: *, **, ?
/// - Multi-level nested .gitignore files
/// </summary>
public sealed class GitIgnoreMatcher
{
    private readonly string _rootPath;
    private readonly List<IgnoreRule> _rules = [];
    private readonly HashSet<string> _loadedDirs = new(StringComparer.OrdinalIgnoreCase);

    public bool NoIgnore { get; set; }

    public GitIgnoreMatcher(string rootPath, bool noIgnore = false)
    {
        _rootPath = Path.GetFullPath(rootPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        NoIgnore = noIgnore;
        if (!NoIgnore)
        {
            LoadGitIgnore(_rootPath);
        }
    }

    /// <summary>
    /// Loads a .gitignore file at the given directory path relative to or within root.
    /// </summary>
    public void LoadGitIgnore(string dirPath)
    {
        if (NoIgnore) return;

        var fullDirPath = Path.GetFullPath(dirPath);
        if (!_loadedDirs.Add(fullDirPath)) return;

        var ignoreFile = Path.Combine(fullDirPath, ".gitignore");
        if (!File.Exists(ignoreFile)) return;

        string relativeDir = fullDirPath.Equals(_rootPath, StringComparison.OrdinalIgnoreCase)
            ? ""
            : Path.GetRelativePath(_rootPath, fullDirPath).Replace('\\', '/').Trim('/');

        foreach (var rawLine in File.ReadAllLines(ignoreFile))
        {
            var line = rawLine.Trim();
            if (string.IsNullOrWhiteSpace(line) || line.StartsWith('#'))
                continue;

            var rule = IgnoreRule.Parse(line, relativeDir);
            if (rule is not null)
            {
                _rules.Add(rule);
            }
        }
    }

    /// <summary>
    /// Determines whether the specified relative or absolute file/directory path is ignored.
    /// </summary>
    public bool IsIgnored(string path, bool isDirectory = false)
    {
        if (NoIgnore || _rules.Count == 0) return false;

        var fullPath = Path.GetFullPath(Path.IsPathRooted(path) ? path : Path.Combine(_rootPath, path));
        if (!fullPath.StartsWith(_rootPath, StringComparison.OrdinalIgnoreCase))
            return false;

        var relPath = Path.GetRelativePath(_rootPath, fullPath).Replace('\\', '/');
        if (string.IsNullOrEmpty(relPath) || relPath == ".")
            return false;

        bool ignored = false;
        foreach (var rule in _rules)
        {
            if (rule.IsMatch(relPath, isDirectory))
            {
                ignored = !rule.IsNegated;
            }
        }
        return ignored;
    }

    internal sealed class IgnoreRule
    {
        public bool IsNegated { get; }
        public bool DirectoryOnly { get; }
        public string BaseRelativeDir { get; }
        private readonly Regex _regex;

        private IgnoreRule(bool isNegated, bool directoryOnly, string baseRelativeDir, Regex regex)
        {
            IsNegated = isNegated;
            DirectoryOnly = directoryOnly;
            BaseRelativeDir = baseRelativeDir;
            _regex = regex;
        }

        public static IgnoreRule? Parse(string line, string baseRelativeDir)
        {
            bool isNegated = false;
            if (line.StartsWith('!'))
            {
                isNegated = true;
                line = line[1..];
            }

            if (string.IsNullOrWhiteSpace(line))
                return null;

            bool directoryOnly = line.EndsWith('/');
            if (directoryOnly)
            {
                line = line[..^1];
            }

            bool anchored = line.StartsWith('/');
            if (anchored)
            {
                line = line[1..];
            }

            string baseDirNorm = baseRelativeDir.Trim('/');
            if (!string.IsNullOrEmpty(baseDirNorm))
            {
                line = $"{baseDirNorm}/{line}";
                anchored = true;
            }

            string pattern = ConvertGlobToRegexPattern(line, anchored);
            var regex = new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

            return new IgnoreRule(isNegated, directoryOnly, baseDirNorm, regex);
        }

        public bool IsMatch(string relPath, bool isDirectory)
        {
            if (!string.IsNullOrEmpty(BaseRelativeDir) && !relPath.StartsWith(BaseRelativeDir, StringComparison.OrdinalIgnoreCase))
                return false;

            return _regex.IsMatch(relPath);
        }

        private static string ConvertGlobToRegexPattern(string glob, bool anchored)
        {
            var sb = new System.Text.StringBuilder();
            sb.Append('^');

            if (!anchored && !glob.Contains('/'))
            {
                sb.Append(@"(?:.*/)?");
            }
            else if (!anchored)
            {
                sb.Append(@"(?:.*/)?");
            }

            int i = 0;
            while (i < glob.Length)
            {
                char c = glob[i];
                if (c == '*' && i + 1 < glob.Length && glob[i + 1] == '*')
                {
                    if (i + 2 < glob.Length && glob[i + 2] == '/')
                    {
                        sb.Append(@"(?:.*/)?");
                        i += 3;
                    }
                    else
                    {
                        sb.Append(@".*");
                        i += 2;
                    }
                }
                else if (c == '*')
                {
                    sb.Append(@"[^/]*");
                    i++;
                }
                else if (c == '?')
                {
                    sb.Append(@"[^/]");
                    i++;
                }
                else
                {
                    sb.Append(Regex.Escape(c.ToString()));
                    i++;
                }
            }

            sb.Append(@"(?:/.*)?$");
            return sb.ToString();
        }
    }
}
