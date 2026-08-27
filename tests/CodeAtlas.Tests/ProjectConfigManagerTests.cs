using CodeAtlas.Core.Config;
using System.Text.Json.Nodes;
using Xunit;

namespace CodeAtlas.Tests;

public class ProjectConfigManagerTests : IDisposable
{
    private readonly string _tempDir;

    public ProjectConfigManagerTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"codegraph_config_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            try { Directory.Delete(_tempDir, recursive: true); } catch { }
        }
    }

    [Fact]
    public void NormalizePattern_HandlesSlashesAndWhitespace()
    {
        Assert.Equal("tests/fixtures/**", ProjectConfigManager.NormalizePattern("  tests\\fixtures\\**  "));
        Assert.Equal("*.generated.cs", ProjectConfigManager.NormalizePattern("*.generated.cs"));
        Assert.Equal(string.Empty, ProjectConfigManager.NormalizePattern("   "));
        Assert.Equal(string.Empty, ProjectConfigManager.NormalizePattern(null));
    }

    [Fact]
    public void AddPattern_WhenFileDoesNotExist_CreatesDirectoryAndValidJson()
    {
        var result = ProjectConfigManager.AddIgnorePattern(_tempDir, "tests/fixtures/**");

        Assert.True(result.Changed);
        Assert.False(result.AlreadyExisted);
        Assert.Equal("tests/fixtures/**", result.NormalizedPattern);
        Assert.True(File.Exists(result.SettingsPath));

        var patterns = ProjectConfigManager.GetIgnorePatterns(_tempDir);
        Assert.Single(patterns);
        Assert.Equal("tests/fixtures/**", patterns[0]);

        // Verify JSON contents have schema and version
        var text = File.ReadAllText(result.SettingsPath);
        var node = JsonNode.Parse(text) as JsonObject;
        Assert.NotNull(node);
        Assert.Equal(1, (int?)node["version"]);
        Assert.NotNull(node["$schema"]);
    }

    [Fact]
    public void AddPattern_RejectsEmptyPattern()
    {
        var result = ProjectConfigManager.AddIgnorePattern(_tempDir, "   ");

        Assert.False(result.Changed);
        Assert.False(result.AlreadyExisted);
        Assert.Contains("Cannot add empty", result.Message);
    }

    [Fact]
    public void AddPattern_DeduplicatesCaseInsensitively()
    {
        ProjectConfigManager.AddIgnorePattern(_tempDir, "src/Generated/**");
        var result2 = ProjectConfigManager.AddIgnorePattern(_tempDir, "src\\generated\\**");

        Assert.False(result2.Changed);
        Assert.True(result2.AlreadyExisted);

        var patterns = ProjectConfigManager.GetIgnorePatterns(_tempDir);
        Assert.Single(patterns);
        Assert.Equal("src/Generated/**", patterns[0]);
    }

    [Fact]
    public void AddPattern_PreservesExistingCustomSettings()
    {
        var settingsDir = Path.Combine(_tempDir, ".codeatlas");
        Directory.CreateDirectory(settingsDir);
        var settingsPath = Path.Combine(settingsDir, "settings.json");

        File.WriteAllText(settingsPath, @"{
            ""version"": 2,
            ""customPluginSetting"": true,
            ""ignoreDirectories"": [""custom_node_modules""],
            ""ignorePatterns"": [""pre_existing/**""]
        }");

        var result = ProjectConfigManager.AddIgnorePattern(_tempDir, "new_ignore/**");
        Assert.True(result.Changed);

        var text = File.ReadAllText(settingsPath);
        var node = JsonNode.Parse(text) as JsonObject;
        Assert.NotNull(node);
        Assert.Equal(2, (int?)node["version"]);
        Assert.True((bool?)node["customPluginSetting"]);

        var patterns = ProjectConfigManager.GetIgnorePatterns(_tempDir);
        Assert.Equal(2, patterns.Count);
        Assert.Contains("pre_existing/**", patterns);
        Assert.Contains("new_ignore/**", patterns);

        // AppConfig should also parse both correctly
        var appConfig = AppConfig.Load(_tempDir);
        Assert.Contains("custom_node_modules", appConfig.IgnoreDirectories);
        Assert.Contains("pre_existing/**", appConfig.IgnorePatterns);
        Assert.Contains("new_ignore/**", appConfig.IgnorePatterns);
    }

    [Fact]
    public void RemovePattern_ExistingPattern_RemovesAndSaves()
    {
        ProjectConfigManager.AddIgnorePattern(_tempDir, "pattern1/**");
        ProjectConfigManager.AddIgnorePattern(_tempDir, "pattern2/**");

        var removeResult = ProjectConfigManager.RemoveIgnorePattern(_tempDir, "pattern1\\**");
        Assert.True(removeResult.Changed);
        Assert.False(removeResult.NotFound);

        var patterns = ProjectConfigManager.GetIgnorePatterns(_tempDir);
        Assert.Single(patterns);
        Assert.Equal("pattern2/**", patterns[0]);
    }

    [Fact]
    public void RemovePattern_NonExistentPattern_ReturnsNotFound()
    {
        ProjectConfigManager.AddIgnorePattern(_tempDir, "pattern1/**");

        var removeResult = ProjectConfigManager.RemoveIgnorePattern(_tempDir, "nonexistent/**");
        Assert.False(removeResult.Changed);
        Assert.True(removeResult.NotFound);

        var patterns = ProjectConfigManager.GetIgnorePatterns(_tempDir);
        Assert.Single(patterns);
    }

    [Fact]
    public void RemovePattern_WhenFileDoesNotExist_ReturnsNotFound()
    {
        var removeResult = ProjectConfigManager.RemoveIgnorePattern(_tempDir, "pattern1/**");
        Assert.False(removeResult.Changed);
        Assert.True(removeResult.NotFound);
    }
}
