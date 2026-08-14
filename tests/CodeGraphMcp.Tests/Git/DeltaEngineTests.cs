using System.Diagnostics;
using CodeGraphMcp.Db;
using CodeGraphMcp.Git;
using CodeGraphMcp.Parser;
using Xunit;

namespace CodeGraphMcp.Tests.Git;

public sealed class DeltaEngineTests : IDisposable
{
    private readonly string _tempRepo;
    private readonly bool _hasGit;

    public DeltaEngineTests()
    {
        _tempRepo = Path.Combine(Path.GetTempPath(), "cg_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempRepo);
        _hasGit = RunGit(_tempRepo, "init");
        if (_hasGit)
        {
            RunGit(_tempRepo, "config user.email test@test.com");
            RunGit(_tempRepo, "config user.name Test");
            File.WriteAllText(Path.Combine(_tempRepo, ".gitignore"), "*.db\n*.db-*\n.codegraph/\n");
            RunGit(_tempRepo, "add -A");
            RunGit(_tempRepo, "commit -m gitignore");
        }
    }

    [Fact]
    public async Task RunAsync_FirstRun_ReturnsFull()
    {
        if (!_hasGit) return;

        WriteAndCommit("Foo.cs", "namespace A; public class Foo {}");

        using var db = CreateInMemoryDb();
        var engine   = new DeltaEngine(_tempRepo, db, [new CSharpParser()]);
        var result   = await engine.RunAsync();

        Assert.Equal("full", result.Mode);
        Assert.Equal(1, result.FilesReindexed);
        Assert.True(result.SymbolsAdded > 0);
    }

    [Fact]
    public async Task RunAsync_NoChanges_ReturnsNoop()
    {
        if (!_hasGit) return;

        WriteAndCommit("Foo.cs", "namespace A; public class Foo {}");

        using var db = CreateInMemoryDb();
        var engine   = new DeltaEngine(_tempRepo, db, [new CSharpParser()]);
        await engine.RunAsync();

        var result = await engine.RunAsync();
        Assert.Equal("noop", result.Mode);
    }

    [Fact]
    public async Task RunAsync_FileChanged_ReturnsDelta()
    {
        if (!_hasGit) return;

        WriteAndCommit("Foo.cs", "namespace A; public class Foo {}");

        using var db = CreateInMemoryDb();
        var engine   = new DeltaEngine(_tempRepo, db, [new CSharpParser()]);
        await engine.RunAsync();

        WriteAndCommit("Foo.cs", "namespace A; public class Foo { public void Bar() {} }");
        var result = await engine.RunAsync();
        Assert.Equal("delta", result.Mode);
        Assert.Equal(1, result.FilesChanged);
        Assert.Equal(1, result.FilesReindexed);
    }

    [Fact]
    public async Task RunAsync_FileDeleted_SymbolsRemoved()
    {
        if (!_hasGit) return;

        WriteAndCommit("Foo.cs", "namespace A; public class Foo {}");

        using var db = CreateInMemoryDb();
        var engine   = new DeltaEngine(_tempRepo, db, [new CSharpParser()]);
        await engine.RunAsync();

        File.Delete(Path.Combine(_tempRepo, "Foo.cs"));
        RunGit(_tempRepo, "add -A");
        RunGit(_tempRepo, "commit -m del");

        var result = await engine.RunAsync();
        Assert.Equal("delta", result.Mode);
        Assert.Empty(db.Symbols.FindByName("Foo"));
    }

    private void WriteAndCommit(string name, string content)
    {
        File.WriteAllText(Path.Combine(_tempRepo, name), content);
        RunGit(_tempRepo, "add -A");
        RunGit(_tempRepo, $"commit -m \"{name}\"");
    }

    private CodeGraphDb CreateInMemoryDb()
    {
        var db = new CodeGraphDb(Path.Combine(_tempRepo, "test.db"));
        db.Migrate();
        return db;
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
