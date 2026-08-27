using System.CommandLine;
using System.Text.Json;
using CodeGraphMcp.Commands;
using CodeGraphMcp.Config;
using Xunit;

namespace CodeGraphMcp.Tests;

[Collection("Sequential")]
public class IgnoreCommandTests : IDisposable
{
    private readonly string _tempDir;

    public IgnoreCommandTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"codegraph_ignore_cli_test_{Guid.NewGuid():N}");
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
    public async Task IgnoreAdd_ViaCli_CreatesSettingsAndAddsPattern()
    {
        var root = CommandFactory.CreateRootCommand();
        var exitCode = await root.InvokeAsync(new[] { "ignore", "add", "tests/fixtures/**", "-r", _tempDir });

        Assert.Equal(0, exitCode);

        var patterns = ProjectConfigManager.GetIgnorePatterns(_tempDir);
        Assert.Single(patterns);
        Assert.Equal("tests/fixtures/**", patterns[0]);
    }

    [Fact]
    public async Task IgnoreList_ViaCli_OutputsPatterns()
    {
        ProjectConfigManager.AddIgnorePattern(_tempDir, "tests/fixtures/**");
        ProjectConfigManager.AddIgnorePattern(_tempDir, "*.generated.cs");

        var currentOut = Console.Out;
        using var sw = new StringWriter();
        try
        {
            Console.SetOut(sw);
            var root = CommandFactory.CreateRootCommand();
            var exitCode = await root.InvokeAsync(new[] { "ignore", "list", "-r", _tempDir });

            Assert.Equal(0, exitCode);
            var output = sw.ToString();
            Assert.Contains("Active Ignore Patterns (2):", output);
            Assert.Contains("- tests/fixtures/**", output);
            Assert.Contains("- *.generated.cs", output);
        }
        finally
        {
            Console.SetOut(currentOut);
        }
    }

    [Fact]
    public async Task IgnoreList_WithJsonFlag_OutputsValidJson()
    {
        ProjectConfigManager.AddIgnorePattern(_tempDir, "src/Legacy/**/*.js");

        var currentOut = Console.Out;
        using var sw = new StringWriter();
        try
        {
            Console.SetOut(sw);
            var root = CommandFactory.CreateRootCommand();
            var exitCode = await root.InvokeAsync(new[] { "ignore", "ls", "-r", _tempDir, "--json" });

            Assert.Equal(0, exitCode);
            var output = sw.ToString().Trim();
            
            using var doc = JsonDocument.Parse(output);
            var rootEl = doc.RootElement;
            Assert.True(rootEl.TryGetProperty("ignorePatterns", out var arr) || rootEl.TryGetProperty("IgnorePatterns", out arr));
            Assert.Equal(1, arr.GetArrayLength());
            Assert.Equal("src/Legacy/**/*.js", arr[0].GetString());
        }
        finally
        {
            Console.SetOut(currentOut);
        }
    }

    [Fact]
    public async Task IgnoreRemove_ViaCli_RemovesPattern()
    {
        ProjectConfigManager.AddIgnorePattern(_tempDir, "tests/fixtures/**");
        ProjectConfigManager.AddIgnorePattern(_tempDir, "*.generated.cs");

        var root = CommandFactory.CreateRootCommand();
        var exitCode = await root.InvokeAsync(new[] { "ignore", "remove", "tests/fixtures/**", "-r", _tempDir });

        Assert.Equal(0, exitCode);

        var patterns = ProjectConfigManager.GetIgnorePatterns(_tempDir);
        Assert.Single(patterns);
        Assert.Equal("*.generated.cs", patterns[0]);
    }
}
