using CodeAtlas.Core.Db;
using CodeAtlas.Models;
using CodeAtlas.Parser;
using Microsoft.Data.Sqlite;
using Xunit;

namespace CodeAtlas.Tests;

public sealed class Phase7OptimizationTests
{
    [Fact]
    public void SymbolLookupCache_ResolvesIdCorrectly()
    {
        var cache = new SymbolLookupCache();
        cache.Add(101, "MyService", "Services/MyService.cs", 10);
        cache.Add(102, "MyService", "Controllers/MyController.cs", 5);

        // Preferred file lookup
        var preferredId = cache.ResolveId("MyService", "Controllers/MyController.cs");
        Assert.Equal(102, preferredId);

        // Fallback first match lookup
        var defaultId = cache.ResolveId("MyService");
        Assert.Equal(101, defaultId);

        // Primary symbol for file
        var primaryId = cache.GetPrimarySymbolIdForFile("Services/MyService.cs");
        Assert.Equal(101, primaryId);
    }

    [Fact]
    public void CSharpParser_ParseFile_ExtractsSymbolsAndReferencesInSinglePass()
    {
        var parser = new CSharpParser();
        var code = """
            namespace MyApp;

            public class UserService
            {
                public void Execute()
                {
                    Logger.Log("Hello");
                }
            }
            """;

        var result = parser.ParseFile("Services/UserService.cs", code);

        Assert.NotEmpty(result.Symbols);
        Assert.Contains(result.Symbols, s => s.SymbolName == "UserService");

        Assert.NotEmpty(result.References);
        Assert.Contains(result.References, r => r.TargetSymbolName == "Log");
    }

    [Fact]
    public void UpsertSymbolsAndReferencesBatch_ExecutesSuccessfully()
    {
        using var conn = new SqliteConnection("Data Source=:memory:");
        conn.Open();

        using var db = new CodeAtlasDb(":memory:");
        db.Migrate();

        var symbols = new List<Symbol>
        {
            new Symbol
            {
                SymbolName = "OrderProcessor",
                SymbolType = SymbolType.Class,
                FilePath = "OrderProcessor.cs",
                StartLine = 1,
                EndLine = 20,
                StartByte = 0,
                EndByte = 100
            }
        };

        var refs = new List<SymbolReference>
        {
            new SymbolReference("OrderProcessor", "Process", "Call", "OrderProcessor.cs", 5)
        };

        var fileSymbols = new List<(string FilePath, IReadOnlyList<Symbol> Symbols)>
        {
            ("OrderProcessor.cs", symbols)
        };

        var fileRefs = new List<(string FilePath, IReadOnlyList<SymbolReference> Refs)>
        {
            ("OrderProcessor.cs", refs)
        };

        var cache = db.Symbols.UpsertSymbolsBatch(fileSymbols);
        Assert.NotNull(cache.ResolveId("OrderProcessor"));

        db.References.UpsertReferencesBatch(fileRefs, cache);
        Assert.True(db.Symbols.GetCount() > 0);
    }

    [Fact]
    public void DeltaResult_SeededDelta_StoresSeedMetadata()
    {
        var result = new CodeAtlas.Core.Git.DeltaEngine.DeltaResult(
            FilesChanged: 5,
            FilesReindexed: 5,
            SymbolsDeleted: 0,
            SymbolsAdded: 30,
            ElapsedMs: 120,
            Mode: "seeded-delta",
            SeedSourcePath: "C:/global/dbs/main.db",
            SeedCommitHash: "abc123456789"
        );

        Assert.Equal("seeded-delta", result.Mode);
        Assert.Equal("C:/global/dbs/main.db", result.SeedSourcePath);
        Assert.Equal("abc123456789", result.SeedCommitHash);
    }
}

