using CodeAtlas.Core.Config;
using Xunit;

namespace CodeAtlas.Tests;

/// <summary>
/// Verifies temporary backward-compatibility migration of legacy codegraph.db / .codegraph/ caches.
/// Scheduled for removal alongside StorageConfig legacy migration methods in a future release.
/// </summary>
[Obsolete("Temporary backward-compatibility migration tests. Scheduled for removal in a future release.")]
public class StorageConfigMigrationTests : IDisposable
{
    private readonly string _tempDir;

    public StorageConfigMigrationTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"codeatlas_migration_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDir))
            {
                Directory.Delete(_tempDir, true);
            }
        }
        catch { }
    }

    [Fact]
    public void ResolveDbPath_RenamesCodegraphDb_ToAtlasDb()
    {
        // Arrange: Create .codeatlas/codegraph.db with dummy content
        var codeatlasDir = Path.Combine(_tempDir, ".codeatlas");
        Directory.CreateDirectory(codeatlasDir);
        var oldDb = Path.Combine(codeatlasDir, "codegraph.db");
        var oldWal = Path.Combine(codeatlasDir, "codegraph.db-wal");
        File.WriteAllText(oldDb, "dummy-db-content");
        File.WriteAllText(oldWal, "dummy-wal-content");

        // Act
        var resolvedPath = StorageConfig.ResolveDbPath(null, _tempDir);

        // Assert
        var expectedAtlasDb = Path.Combine(codeatlasDir, "atlas.db");
        var expectedAtlasWal = Path.Combine(codeatlasDir, "atlas.db-wal");

        Assert.Equal(expectedAtlasDb.ToLowerInvariant(), resolvedPath.ToLowerInvariant());
        Assert.True(File.Exists(expectedAtlasDb));
        Assert.True(File.Exists(expectedAtlasWal));
        Assert.False(File.Exists(oldDb));
        Assert.False(File.Exists(oldWal));
        Assert.Equal("dummy-db-content", File.ReadAllText(expectedAtlasDb));
        Assert.Equal("dummy-wal-content", File.ReadAllText(expectedAtlasWal));
    }

    [Fact]
    public void ResolveDbPath_MigratesLegacyCodegraphDirectory_ToCodeatlas()
    {
        // Arrange: Create .codegraph/codegraph.db
        var codegraphDir = Path.Combine(_tempDir, ".codegraph");
        Directory.CreateDirectory(codegraphDir);
        var oldDb = Path.Combine(codegraphDir, "codegraph.db");
        File.WriteAllText(oldDb, "legacy-db-content");

        // Act
        var resolvedPath = StorageConfig.ResolveDbPath(null, _tempDir);

        // Assert
        var expectedAtlasDb = Path.Combine(_tempDir, ".codeatlas", "atlas.db");
        Assert.Equal(expectedAtlasDb.ToLowerInvariant(), resolvedPath.ToLowerInvariant());
        Assert.True(File.Exists(expectedAtlasDb));
        Assert.Equal("legacy-db-content", File.ReadAllText(expectedAtlasDb));
    }

    [Fact]
    public void ResolveDbPath_RenamesCodeGraphUnderscoreDb_ToAtlasDb()
    {
        // Arrange: Create .codeatlas/code_graph.db
        var codeatlasDir = Path.Combine(_tempDir, ".codeatlas");
        Directory.CreateDirectory(codeatlasDir);
        var oldDb = Path.Combine(codeatlasDir, "code_graph.db");
        File.WriteAllText(oldDb, "underscore-db-content");

        // Act
        var resolvedPath = StorageConfig.ResolveDbPath(null, _tempDir);

        // Assert
        var expectedAtlasDb = Path.Combine(codeatlasDir, "atlas.db");
        Assert.Equal(expectedAtlasDb.ToLowerInvariant(), resolvedPath.ToLowerInvariant());
        Assert.True(File.Exists(expectedAtlasDb));
        Assert.False(File.Exists(oldDb));
        Assert.Equal("underscore-db-content", File.ReadAllText(expectedAtlasDb));
    }
}
