using System.CommandLine;
using System.Text.Json;
using CodeAtlas.Commands;
using CodeAtlas.Core.Db;
using CodeAtlas.Models;
using Microsoft.Data.Sqlite;
using Xunit;
using Symbol = CodeAtlas.Models.Symbol;

namespace CodeAtlas.Tests;

[Collection("Sequential")]
public class QueryCommandHandlerTests : IDisposable
{
    private static readonly SemaphoreSlim ConsoleLock = new(1, 1);
    private readonly string _tempDir;
    private readonly string _dbPath;
    private readonly CodeAtlasDb _db;

    public QueryCommandHandlerTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "QueryCliTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _dbPath = CodeAtlas.Core.Config.StorageConfig.ResolveDbPath(null, _tempDir);
        _db = new CodeAtlasDb(_dbPath);
        _db.Migrate();

        var symbols = new List<Symbol>
        {
            new() { SymbolName = "GetCustomerRequest", SymbolType = SymbolType.Class, FilePath = "src/Dto.cs", StartLine = 10, EndLine = 20, StartByte = 100, EndByte = 200, Signature = "public sealed record GetCustomerRequest(int Id)", Language = "csharp" },
            new() { SymbolName = "GetCustomerResponse", SymbolType = SymbolType.Class, FilePath = "src/Dto.cs", StartLine = 25, EndLine = 35, StartByte = 250, EndByte = 350, Signature = "public sealed record GetCustomerResponse(string Name)", Language = "csharp" },
            new() { SymbolName = "CustomerService", SymbolType = SymbolType.Class, FilePath = "src/CustomerService.cs", StartLine = 1, EndLine = 50, StartByte = 0, EndByte = 500, Signature = "public class CustomerService : ICustomerService", Language = "csharp" }
        };
        _db.Symbols.UpsertFile("src/Dto.cs", symbols);
    }

    public void Dispose()
    {
        _db.Dispose();
        SqliteConnection.ClearAllPools();
        try { if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, recursive: true); } catch { }
    }

    [Fact]
    public async Task Query_WithFilterPattern_OutputsMatchingSymbols()
    {
        await ConsoleLock.WaitAsync();
        var currentOut = Console.Out;
        using var sw = new StringWriter();
        try
        {
            Console.SetOut(sw);
            var root = CommandFactory.CreateRootCommand();
            var exitCode = await root.InvokeAsync(new[] { "query", "--filter", "(Request|Response)", "-r", _tempDir });

            Assert.Equal(0, exitCode);
            var output = sw.ToString();

            Assert.Contains("GetCustomerRequest", output);
            Assert.Contains("GetCustomerResponse", output);
            Assert.DoesNotContain("CustomerService", output);
        }
        finally
        {
            Console.SetOut(currentOut);
            ConsoleLock.Release();
        }
    }

    [Fact]
    public async Task Query_WithJsonFlag_OutputsValidJsonAndCursor()
    {
        await ConsoleLock.WaitAsync();
        var currentOut = Console.Out;
        using var sw = new StringWriter();
        try
        {
            Console.SetOut(sw);
            var root = CommandFactory.CreateRootCommand();
            var exitCode = await root.InvokeAsync(new[] { "query", "--filter", ".*Request$", "-r", _tempDir, "--json" });

            Assert.Equal(0, exitCode);
            var output = sw.ToString().Trim();

            using var doc = JsonDocument.Parse(output);
            var rootEl = doc.RootElement;
            Assert.True(rootEl.TryGetProperty("symbols", out var symsArr) || rootEl.TryGetProperty("Symbols", out symsArr));
            Assert.Equal(1, symsArr.GetArrayLength());
            Assert.True(symsArr[0].TryGetProperty("name", out var nameEl) || symsArr[0].TryGetProperty("Name", out nameEl));
            Assert.Equal("GetCustomerRequest", nameEl.GetString());
            Assert.True(symsArr[0].TryGetProperty("startByte", out var sbEl) || symsArr[0].TryGetProperty("StartByte", out sbEl));
            Assert.Equal(100, sbEl.GetInt32());
            Assert.True(symsArr[0].TryGetProperty("endByte", out var ebEl) || symsArr[0].TryGetProperty("EndByte", out ebEl));
            Assert.Equal(200, ebEl.GetInt32());
            Assert.True(symsArr[0].TryGetProperty("signature", out var sigEl) || symsArr[0].TryGetProperty("Signature", out sigEl));
            Assert.Equal("public sealed record GetCustomerRequest(int Id)", sigEl.GetString());
            Assert.True(rootEl.TryGetProperty("transpiledToSql", out var transEl) || rootEl.TryGetProperty("TranspiledToSql", out transEl));
            Assert.True(transEl.GetBoolean());
        }
        finally
        {
            Console.SetOut(currentOut);
            ConsoleLock.Release();
        }
    }

    [Fact]
    public async Task QueryRaw_WithFilter_OutputsTabSeparatedLines()
    {
        await ConsoleLock.WaitAsync();
        var currentOut = Console.Out;
        using var sw = new StringWriter();
        try
        {
            Console.SetOut(sw);
            var root = CommandFactory.CreateRootCommand();
            var exitCode = await root.InvokeAsync(new[] { "query", "raw", "--filter", "(Request|Response)", "-r", _tempDir });

            Assert.Equal(0, exitCode);
            var output = sw.ToString();

            Assert.Contains("GetCustomerRequest", output);
            Assert.Contains("\tClass\t", output);
        }
        finally
        {
            Console.SetOut(currentOut);
            ConsoleLock.Release();
        }
    }
}

