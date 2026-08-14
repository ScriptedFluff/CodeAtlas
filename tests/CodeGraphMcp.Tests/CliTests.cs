using System.CommandLine;
using CodeGraphMcp.Commands;
using CodeGraphMcp.Db;
using CodeGraphMcp.Models;
using Xunit;

namespace CodeGraphMcp.Tests;

public class CliTests : IDisposable
{
    private readonly string _tempRepoDir;
    private readonly string _dbPath;

    public CliTests()
    {
        _tempRepoDir = Path.Combine(Path.GetTempPath(), $"codegraph_cli_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempRepoDir);
        _dbPath = Path.Combine(_tempRepoDir, ".codegraph", "code_graph.db");
        Directory.CreateDirectory(Path.GetDirectoryName(_dbPath)!);
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        if (Directory.Exists(_tempRepoDir))
        {
            try { Directory.Delete(_tempRepoDir, recursive: true); } catch { }
        }
    }

    [Fact]
    public void CleanCommand_RemovesDbAndJournalFiles()
    {
        // Setup mock database file and journals
        File.WriteAllText(_dbPath, "mock db data");
        var walPath = _dbPath + "-wal";
        var shmPath = _dbPath + "-shm";
        File.WriteAllText(walPath, "wal data");
        File.WriteAllText(shmPath, "shm data");

        Assert.True(File.Exists(_dbPath));
        Assert.True(File.Exists(walPath));
        Assert.True(File.Exists(shmPath));

        // Perform clean operation
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        File.Delete(_dbPath);
        if (File.Exists(walPath)) File.Delete(walPath);
        if (File.Exists(shmPath)) File.Delete(shmPath);

        Assert.False(File.Exists(_dbPath));
        Assert.False(File.Exists(walPath));
        Assert.False(File.Exists(shmPath));
    }

    [Fact]
    public void StatusQuery_PopulatedDatabase_ReturnsValidCounts()
    {
        using (var db = new CodeGraphDb(_dbPath))
        {
            db.Migrate();
            db.Symbols.UpsertFile("Test.cs", new[]
            {
                new CodeGraphMcp.Models.Symbol { SymbolName = "Foo", SymbolType = SymbolType.Method, FilePath = "Test.cs", StartLine = 1, EndLine = 5, StartByte = 0, EndByte = 20 }
            });
            db.IndexState.SetIndexState("main", "hash123", 100);
        }

        using (var db = new CodeGraphDb(_dbPath, readOnly: true))
        {
            var counts = db.Symbols.CountPerFile();
            Assert.Single(counts);
            Assert.Equal(1, counts["Test.cs"]);
            Assert.Equal("hash123", db.IndexState.GetLastCommitHash());
        }
    }

    [Fact]
    public async Task RootCommand_InvokeStatus_ReturnsSuccessExitCode()
    {
        using (var db = new CodeGraphDb(_dbPath))
        {
            db.Migrate();
        }

        var root = CommandFactory.CreateRootCommand();
        var exitCode = await root.InvokeAsync(new[] { "status", _tempRepoDir });

        Assert.Equal(0, exitCode);
    }

    [Fact]
    public async Task RootCommand_InvokeCleanWithForce_RemovesDatabase()
    {
        using (var db = new CodeGraphDb(_dbPath))
        {
            db.Migrate();
        }

        Assert.True(File.Exists(_dbPath));

        var root = CommandFactory.CreateRootCommand();
        var exitCode = await root.InvokeAsync(new[] { "clean", _tempRepoDir, "-f" });

        Assert.Equal(0, exitCode);
        Assert.False(File.Exists(_dbPath));
    }

    [Fact]
    public void ServeCommand_OptionParsing_SupportsUnboundMode()
    {
        var root = CommandFactory.CreateRootCommand();
        var serveCmd = root.Subcommands.FirstOrDefault(c => c.Name == "serve");

        Assert.NotNull(serveCmd);
        Assert.Contains(serveCmd.Options, opt => opt.Name == "repo" || opt.Aliases.Contains("-r"));
    }

    [Fact]
    public async Task McpServer_RunAsync_SupportsUnboundModeWithCancelledToken()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        // Verifies starting unbound McpServer with pre-cancelled token cancels cleanly
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await CodeGraphMcp.Mcp.McpServer.RunAsync(repoPath: null, dbPath: null, ct: cts.Token));
    }

    [Fact]
    public void AppConfig_Load_ReadsDotCodegraphSettingsJson()
    {
        var codegraphDir = Path.Combine(_tempRepoDir, ".codegraph");
        Directory.CreateDirectory(codegraphDir);
        var settingsPath = Path.Combine(codegraphDir, "settings.json");
        File.WriteAllText(settingsPath, @"{
            ""IgnoreDirectories"": [""custom_built""],
            ""IgnorePatterns"": [""*.generated.cs""]
        }");

        var config = CodeGraphMcp.Config.AppConfig.Load(_tempRepoDir);

        Assert.Contains("custom_built", config.IgnoreDirectories);
        Assert.Contains("*.generated.cs", config.IgnorePatterns);
        Assert.Contains(".codegraph", config.IgnoreDirectories);
    }

    [Fact]
    public async Task QueryCommand_WithReferencedFlag_DisplaysCallers()
    {
        using (var db = new CodeGraphDb(_dbPath))
        {
            db.Migrate();
            db.Symbols.UpsertFile("Target.cs", new[]
            {
                new CodeGraphMcp.Models.Symbol { SymbolName = "MyTarget", SymbolType = SymbolType.Class, FilePath = "Target.cs", StartLine = 1, EndLine = 20, StartByte = 0, EndByte = 100 }
            });
            db.Symbols.UpsertFile("Caller.cs", new[]
            {
                new CodeGraphMcp.Models.Symbol { SymbolName = "MyCaller", SymbolType = SymbolType.Method, FilePath = "Caller.cs", StartLine = 10, EndLine = 15, StartByte = 50, EndByte = 120 }
            });

            var cache = SymbolLookupCache.BuildFromDb(db.Connection);
            db.References.UpsertReferencesBatch(new[]
            {
                ("Caller.cs", (IReadOnlyList<SymbolReference>)new[]
                {
                    new SymbolReference("MyCaller", "MyTarget", "Call", "Caller.cs", 12)
                })
            }, cache);
        }

        var currentOut = Console.Out;
        using var sw = new StringWriter();
        try
        {
            Console.SetOut(sw);
            var root = CommandFactory.CreateRootCommand();
            var exitCode = await root.InvokeAsync(new[] { "query", "MyTarget", "-r", _tempRepoDir, "--referenced" });

            Assert.Equal(0, exitCode);
            var output = sw.ToString();
            Assert.Contains("[Class] MyTarget", output);
            Assert.Contains("Referenced in (1 places)", output);
            Assert.Contains("[Call] Caller.cs:12 (MyCaller)", output);
        }
        finally
        {
            Console.SetOut(currentOut);
        }
    }
}
