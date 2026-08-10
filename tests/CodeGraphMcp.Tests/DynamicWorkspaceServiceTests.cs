using CodeGraphMcp.Config;
using CodeGraphMcp.Db;
using CodeGraphMcp.Indexer;
using Microsoft.Data.Sqlite;
using Xunit;

namespace CodeGraphMcp.Tests;

public class DynamicWorkspaceServiceTests
{
    [Fact]
    public async Task GetOrIndexWorkspaceAsync_UnindexedDirectory_TriggersAutoIndex()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "DynamicTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        File.WriteAllText(Path.Combine(tempDir, "Sample.cs"), "public class AutoIndexedSample { public void Test() {} }");

        using var dbManager = new WorkspaceDbManager();
        var service = new DynamicWorkspaceService(dbManager);

        try
        {
            var context = await service.GetOrIndexWorkspaceAsync(tempDir);
            Assert.NotNull(context);
            Assert.Equal(WorkspacePathResolver.NormalizePath(tempDir), context.WorkspaceRoot);

            var symbols = context.Db.Symbols.FindByName("AutoIndexedSample");
            Assert.NotEmpty(symbols);
        }
        finally
        {
            dbManager.Dispose();
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task GetOrIndexWorkspaceAsync_ConcurrentCalls_IndexesOnlyOnce()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "DynamicConcurrentTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        File.WriteAllText(Path.Combine(tempDir, "Service.cs"), "public class ConcurrentSample { }");

        using var dbManager = new WorkspaceDbManager();
        var service = new DynamicWorkspaceService(dbManager);

        try
        {
            var tasks = Enumerable.Range(0, 10)
                .Select(_ => service.GetOrIndexWorkspaceAsync(tempDir))
                .ToArray();

            var results = await Task.WhenAll(tasks);
            Assert.Equal(10, results.Length);
            foreach (var r in results)
            {
                Assert.Same(results[0].Db, r.Db);
            }
        }
        finally
        {
            dbManager.Dispose();
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, recursive: true);
        }
    }
}
