using CodeAtlas.Core.Db;
using CodeAtlas.Core.Indexer;
using CodeAtlas.Models;
using Xunit;

namespace CodeAtlas.Tests;

public class GlobFileIndexerTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _dbPath;

    public GlobFileIndexerTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"codegraph_glob_test_{Guid.NewGuid():N}");
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
    public void IsGlobPattern_IdentifiesWildcardsCorrectly()
    {
        Assert.True(GlobFileIndexer.IsGlobPattern("src/**/*.cs"));
        Assert.True(GlobFileIndexer.IsGlobPattern("*.ts"));
        Assert.True(GlobFileIndexer.IsGlobPattern("src/Test?.cs"));
        Assert.True(GlobFileIndexer.IsGlobPattern("src/[a-z]/*.cs"));
        Assert.False(GlobFileIndexer.IsGlobPattern("src/Services/UserService.cs"));
        Assert.False(GlobFileIndexer.IsGlobPattern("Program.cs"));
        Assert.False(GlobFileIndexer.IsGlobPattern(""));
    }

    [Fact]
    public async Task SyncGlobAsync_UpsertsAllMatchingFiles()
    {
        var dir1 = Path.Combine(_tempDir, "src", "Services");
        var dir2 = Path.Combine(_tempDir, "src", "Models");
        Directory.CreateDirectory(dir1);
        Directory.CreateDirectory(dir2);

        File.WriteAllText(Path.Combine(dir1, "UserService.cs"), "namespace App.Services; public class UserService { public void GetUser() {} }");
        File.WriteAllText(Path.Combine(dir2, "UserModel.cs"), "namespace App.Models; public class UserModel { public string Name { get; set; } }");
        File.WriteAllText(Path.Combine(_tempDir, "ignored.txt"), "some text");

        using var db = new CodeAtlasDb(_dbPath);
        db.Migrate();

        var indexer = new GlobFileIndexer();
        var result = await indexer.SyncGlobAsync(_tempDir, db, "src/**/*.cs");

        Assert.Equal(2, result.FilesProcessed);
        Assert.True(result.SymbolsAdded >= 2);

        var userServices = db.Symbols.FindByName("UserService");
        Assert.Single(userServices);
        var userModels = db.Symbols.FindByName("UserModel");
        Assert.Single(userModels);
    }

    [Fact]
    public async Task SyncGlobAsync_ReconcilesAndPurgesDeletedFiles()
    {
        var dir = Path.Combine(_tempDir, "src", "Controllers");
        Directory.CreateDirectory(dir);

        var file1 = Path.Combine(dir, "AuthController.cs");
        var file2 = Path.Combine(dir, "HomeController.cs");
        File.WriteAllText(file1, "public class AuthController { public void Login() {} }");
        File.WriteAllText(file2, "public class HomeController { public void Index() {} }");

        using var db = new CodeAtlasDb(_dbPath);
        db.Migrate();

        var indexer = new GlobFileIndexer();
        // Initial sync of both controllers
        await indexer.SyncGlobAsync(_tempDir, db, "src/Controllers/*.cs");

        Assert.Single(db.Symbols.FindByName("AuthController"));
        Assert.Single(db.Symbols.FindByName("HomeController"));

        // Delete AuthController from disk
        File.Delete(file1);

        // Re-sync glob
        var reSyncResult = await indexer.SyncGlobAsync(_tempDir, db, "src/Controllers/*.cs");

        Assert.Equal(1, reSyncResult.FilesProcessed);
        Assert.Equal(1, reSyncResult.FilesDeleted);

        // Verify AuthController is purged from DB while HomeController remains
        Assert.Empty(db.Symbols.FindByName("AuthController"));
        Assert.Single(db.Symbols.FindByName("HomeController"));
    }

    [Fact]
    public async Task PurgeGlobAsync_RemovesMatchingSymbolsAndReferences()
    {
        var testDir = Path.Combine(_tempDir, "tests", "fixtures");
        var srcDir = Path.Combine(_tempDir, "src");
        Directory.CreateDirectory(testDir);
        Directory.CreateDirectory(srcDir);

        File.WriteAllText(Path.Combine(testDir, "MockFixture.cs"), "public class MockFixture {}");
        File.WriteAllText(Path.Combine(srcDir, "RealService.cs"), "public class RealService {}");

        using var db = new CodeAtlasDb(_dbPath);
        db.Migrate();

        var indexer = new GlobFileIndexer();
        await indexer.SyncGlobAsync(_tempDir, db, "**/*.cs");

        Assert.Single(db.Symbols.FindByName("MockFixture"));
        Assert.Single(db.Symbols.FindByName("RealService"));

        // Purge fixtures glob
        var purgeResult = await indexer.PurgeGlobAsync(_tempDir, db, "tests/fixtures/**");

        Assert.Equal(1, purgeResult.FilesDeleted);
        Assert.True(purgeResult.SymbolsDeleted >= 1);

        // Assert MockFixture is removed, RealService is preserved
        Assert.Empty(db.Symbols.FindByName("MockFixture"));
        Assert.Single(db.Symbols.FindByName("RealService"));
    }
}
