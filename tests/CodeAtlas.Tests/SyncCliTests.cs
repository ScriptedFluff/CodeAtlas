using System.CommandLine;
using CodeAtlas.Commands;
using CodeAtlas.Core.Db;
using CodeAtlas.Models;
using Xunit;

namespace CodeAtlas.Tests;

[Collection("Sequential")]
public class SyncCliTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _dbPath;

    public SyncCliTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"codegraph_sync_cli_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _dbPath = Path.Combine(_tempDir, ".codeatlas", "atlas.db");
        Directory.CreateDirectory(Path.GetDirectoryName(_dbPath)!);
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        if (Directory.Exists(_tempDir))
        {
            try { Directory.Delete(_tempDir, recursive: true); } catch { }
        }
    }

    [Fact]
    public async Task SyncCommand_WithGlob_IndexesMatchingFiles()
    {
        var servicesDir = Path.Combine(_tempDir, "src", "Services");
        Directory.CreateDirectory(servicesDir);
        File.WriteAllText(Path.Combine(servicesDir, "OrderService.cs"), "public class OrderService { public void PlaceOrder() {} }");
        File.WriteAllText(Path.Combine(servicesDir, "PaymentService.cs"), "public class PaymentService { public void Pay() {} }");

        var root = CommandFactory.CreateRootCommand();
        var exitCode = await root.InvokeAsync(new[] { "sync", "src/Services/**/*.cs", "-r", _tempDir });

        Assert.Equal(0, exitCode);

        using var db = new CodeAtlasDb(_dbPath, readOnly: true);
        Assert.Single(db.Symbols.FindByName("OrderService"));
        Assert.Single(db.Symbols.FindByName("PaymentService"));
    }

    [Fact]
    public async Task SyncCommand_WithDeleteFlag_PurgesGlobFromDatabase()
    {
        var dir = Path.Combine(_tempDir, "src", "Legacy");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "LegacyModule.cs"), "public class LegacyModule {}");

        var root = CommandFactory.CreateRootCommand();
        // 1. Sync
        await root.InvokeAsync(new[] { "sync", "src/Legacy/**/*.cs", "-r", _tempDir });

        using (var db = new CodeAtlasDb(_dbPath, readOnly: true))
        {
            Assert.Single(db.Symbols.FindByName("LegacyModule"));
        }

        // 2. Delete via sync --delete
        var exitCode = await root.InvokeAsync(new[] { "sync", "src/Legacy/**", "-r", _tempDir, "--delete" });
        Assert.Equal(0, exitCode);

        using (var db = new CodeAtlasDb(_dbPath, readOnly: true))
        {
            Assert.Empty(db.Symbols.FindByName("LegacyModule"));
        }
    }

    [Fact]
    public async Task IgnoreAddCommand_WithPurgeFlag_UpdatesSettingsAndPurgesFromDb()
    {
        var fixtureDir = Path.Combine(_tempDir, "tests", "fixtures");
        Directory.CreateDirectory(fixtureDir);
        File.WriteAllText(Path.Combine(fixtureDir, "TempFixture.cs"), "public class TempFixture {}");

        var root = CommandFactory.CreateRootCommand();
        // 1. Index fixture
        await root.InvokeAsync(new[] { "sync", "tests/fixtures/**/*.cs", "-r", _tempDir });

        using (var db = new CodeAtlasDb(_dbPath, readOnly: true))
        {
            Assert.Single(db.Symbols.FindByName("TempFixture"));
        }

        // 2. Ignore add with --purge
        var exitCode = await root.InvokeAsync(new[] { "ignore", "add", "tests/fixtures/**", "-r", _tempDir, "--purge" });
        Assert.Equal(0, exitCode);

        using (var db = new CodeAtlasDb(_dbPath, readOnly: true))
        {
            Assert.Empty(db.Symbols.FindByName("TempFixture"));
        }
    }
}
