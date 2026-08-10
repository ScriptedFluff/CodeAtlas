using CodeGraphMcp.Db;
using Microsoft.Data.Sqlite;
using Xunit;

namespace CodeGraphMcp.Tests;

public class WorkspaceDbManagerTests
{
    [Fact]
    public void ResolveWorkspaceRoot_ValidPath_ReturnsNormalizedPath()
    {
        using var manager = new WorkspaceDbManager();
        var tempDir = Path.GetTempPath();

        var resolved = manager.ResolveWorkspaceRoot(tempDir);

        Assert.NotNull(resolved);
        Assert.True(Path.IsPathRooted(resolved));
    }

    [Fact]
    public void GetOrCreateDb_SameWorkspace_ReturnsSameInstance()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "CodeGraphTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var manager = new WorkspaceDbManager();
        try
        {
            var db1 = manager.GetOrCreateDb(tempDir);
            var db2 = manager.GetOrCreateDb(tempDir);

            Assert.Same(db1, db2);
            Assert.Single(manager.ActiveWorkspaces);
        }
        finally
        {
            manager.Dispose();
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void GetOrCreateDb_DifferentWorkspaces_ReturnsDistinctInstances()
    {
        var tempDir1 = Path.Combine(Path.GetTempPath(), "CodeGraphTest1_" + Guid.NewGuid().ToString("N"));
        var tempDir2 = Path.Combine(Path.GetTempPath(), "CodeGraphTest2_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir1);
        Directory.CreateDirectory(tempDir2);
        var manager = new WorkspaceDbManager();

        try
        {
            var db1 = manager.GetOrCreateDb(tempDir1);
            var db2 = manager.GetOrCreateDb(tempDir2);

            Assert.NotSame(db1, db2);
            Assert.Equal(2, manager.ActiveWorkspaces.Count);
        }
        finally
        {
            manager.Dispose();
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(tempDir1)) Directory.Delete(tempDir1, recursive: true);
            if (Directory.Exists(tempDir2)) Directory.Delete(tempDir2, recursive: true);
        }
    }

    [Fact]
    public void GetOrCreateDb_ConcurrentRequests_ThreadSafe()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "CodeGraphTest_Concurrent_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var manager = new WorkspaceDbManager();

        try
        {
            CodeGraphDb[] instances = new CodeGraphDb[50];
            Parallel.For(0, 50, i =>
            {
                instances[i] = manager.GetOrCreateDb(tempDir);
            });

            var first = instances[0];
            Assert.NotNull(first);
            for (int i = 1; i < instances.Length; i++)
            {
                Assert.Same(first, instances[i]);
            }
        }
        finally
        {
            manager.Dispose();
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void Dispose_FlushesAndClearsWorkspaces()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "CodeGraphTest_Dispose_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            var manager = new WorkspaceDbManager();
            var db = manager.GetOrCreateDb(tempDir);
            Assert.NotNull(db);

            manager.Dispose();
            SqliteConnection.ClearAllPools();

            Assert.Empty(manager.ActiveWorkspaces);
            Assert.Throws<ObjectDisposedException>(() => manager.GetOrCreateDb(tempDir));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
    }
}
