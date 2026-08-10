using CodeGraphMcp.CrossStack;
using CodeGraphMcp.Db;
using CodeGraphMcp.Models;
using Xunit;

namespace CodeGraphMcp.Tests.CrossStack;

public sealed class ServiceStackLinkerTests : IDisposable
{
    private readonly string _dbPath;
    private readonly CodeGraphDb _db;

    public ServiceStackLinkerTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), "cg_ss_test_" + Guid.NewGuid().ToString("N") + ".db");
        _db = new CodeGraphDb(_dbPath);
        _db.Migrate();
    }

    [Fact]
    public void LinkDtos_MatchesCSharpDtoToTypeScriptInterface()
    {
        // 1. Insert C# DTO symbol
        _db.Symbols.UpsertFile("src/Requests/GetUserBillingRequest.cs",
        [
            new Symbol
            {
                SymbolName = "GetUserBillingRequest",
                SymbolType = SymbolType.Class,
                ClassName  = "MyNamespace.Requests",
                Namespace  = "MyNamespace.Requests",
                Language   = "csharp",
                FilePath   = "src/Requests/GetUserBillingRequest.cs",
                StartLine  = 1, EndLine = 10, StartByte = 0, EndByte = 100
            }
        ]);

        // 2. Insert TypeScript Interface symbol
        _db.Symbols.UpsertFile("clevernpm/dtos.ts",
        [
            new Symbol
            {
                SymbolName = "GetUserBillingRequest",
                SymbolType = SymbolType.Interface,
                Language   = "typescript",
                FilePath   = "clevernpm/dtos.ts",
                StartLine  = 1, EndLine = 10, StartByte = 0, EndByte = 100
            }
        ]);

        var linker = new ServiceStackLinker(_db);
        var result = linker.LinkDtos();

        Assert.Equal(1, result.Total);
        Assert.Equal(1, result.Linked);

        var mapping = _db.CrossStack.GetMapping("GetUserBillingRequest");
        Assert.NotNull(mapping);
        Assert.Equal("GetUserBillingRequest", mapping.DtoName);
        Assert.Equal("src/Requests/GetUserBillingRequest.cs", mapping.CsFilePath);
        Assert.Equal("clevernpm/dtos.ts", mapping.TsFilePath);
    }

    public void Dispose()
    {
        _db.Dispose();
        try { if (File.Exists(_dbPath)) File.Delete(_dbPath); } catch { }
    }
}
