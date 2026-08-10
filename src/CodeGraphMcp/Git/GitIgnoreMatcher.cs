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

    public GitIgnoreMatcher(string rootPath)
    {
        _rootPath = Path.GetFullPath(rootPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        LoadGitIgnore(_rootPath);
    }

    /// <summary>
    /// Loads a .gitignore file at the given directory path relative to or within root.
    /// </summary>
    public void LoadGitIgnore(string dirPath)
    {
        var ignoreFile = Path.Combine(dirPath, ".gitignore");
        if (!File.Exists(ignoreFile)) return;

        var fullDirPath = Path.GetFullPath(dirPath);
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

    private sealed class IgnoreRule
    {
        public bool IsNegated { get; }
        public bool DirectoryOnly { get; }
        private readonly Regex _regex;

        private IgnoreRule(bool isNegated, bool directoryOnly, Regex regex)
        {
            IsNegated = isNegated;
            DirectoryOnly = directoryOnly;
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

            if (!string.IsNullOrEmpty(baseRelativeDir))
            {
                line = $"{baseRelativeDir.Trim('/')}/{line}";
                anchored = true;
            }

            string pattern = ConvertGlobToRegexPattern(line, anchored);
            var regex = new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

            return new IgnoreRule(isNegated, directoryOnly, regex);
        }

        public bool IsMatch(string relPath, bool isDirectory)
        {
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
