using CodeGraphMcp.Db;
using CodeGraphMcp.Mcp.Tools;
using Xunit;

namespace CodeGraphMcp.Tests;

public class PruneIndexedWorktreesToolTests
{
    [Fact]
    public async Task PruneIndexedWorktreesAsync_DeletesOrphanedDatabaseFiles()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "cg_prune_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            var dbPath = Path.Combine(tempDir, "orphaned.db");
            using (var db = new CodeGraphDb(dbPath))
            {
                db.Migrate();
                db.Symbols.UpsertSymbolsBatch([
                    ("non_existent_folder_123/file.cs", [
                        new Models.Symbol
                        {
                            SymbolName = "TestSym",
                            SymbolType = Models.SymbolType.Class,
                            FilePath = "C:\\non_existent_folder_123\\file.cs",
                            StartLine = 1,
                            EndLine = 10,
                            StartByte = 0,
                            EndByte = 100
                        }
                    ])
                ]);
            }

            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            Assert.True(File.Exists(dbPath));

            var tool = new PruneIndexedWorktreesTool();
            var result = await tool.PruneIndexedWorktreesAsync(tempDir);

            Assert.Equal(1, result.PrunedDatabaseCount);
            Assert.True(result.TotalBytesFreed > 0);
            Assert.False(File.Exists(dbPath));
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                try { Directory.Delete(tempDir, true); } catch { }
            }
        }
    }

    [Fact]
    public async Task PruneIndexedWorktreesAsync_WithDryRun_DoesNotDeleteDatabaseFiles()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "cg_prune_dry_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            var dbPath = Path.Combine(tempDir, "orphaned.db");
            using (var db = new CodeGraphDb(dbPath))
            {
                db.Migrate();
                db.Symbols.UpsertSymbolsBatch([
                    ("non_existent_folder_123/file.cs", [
                        new Models.Symbol
                        {
                            SymbolName = "TestSym",
                            SymbolType = Models.SymbolType.Class,
                            FilePath = "C:\\non_existent_folder_123\\file.cs",
                            StartLine = 1,
                            EndLine = 10,
                            StartByte = 0,
                            EndByte = 100
                        }
                    ])
                ]);
            }

            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            Assert.True(File.Exists(dbPath));

            var tool = new PruneIndexedWorktreesTool();
            var result = await tool.PruneIndexedWorktreesAsync(tempDir, dryRun: true);

            Assert.Equal(1, result.PrunedDatabaseCount);
            Assert.True(result.TotalBytesFreed > 0);
            Assert.True(File.Exists(dbPath));
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                try { Directory.Delete(tempDir, true); } catch { }
            }
        }
    }
}
