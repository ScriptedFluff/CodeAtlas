using CodeGraphMcp.Db;
using CodeGraphMcp.Filtering;
using CodeGraphMcp.Models;
using Microsoft.Data.Sqlite;
using Xunit;

namespace CodeGraphMcp.Tests;

public class PrimaryQueryGlobTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _dbPath;
    private readonly CodeGraphDb _db;

    public PrimaryQueryGlobTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "PrimaryGlobTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _dbPath = Path.Combine(_tempDir, "test.db");
        _db = new CodeGraphDb(_dbPath);
        _db.Migrate();

        var symbols = new List<Symbol>
        {
            new() { SymbolName = "OrderDto", SymbolType = SymbolType.Class, FilePath = "src/Dto.cs", StartLine = 10, EndLine = 20, StartByte = 100, EndByte = 200, Language = "csharp" },
            new() { SymbolName = "UserDto", SymbolType = SymbolType.Class, FilePath = "src/Dto.cs", StartLine = 25, EndLine = 35, StartByte = 250, EndByte = 350, Language = "csharp" },
            new() { SymbolName = "OrderDtoHandler", SymbolType = SymbolType.Class, FilePath = "src/Handler.cs", StartLine = 1, EndLine = 50, StartByte = 0, EndByte = 500, Language = "csharp" },
            new() { SymbolName = "CustomerDtoResponse", SymbolType = SymbolType.Class, FilePath = "src/Response.cs", StartLine = 1, EndLine = 50, StartByte = 0, EndByte = 500, Language = "csharp" },
            new() { SymbolName = "GetOrder", SymbolType = SymbolType.Method, FilePath = "src/Service.cs", StartLine = 1, EndLine = 20, StartByte = 0, EndByte = 200, Language = "csharp" },
            new() { SymbolName = "GetUser", SymbolType = SymbolType.Method, FilePath = "src/Service.cs", StartLine = 25, EndLine = 45, StartByte = 250, EndByte = 450, Language = "csharp" },
            new() { SymbolName = "AsyncGetOrder", SymbolType = SymbolType.Method, FilePath = "src/Service.cs", StartLine = 50, EndLine = 70, StartByte = 500, EndByte = 700, Language = "csharp" },
            new() { SymbolName = "IUserService", SymbolType = SymbolType.Interface, FilePath = "src/Interfaces.cs", StartLine = 1, EndLine = 30, StartByte = 0, EndByte = 300, Language = "csharp" },
            new() { SymbolName = "ICustomerService", SymbolType = SymbolType.Interface, FilePath = "src/Interfaces.cs", StartLine = 35, EndLine = 65, StartByte = 350, EndByte = 650, Language = "csharp" },
            new() { SymbolName = "ItemService", SymbolType = SymbolType.Class, FilePath = "src/ItemService.cs", StartLine = 1, EndLine = 40, StartByte = 0, EndByte = 400, Language = "csharp" },
            new() { SymbolName = "iLegacyService", SymbolType = SymbolType.Interface, FilePath = "src/Legacy.cs", StartLine = 1, EndLine = 20, StartByte = 0, EndByte = 200, Language = "csharp" }
        };
        _db.Symbols.UpsertFile("src/All.cs", symbols);
    }

    public void Dispose()
    {
        _db.Dispose();
        SqliteConnection.ClearAllPools();
        try { if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, recursive: true); } catch { }
    }

    [Fact]
    public void FindByName_WithSuffixGlob_RespectsSuffixBoundaries()
    {
        var results = _db.Symbols.FindByName("*Dto");
        var names = results.Select(s => s.SymbolName).ToList();

        Assert.Contains("OrderDto", names);
        Assert.Contains("UserDto", names);
        Assert.DoesNotContain("OrderDtoHandler", names);
        Assert.DoesNotContain("CustomerDtoResponse", names);
    }

    [Fact]
    public void FindByName_WithPrefixGlob_RespectsPrefixBoundaries()
    {
        var results = _db.Symbols.FindByName("Get*");
        var names = results.Select(s => s.SymbolName).ToList();

        Assert.Contains("GetOrder", names);
        Assert.Contains("GetUser", names);
        Assert.DoesNotContain("AsyncGetOrder", names);
    }

    [Fact]
    public void FindByName_WithInterfaceGlob_CaseSensitiveMatch()
    {
        var results = _db.Symbols.FindByName("I[A-Z]*");
        var names = results.Select(s => s.SymbolName).ToList();

        Assert.Contains("IUserService", names);
        Assert.Contains("ICustomerService", names);
        Assert.DoesNotContain("ItemService", names);
        Assert.DoesNotContain("iLegacyService", names);
    }

    [Fact]
    public void FindByName_WithPureIdentifier_MatchesSubstring()
    {
        var results = _db.Symbols.FindByName("Order");
        var names = results.Select(s => s.SymbolName).ToList();

        Assert.Contains("OrderDto", names);
        Assert.Contains("OrderDtoHandler", names);
        Assert.Contains("GetOrder", names);
        Assert.Contains("AsyncGetOrder", names);
    }

    [Fact]
    public void FindByName_WithComplexRegex_ThrowsArgumentException()
    {
        var ex = Assert.Throws<ArgumentException>(() => _db.Symbols.FindByName("(?=.*Service)"));
        Assert.Contains("--filter", ex.Message);
    }
}
