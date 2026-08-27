using CodeAtlas.Core.Db;
using CodeAtlas.Core.Git;
using CodeAtlas.Core.Indexer;
using CodeAtlas.Mcp.Tools;
using CodeAtlas.Parser;
using Xunit;

namespace CodeAtlas.Tests;

public class SyncFileToolTests : IDisposable
{
    private readonly string _tempDir;
    private readonly WorkspaceDbManager _dbManager;
    private readonly WorkspaceBufferManager _bufferManager;
    private readonly SingleFileIndexer _singleFileIndexer;
    private readonly DynamicWorkspaceService _workspaceService;
    private readonly SyncFileTool _tool;

    public SyncFileToolTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"codegraph_sync_tool_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);

        var parsers = ParserRegistry.CreateDefault();
        _dbManager = new WorkspaceDbManager(_tempDir);
        _bufferManager = new WorkspaceBufferManager(parsers);
        _singleFileIndexer = new SingleFileIndexer(parsers);
        _workspaceService = new DynamicWorkspaceService(_dbManager, _tempDir, parsers);

        _tool = new SyncFileTool(_workspaceService, _bufferManager, _singleFileIndexer);
    }

    public void Dispose()
    {
        _dbManager.Dispose();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try
        {
            if (Directory.Exists(_tempDir))
            {
                Directory.Delete(_tempDir, recursive: true);
            }
        }
        catch { }
    }

    [Fact]
    public async Task SyncFileAsync_UpsertAndThenDelete_WorksEndToEnd()
    {
        const string csharpCode = """
            namespace ToolTest;

            public class WidgetService
            {
                public void CreateWidget() {}
            }
            """;

        var filePath = Path.Combine(_tempDir, "WidgetService.cs");
        await File.WriteAllTextAsync(filePath, csharpCode);

        // 1. Sync file (upsert)
        var upsertResult = await _tool.SyncFileAsync("WidgetService.cs", targetDirectory: _tempDir);

        Assert.Equal("upsert", upsertResult.Operation);
        Assert.True(upsertResult.SymbolsAdded > 0 || upsertResult.SymbolsUpdated > 0);

        var db = _dbManager.GetOrCreateDb(_tempDir);
        var syms = db.Symbols.FindByName("WidgetService");
        Assert.Single(syms);

        // 2. Sync file with delete: true
        var deleteResult = await _tool.SyncFileAsync("WidgetService.cs", delete: true, targetDirectory: _tempDir);

        Assert.Equal("delete", deleteResult.Operation);
        Assert.True(deleteResult.SymbolsDeleted > 0);

        var deletedSyms = db.Symbols.FindByName("WidgetService");
        Assert.Empty(deletedSyms);
    }

    [Fact]
    public async Task SyncFileAsync_WithContent_ParsesWithoutFileOnDisk()
    {
        const string virtualCode = """
            public class VirtualController
            {
                public void Execute() {}
            }
            """;

        var result = await _tool.SyncFileAsync(
            filePath: "VirtualController.cs",
            content: virtualCode,
            targetDirectory: _tempDir);

        Assert.Equal("upsert", result.Operation);
        Assert.True(result.SymbolsAdded > 0);

        var db = _dbManager.GetOrCreateDb(_tempDir);
        var syms = db.Symbols.FindByName("VirtualController");
        Assert.Single(syms);
    }
}
