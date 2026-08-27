using CodeAtlas.Core.Git;
using Xunit;

namespace CodeAtlas.Tests.Git;

public class GitIgnoreMatcherTests : IDisposable
{
    private readonly string _tempDir;

    public GitIgnoreMatcherTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "GitIgnoreTest_" + Guid.NewGuid().ToString("N"));
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
    public void RespectsRootGitIgnoreRules()
    {
        var gitignoreContent = @"
# Ignore build outputs and temp files
bin/
*.tmp
/config.json
!important.tmp
";
        File.WriteAllText(Path.Combine(_tempDir, ".gitignore"), gitignoreContent);

        var matcher = new GitIgnoreMatcher(_tempDir);

        Assert.True(matcher.IsIgnored("bin/debug/app.dll", isDirectory: false));
        Assert.True(matcher.IsIgnored("bin", isDirectory: true));
        Assert.True(matcher.IsIgnored("test.tmp", isDirectory: false));
        Assert.True(matcher.IsIgnored("config.json", isDirectory: false));
        Assert.False(matcher.IsIgnored("sub/config.json", isDirectory: false));
        Assert.False(matcher.IsIgnored("important.tmp", isDirectory: false));
        Assert.False(matcher.IsIgnored("src/Program.cs", isDirectory: false));
    }

    [Fact]
    public void RespectsNestedGitIgnoreFiles()
    {
        File.WriteAllText(Path.Combine(_tempDir, ".gitignore"), "*.log\n");

        var subDir = Path.Combine(_tempDir, "src", "module");
        Directory.CreateDirectory(subDir);
        File.WriteAllText(Path.Combine(subDir, ".gitignore"), "*.generated.cs\n");

        var matcher = new GitIgnoreMatcher(_tempDir);
        matcher.LoadGitIgnore(subDir);

        Assert.True(matcher.IsIgnored("app.log", isDirectory: false));
        Assert.True(matcher.IsIgnored("src/module/service.generated.cs", isDirectory: false));
        Assert.False(matcher.IsIgnored("src/other/service.generated.cs", isDirectory: false));
        Assert.False(matcher.IsIgnored("src/module/service.cs", isDirectory: false));
    }

    [Fact]
    public void DisablesFilteringWhenNoIgnoreIsTrue()
    {
        File.WriteAllText(Path.Combine(_tempDir, ".gitignore"), "*.log\nbin/\n");
        var matcher = new GitIgnoreMatcher(_tempDir, noIgnore: true);

        Assert.False(matcher.IsIgnored("app.log", isDirectory: false));
        Assert.False(matcher.IsIgnored("bin/debug/app.dll", isDirectory: false));
    }
}
