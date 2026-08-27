using CodeAtlas.Core.Db;
using CodeAtlas.Core.Filtering;
using CodeAtlas.Models;
using Microsoft.Data.Sqlite;
using Xunit;

namespace CodeAtlas.Tests;

public class SymbolQueriesFilterTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _dbPath;
    private readonly CodeAtlasDb _db;

    public SymbolQueriesFilterTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "SymbolQueriesFilterTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _dbPath = Path.Combine(_tempDir, "test.db");
        _db = new CodeAtlasDb(_dbPath);
        _db.Migrate();
    }

    public void Dispose()
    {
        _db.Dispose();
        SqliteConnection.ClearAllPools();
        try { if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, recursive: true); } catch { }
    }

    [Fact]
    public void FindWithFilter_PushdownAlternation_ReturnsMatchingRecords()
    {
        var symbols = new List<Symbol>
        {
            new() { SymbolName = "GetUserRequest", SymbolType = SymbolType.Class, FilePath = "src/Users.cs", StartLine = 1, EndLine = 10, StartByte = 0, EndByte = 100, Language = "csharp" },
            new() { SymbolName = "GetUserResponse", SymbolType = SymbolType.Class, FilePath = "src/Users.cs", StartLine = 12, EndLine = 20, StartByte = 120, EndByte = 200, Language = "csharp" },
            new() { SymbolName = "OrderEvent", SymbolType = SymbolType.Class, FilePath = "src/Orders.cs", StartLine = 1, EndLine = 10, StartByte = 0, EndByte = 100, Language = "csharp" },
            new() { SymbolName = "UserService", SymbolType = SymbolType.Class, FilePath = "src/Service.cs", StartLine = 1, EndLine = 30, StartByte = 0, EndByte = 300, Language = "csharp" }
        };

        _db.Symbols.UpsertFile("src/All.cs", symbols);

        var filter = FilterTranspiler.Transpile("(Request|Response|Event)");
        var result = _db.Symbols.FindWithFilter(filter, limit: 100);

        Assert.True(result.TranspiledToSql);
        Assert.Equal(3, result.TotalReturned);
        Assert.Contains(result.Items, s => s.SymbolName == "GetUserRequest");
        Assert.Contains(result.Items, s => s.SymbolName == "GetUserResponse");
        Assert.Contains(result.Items, s => s.SymbolName == "OrderEvent");
        Assert.DoesNotContain(result.Items, s => s.SymbolName == "UserService");
    }

    [Fact]
    public void FindWithFilter_KeysetCursorPagination_YieldsZeroDuplicatesAndFullCoverage()
    {
        var symbols = Enumerable.Range(1, 25).Select(i => new Symbol
        {
            SymbolName = $"Item{i:D2}Dto",
            SymbolType = SymbolType.Class,
            FilePath = "src/Dto.cs",
            StartLine = i,
            EndLine = i + 1,
            StartByte = i * 10,
            EndByte = (i + 1) * 10,
            Language = "csharp"
        }).ToList();

        _db.Symbols.UpsertFile("src/Dto.cs", symbols);

        var filter = FilterTranspiler.Transpile(".*Dto$");

        // Page 1: limit 10
        var page1 = _db.Symbols.FindWithFilter(filter, cursor: 0, limit: 10);
        Assert.Equal(10, page1.TotalReturned);
        Assert.True(page1.HasMore);
        Assert.True(page1.NextCursor > 0);

        // Page 2: limit 10 using cursor
        var page2 = _db.Symbols.FindWithFilter(filter, cursor: page1.NextCursor, limit: 10);
        Assert.Equal(10, page2.TotalReturned);
        Assert.True(page2.HasMore);
        Assert.True(page2.NextCursor > page1.NextCursor);

        // Page 3: limit 10 using cursor
        var page3 = _db.Symbols.FindWithFilter(filter, cursor: page2.NextCursor, limit: 10);
        Assert.Equal(5, page3.TotalReturned);
        Assert.False(page3.HasMore);

        // Verify mutually exclusive items
        var page1Ids = page1.Items.Select(x => x.Id).ToHashSet();
        var page2Ids = page2.Items.Select(x => x.Id).ToHashSet();
        var page3Ids = page3.Items.Select(x => x.Id).ToHashSet();

        Assert.Empty(page1Ids.Intersect(page2Ids));
        Assert.Empty(page2Ids.Intersect(page3Ids));
        Assert.Equal(25, page1Ids.Count + page2Ids.Count + page3Ids.Count);
    }

    [Fact]
    public void FindWithFilter_StreamingRegexFallback_FiltersCorrectly()
    {
        var symbols = new List<Symbol>
        {
            new() { SymbolName = "OrderService", SymbolType = SymbolType.Class, FilePath = "src/Orders.cs", StartLine = 1, EndLine = 10, StartByte = 0, EndByte = 100, Language = "csharp" },
            new() { SymbolName = "OrderServiceInternal", SymbolType = SymbolType.Class, FilePath = "src/Orders.cs", StartLine = 12, EndLine = 20, StartByte = 120, EndByte = 200, Language = "csharp" },
            new() { SymbolName = "UserService", SymbolType = SymbolType.Class, FilePath = "src/Users.cs", StartLine = 1, EndLine = 10, StartByte = 0, EndByte = 100, Language = "csharp" }
        };

        _db.Symbols.UpsertFile("src/Services.cs", symbols);

        // Complex lookaround pattern forces regex fallback
        var filter = FilterTranspiler.Transpile("(?=.*Service)(?!.*Internal)");
        Assert.False(filter.IsTranslatable);

        var result = _db.Symbols.FindWithFilter(filter, limit: 10);

        Assert.False(result.TranspiledToSql);
        Assert.Equal(2, result.TotalReturned);
        Assert.Contains(result.Items, s => s.SymbolName == "OrderService");
        Assert.Contains(result.Items, s => s.SymbolName == "UserService");
        Assert.DoesNotContain(result.Items, s => s.SymbolName == "OrderServiceInternal");
    }
}
