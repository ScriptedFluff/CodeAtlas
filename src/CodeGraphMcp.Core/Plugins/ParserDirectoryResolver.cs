namespace CodeGraphMcp.Plugins;

/// <summary>
/// Identifies the tier / source origin of a parser directory.
/// </summary>
public enum ParserSourceTier
{
    Environment,
    User,
    App
}

/// <summary>
/// Multi-tier directory resolver for discovering active ('Parsers/') and inactive ('DisabledParsers/') parser folders.
/// </summary>
public sealed class ParserDirectoryResolver
{
    public const string ParsersFolderName = "Parsers";
    public const string DisabledParsersFolderName = "DisabledParsers";
    public const string EnvVarParsersDir = "CODEGRAPH_PARSERS_DIR";

    private readonly string? _customEnvDir;
    private readonly string? _customUserDir;
    private readonly string? _customAppDir;

    public ParserDirectoryResolver(
        string? customEnvDir = null,
        string? customUserDir = null,
        string? customAppDir = null)
    {
        _customEnvDir = customEnvDir;
        _customUserDir = customUserDir;
        _customAppDir = customAppDir;
    }

    /// <summary>
    /// Gets the Environment tier Parsers directory (Priority 1), if set.
    /// </summary>
    public string? GetEnvParsersDirectory()
    {
        var envPath = _customEnvDir ?? Environment.GetEnvironmentVariable(EnvVarParsersDir);
        return string.IsNullOrWhiteSpace(envPath) ? null : Path.GetFullPath(envPath);
    }

    /// <summary>
    /// Gets the Environment tier DisabledParsers directory, if environment path is set.
    /// </summary>
    public string? GetEnvDisabledParsersDirectory()
    {
        var envDir = GetEnvParsersDirectory();
        if (string.IsNullOrWhiteSpace(envDir)) return null;

        var parent = Directory.GetParent(envDir)?.FullName;
        return parent != null ? Path.Combine(parent, DisabledParsersFolderName) : null;
    }

    /// <summary>
    /// Gets the Primary User tier Parsers directory (Priority 2, unprivileged user profile).
    /// </summary>
    public string GetUserParsersDirectory()
    {
        if (!string.IsNullOrWhiteSpace(_customUserDir))
            return Path.GetFullPath(_customUserDir);

        string basePath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(basePath))
        {
            basePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share");
        }

        return Path.Combine(basePath, "CodeGraphMcp", ParsersFolderName);
    }

    /// <summary>
    /// Gets the Primary User tier DisabledParsers directory.
    /// </summary>
    public string GetUserDisabledParsersDirectory()
    {
        var userParsers = GetUserParsersDirectory();
        var parent = Directory.GetParent(userParsers)?.FullName;
        return parent != null
            ? Path.Combine(parent, DisabledParsersFolderName)
            : Path.Combine(userParsers, "..", DisabledParsersFolderName);
    }

    /// <summary>
    /// Gets the Fallback Application base directory Parsers directory (Priority 3).
    /// </summary>
    public string GetAppParsersDirectory()
    {
        if (!string.IsNullOrWhiteSpace(_customAppDir))
            return Path.GetFullPath(_customAppDir);

        return Path.Combine(AppContext.BaseDirectory, ParsersFolderName);
    }

    /// <summary>
    /// Gets the Fallback Application base directory DisabledParsers directory.
    /// </summary>
    public string GetAppDisabledParsersDirectory()
    {
        var appParsers = GetAppParsersDirectory();
        var parent = Directory.GetParent(appParsers)?.FullName;
        return parent != null
            ? Path.Combine(parent, DisabledParsersFolderName)
            : Path.Combine(appParsers, "..", DisabledParsersFolderName);
    }

    /// <summary>
    /// Returns all active parser search directories in prioritized order (Env -> User -> App).
    /// </summary>
    public IReadOnlyList<(ParserSourceTier Tier, string Path)> GetActiveSearchDirectories()
    {
        var list = new List<(ParserSourceTier, string)>();

        var envDir = GetEnvParsersDirectory();
        if (!string.IsNullOrWhiteSpace(envDir) && Directory.Exists(envDir))
        {
            list.Add((ParserSourceTier.Environment, envDir));
        }

        var userDir = GetUserParsersDirectory();
        if (Directory.Exists(userDir))
        {
            list.Add((ParserSourceTier.User, userDir));
        }

        var appDir = GetAppParsersDirectory();
        if (Directory.Exists(appDir))
        {
            list.Add((ParserSourceTier.App, appDir));
        }

        return list;
    }

    /// <summary>
    /// Returns all disabled parser search directories in prioritized order.
    /// </summary>
    public IReadOnlyList<(ParserSourceTier Tier, string Path)> GetDisabledSearchDirectories()
    {
        var list = new List<(ParserSourceTier, string)>();

        var envDisabled = GetEnvDisabledParsersDirectory();
        if (!string.IsNullOrWhiteSpace(envDisabled) && Directory.Exists(envDisabled))
        {
            list.Add((ParserSourceTier.Environment, envDisabled));
        }

        var userDisabled = GetUserDisabledParsersDirectory();
        if (Directory.Exists(userDisabled))
        {
            list.Add((ParserSourceTier.User, userDisabled));
        }

        var appDisabled = GetAppDisabledParsersDirectory();
        if (Directory.Exists(appDisabled))
        {
            list.Add((ParserSourceTier.App, appDisabled));
        }

        return list;
    }

    /// <summary>
    /// Checks whether the current process has write permissions to the specified directory.
    /// </summary>
    public static bool IsDirectoryWritable(string directoryPath)
    {
        if (string.IsNullOrWhiteSpace(directoryPath)) return false;

        try
        {
            if (!Directory.Exists(directoryPath))
            {
                Directory.CreateDirectory(directoryPath);
            }

            var testFile = Path.Combine(directoryPath, $".write_test_{Guid.NewGuid():N}.tmp");
            File.WriteAllText(testFile, "test");
            File.Delete(testFile);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
