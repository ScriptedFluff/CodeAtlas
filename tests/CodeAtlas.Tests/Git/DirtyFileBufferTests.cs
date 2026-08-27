using System.Diagnostics;
using CodeAtlas.Core.Git;
using CodeAtlas.Parser;
using Xunit;

namespace CodeAtlas.Tests.Git;

public sealed class DirtyFileBufferTests : IDisposable
{
    private readonly string _tempRepo;
    private readonly bool _hasGit;

    public DirtyFileBufferTests()
    {
        _tempRepo = Path.Combine(Path.GetTempPath(), "cg_dirty_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempRepo);
        _hasGit = RunGit(_tempRepo, "init");
        if (_hasGit)
        {
            RunGit(_tempRepo, "config user.email test@test.com");
            RunGit(_tempRepo, "config user.name Test");
        }
    }

    [Fact]
    public void Refresh_UncommittedFile_ReturnsSymbols()
    {
        if (!_hasGit) return;

        File.WriteAllText(
            Path.Combine(_tempRepo, "Live.cs"),
            "namespace A; public class LiveClass { public void LiveMethod() {} }");

        var buffer = new DirtyFileBuffer(_tempRepo, [new CSharpParser()]);
        buffer.Refresh();

        var result = buffer.FindByName("LiveClass");
        Assert.NotNull(result);
        Assert.Single(result);
    }

    [Fact]
    public void Refresh_CommittedFile_NotTracked()
    {
        if (!_hasGit) return;

        File.WriteAllText(Path.Combine(_tempRepo, "Committed.cs"), "namespace A; public class Committed {}");
        RunGit(_tempRepo, "add -A");
        RunGit(_tempRepo, "commit -m init");

        var buffer = new DirtyFileBuffer(_tempRepo, [new CSharpParser()]);
        buffer.Refresh();

        Assert.Equal(0, buffer.DirtyFileCount);
    }

    [Fact]
    public void FindByName_ReturnsNull_WhenNoDirtyFiles()
    {
        var buffer = new DirtyFileBuffer(_tempRepo, [new CSharpParser()]);
        buffer.Refresh();
        Assert.Null(buffer.FindByName("Anything"));
    }

    private static bool RunGit(string cwd, string args)
    {
        try
        {
            var psi = new ProcessStartInfo("git", args)
            {
                WorkingDirectory       = cwd,
                RedirectStandardOutput = true,
                RedirectStandardError  = true,
                UseShellExecute        = false,
                CreateNoWindow         = true
            };
            using var p = Process.Start(psi);
            p?.WaitForExit();
            return p?.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempRepo))
                Directory.Delete(_tempRepo, recursive: true);
        }
        catch { }
    }
}
