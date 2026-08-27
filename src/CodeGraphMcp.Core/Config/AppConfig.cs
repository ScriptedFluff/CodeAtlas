using System.Text.Json;

namespace CodeGraphMcp.Config;

public sealed class AppConfig
{
    public List<string> IgnorePatterns { get; set; } = [
        "package-lock.json",
        "pnpm-lock.yaml",
        "yarn.lock",
        "*.min.json",
        "*.min.js",
        "*.min.css",
        "*.map",
        "**/wwwroot/lib/**",
        "**/wwwroot/lib/*"
    ];

    public List<string> IgnoreDirectories { get; set; } = [
        ".git", ".codegraph", "bin", "obj",
        "node_modules", "dist", ".next", "coverage", ".vs",
        "packages", "TestResults", "publish", "out", "temp", "tmp", "vendor", ".idea",
        "lib", "wwwroot/lib"
    ];

    public List<string> IncludePatterns { get; set; } = [];

    public bool WildcardIgnoreAll { get; private set; }

    public static AppConfig Load(string? repoPath = null)
    {
        var config = new AppConfig();

        // 1. Try local appsettings.json in current executable / AppDomain directory
        var exeDir = AppDomain.CurrentDomain.BaseDirectory;
        var exeConfigPath = Path.Combine(exeDir, "appsettings.json");
        MergeConfigFile(config, exeConfigPath);

        // 2. Try repository settings at .codegraph/settings.json or appsettings.json if present
        if (!string.IsNullOrWhiteSpace(repoPath) && Directory.Exists(repoPath))
        {
            var configFiles = new[]
            {
                Path.Combine(repoPath, ".codegraph", "settings.json"),
                Path.Combine(repoPath, "appsettings.json")
            };

            foreach (var cfg in configFiles)
            {
                MergeConfigFile(config, cfg);
            }
        }

        // Post-process IncludePatterns: if empty or contains "*", treat as no include filter (include all)
        if (config.IncludePatterns.Count == 0 || config.IncludePatterns.Any(p => p.Trim() == "*"))
        {
            config.IncludePatterns.Clear();
        }

        // Post-process IgnorePatterns: check if user configured a wildcard ignore "*" or "**"
        if (config.IgnorePatterns.Any(p => p.Trim() == "*" || p.Trim() == "**"))
        {
            config.WildcardIgnoreAll = true;
        }

        return config;
    }

    private static void MergeConfigFile(AppConfig config, string filePath)
    {
        if (!File.Exists(filePath)) return;

        try
        {
            var json = File.ReadAllText(filePath);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            // 1. Check top-level "Include", "Ignore" or "IgnorePatterns"
            if ((root.TryGetProperty("Include", out var topInclude) || root.TryGetProperty("include", out topInclude)) && topInclude.ValueKind == JsonValueKind.Array)
            {
                AddPatterns(config.IncludePatterns, topInclude);
            }

            if ((root.TryGetProperty("Ignore", out var topIgnore) || root.TryGetProperty("ignore", out topIgnore)) && topIgnore.ValueKind == JsonValueKind.Array)
            {
                AddPatterns(config.IgnorePatterns, topIgnore);
            }
            else if ((root.TryGetProperty("IgnorePatterns", out var topPatterns) || root.TryGetProperty("ignorePatterns", out topPatterns)) && topPatterns.ValueKind == JsonValueKind.Array)
            {
                AddPatterns(config.IgnorePatterns, topPatterns);
            }

            if ((root.TryGetProperty("IgnoreDirectories", out var topDirs) || root.TryGetProperty("ignoreDirectories", out topDirs)) && topDirs.ValueKind == JsonValueKind.Array)
            {
                AddPatterns(config.IgnoreDirectories, topDirs);
            }

            // 2. Check nested "CodeGraph" section (e.g. appsettings.json style)
            if (root.TryGetProperty("CodeGraph", out var section) || root.TryGetProperty("codeGraph", out section))
            {
                if ((section.TryGetProperty("Include", out var secInclude) || section.TryGetProperty("include", out secInclude)) && secInclude.ValueKind == JsonValueKind.Array)
                {
                    AddPatterns(config.IncludePatterns, secInclude);
                }
                if ((section.TryGetProperty("Ignore", out var secIgnore) || section.TryGetProperty("ignore", out secIgnore)) && secIgnore.ValueKind == JsonValueKind.Array)
                {
                    AddPatterns(config.IgnorePatterns, secIgnore);
                }
                if ((section.TryGetProperty("IgnorePatterns", out var secPatterns) || section.TryGetProperty("ignorePatterns", out secPatterns)) && secPatterns.ValueKind == JsonValueKind.Array)
                {
                    AddPatterns(config.IgnorePatterns, secPatterns);
                }
                if ((section.TryGetProperty("IgnoreDirectories", out var secDirs) || section.TryGetProperty("ignoreDirectories", out secDirs)) && secDirs.ValueKind == JsonValueKind.Array)
                {
                    AddPatterns(config.IgnoreDirectories, secDirs);
                }
            }
        }
        catch
        {
            // Ignore configuration parse errors and fall back to default rules
        }
    }

    private static void AddPatterns(List<string> targetList, JsonElement jsonArray)
    {
        foreach (var item in jsonArray.EnumerateArray())
        {
            var val = item.GetString();
            if (!string.IsNullOrWhiteSpace(val) && !targetList.Contains(val, StringComparer.OrdinalIgnoreCase))
            {
                targetList.Add(val);
            }
        }
    }
}
