using CodeGraphMcp.Db;
using CodeGraphMcp.Indexer;
using CodeGraphMcp.Models;
using CodeGraphMcp.Parser;
using Xunit;

namespace CodeGraphMcp.Tests;

public class SingleFileIndexerTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _dbPath;
    private readonly CodeGraphDb _db;
    private readonly SingleFileIndexer _indexer;

    public SingleFileIndexerTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"codegraph_singlefile_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _dbPath = Path.Combine(_tempDir, "test.db");
        _db = new CodeGraphDb(_dbPath);
        _db.Migrate();

        _indexer = new SingleFileIndexer(ParserRegistry.CreateDefault());
    }

    public void Dispose()
    {
        _db.Dispose();
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
    public async Task UpsertFile_NewFile_InsertsSymbolsAndReferences()
    {
        const string csharpCode = """
            namespace MyApp.Services;

            public class OrderService
            {
                public void ProcessOrder(int orderId)
                {
                    var validator = new OrderValidator();
                    validator.Validate(orderId);
                }
            }
            """;

        var filePath = Path.Combine(_tempDir, "OrderService.cs");
        await File.WriteAllTextAsync(filePath, csharpCode);

        var result = await _indexer.UpsertFileAsync(_tempDir, _db, "OrderService.cs");

        Assert.True(result.SymbolsAdded > 0);
        Assert.Equal(0, result.SymbolsDeleted);

        var symbols = _db.Symbols.FindByName("OrderService");
        Assert.Single(symbols);
        Assert.Equal(SymbolType.Class, symbols[0].SymbolType);
        Assert.Equal("OrderService.cs", symbols[0].FilePath.Replace('\\', '/'));

        var methodSym = _db.Symbols.FindByName("ProcessOrder");
        Assert.Single(methodSym);
        Assert.Equal(SymbolType.Method, methodSym[0].SymbolType);
    }

    [Fact]
    public async Task UpsertFile_ModifyExistingFile_UpdatesInPlaceWithoutDuplicates()
    {
        const string initialCode = """
            namespace MyApp.Services;

            public class CustomerService
            {
                public string GetCustomerName(int id)
                {
                    return "Alice";
                }
            }
            """;

        var filePath = Path.Combine(_tempDir, "CustomerService.cs");
        await File.WriteAllTextAsync(filePath, initialCode);

        // 1. Initial upsert
        var res1 = await _indexer.UpsertFileAsync(_tempDir, _db, "CustomerService.cs");
        Assert.Equal(2, res1.SymbolsAdded);

        var initialSymbols = _db.Symbols.GetSymbolsByFile("CustomerService.cs");
        Assert.Equal(2, initialSymbols.Count);
        var initialClassId = initialSymbols.First(s => s.SymbolName == "CustomerService").Id;
        var initialMethodId = initialSymbols.First(s => s.SymbolName == "GetCustomerName").Id;

        // 2. Modify signature and lines
        const string modifiedCode = """
            // Extra header comment pushing lines down
            // Another comment
            namespace MyApp.Services;

            /// <summary>Updated doc comment</summary>
            public class CustomerService
            {
                public async Task<string> GetCustomerName(int id, CancellationToken ct)
                {
                    return await Task.FromResult("Alice");
                }
            }
            """;
        await File.WriteAllTextAsync(filePath, modifiedCode);

        var res2 = await _indexer.UpsertFileAsync(_tempDir, _db, "CustomerService.cs");

        Assert.Equal(2, res2.SymbolsUpdated);
        Assert.Equal(0, res2.SymbolsAdded);
        Assert.Equal(0, res2.SymbolsDeleted);

        // 3. Verify IDs were preserved in-place (no duplicate rows)
        var updatedSymbols = _db.Symbols.GetSymbolsByFile("CustomerService.cs");
        Assert.Equal(2, updatedSymbols.Count);

        var updatedClass = updatedSymbols.First(s => s.SymbolName == "CustomerService");
        var updatedMethod = updatedSymbols.First(s => s.SymbolName == "GetCustomerName");

        Assert.Equal(initialClassId, updatedClass.Id);
        Assert.Equal(initialMethodId, updatedMethod.Id);
        Assert.True(updatedClass.StartLine > initialSymbols.First(s => s.SymbolName == "CustomerService").StartLine);
        Assert.Contains("Updated doc comment", updatedClass.DocComment ?? "");
        Assert.Contains("Task<string>", updatedMethod.Signature ?? "");
    }

    [Fact]
    public async Task UpsertFile_PreservesCrossFileIncomingReferences_WhenTargetFileIsModified()
    {
        // 1. FileB defines TargetMethod
        const string fileBCode = """
            namespace MyApp;

            public class TargetClass
            {
                public void TargetMethod()
                {
                }
            }
            """;
        var fileBPath = Path.Combine(_tempDir, "TargetClass.cs");
        await File.WriteAllTextAsync(fileBPath, fileBCode);
        await _indexer.UpsertFileAsync(_tempDir, _db, "TargetClass.cs");

        // 2. FileA calls TargetMethod
        const string fileACode = """
            namespace MyApp;

            public class CallerClass
            {
                public void CallTarget()
                {
                    var t = new TargetClass();
                    t.TargetMethod();
                }
            }
            """;
        var fileAPath = Path.Combine(_tempDir, "CallerClass.cs");
        await File.WriteAllTextAsync(fileAPath, fileACode);
        await _indexer.UpsertFileAsync(_tempDir, _db, "CallerClass.cs");

        // Verify initial caller reference exists
        var initialCallers = _db.References.FindCallers("TargetMethod");
        Assert.Single(initialCallers);
        Assert.Equal("CallTarget", initialCallers[0].CallerName);
        Assert.Equal("CallerClass.cs", initialCallers[0].FilePath);

        // 3. Surgically modify TargetClass.cs (e.g. shift lines or modify method body)
        const string fileBModified = """
            // Extra comment
            // Extra line
            namespace MyApp;

            public class TargetClass
            {
                public void TargetMethod()
                {
                    // New internal logic
                    Console.WriteLine("Hello");
                }
            }
            """;
        await File.WriteAllTextAsync(fileBPath, fileBModified);
        var upsertResult = await _indexer.UpsertFileAsync(_tempDir, _db, "TargetClass.cs");

        Assert.True(upsertResult.SymbolsUpdated > 0);

        // 4. Verify the caller edge from CallerClass.cs was NOT broken!
        var updatedCallers = _db.References.FindCallers("TargetMethod");
        Assert.Single(updatedCallers);
        Assert.Equal("CallTarget", updatedCallers[0].CallerName);
        Assert.Equal("CallerClass.cs", updatedCallers[0].FilePath);
    }

    [Fact]
    public async Task DeleteFile_RemovesSymbolsAndAllAssociatedEdges()
    {
        // 1. TargetClass in TargetClass.cs
        const string targetCode = """
            namespace MyApp;
            public class TargetClass
            {
                public void DoAction() {}
            }
            """;
        await File.WriteAllTextAsync(Path.Combine(_tempDir, "TargetClass.cs"), targetCode);
        await _indexer.UpsertFileAsync(_tempDir, _db, "TargetClass.cs");

        // 2. CallerClass in CallerClass.cs
        const string callerCode = """
            namespace MyApp;
            public class CallerClass
            {
                public void Run()
                {
                    var t = new TargetClass();
                    t.DoAction();
                }
            }
            """;
        await File.WriteAllTextAsync(Path.Combine(_tempDir, "CallerClass.cs"), callerCode);
        await _indexer.UpsertFileAsync(_tempDir, _db, "CallerClass.cs");

        Assert.NotEmpty(_db.Symbols.FindByName("TargetClass"));
        Assert.NotEmpty(_db.References.FindCallers("DoAction"));

        // 3. Delete TargetClass.cs
        var deleteResult = await _indexer.DeleteFileAsync(_tempDir, _db, "TargetClass.cs");

        Assert.True(deleteResult.SymbolsDeleted > 0);
        Assert.Empty(_db.Symbols.FindByName("TargetClass"));
        Assert.Empty(_db.Symbols.FindByName("DoAction"));

        // The incoming reference edge to DoAction must be purged
        var callers = _db.References.FindCallers("DoAction");
        Assert.Empty(callers);
    }

    [Fact]
    public async Task UpsertFile_WithInMemoryContent_OverridesDiskContent()
    {
        const string diskCode = """
            public class DiskVersion
            {
                public void OriginalMethod() {}
            }
            """;
        var path = Path.Combine(_tempDir, "VirtualFile.cs");
        await File.WriteAllTextAsync(path, diskCode);

        const string memoryCode = """
            public class InMemoryVersion
            {
                public void NewMethod() {}
            }
            """;

        var result = await _indexer.UpsertFileAsync(_tempDir, _db, "VirtualFile.cs", memoryCode);

        Assert.True(result.SymbolsAdded > 0);
        Assert.NotEmpty(_db.Symbols.FindByName("InMemoryVersion"));
        Assert.Empty(_db.Symbols.FindByName("DiskVersion"));
    }

    [Fact]
    public async Task UpsertFile_NonExistentFileWithoutContent_TriggersCleanDelete()
    {
        // Insert a file first
        const string code = "public class ToBeDeleted {}";
        await File.WriteAllTextAsync(Path.Combine(_tempDir, "Temp.cs"), code);
        await _indexer.UpsertFileAsync(_tempDir, _db, "Temp.cs");
        Assert.NotEmpty(_db.Symbols.FindByName("ToBeDeleted"));

        // Remove from disk
        File.Delete(Path.Combine(_tempDir, "Temp.cs"));

        // Upsert should detect non-existence and purge
        var res = await _indexer.UpsertFileAsync(_tempDir, _db, "Temp.cs");
        Assert.Equal(1, res.SymbolsDeleted);
        Assert.Empty(_db.Symbols.FindByName("ToBeDeleted"));
    }
}
