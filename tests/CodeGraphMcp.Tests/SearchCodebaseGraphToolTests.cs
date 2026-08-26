using CodeGraphMcp.Db;
using CodeGraphMcp.Git;
using CodeGraphMcp.Indexer;
using CodeGraphMcp.Mcp.Tools;
using CodeGraphMcp.Models;
using CodeGraphMcp.Parser;
using Microsoft.Data.Sqlite;
using Xunit;

namespace CodeGraphMcp.Tests;

public class SearchCodebaseGraphToolTests
{
    [Fact]
    public async Task SearchCodebaseGraphAsync_WithExactMatch_FiltersOutSubstringMatches()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "SearchExactMatchTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        File.WriteAllText(Path.Combine(tempDir, "Models.cs"), """
            namespace Sample;
            public class Model {}
            public class UserModel {}
            public class ModelBase {}
            """);

        using var dbManager = new WorkspaceDbManager();
        var workspaceService = new DynamicWorkspaceService(dbManager);
        var parsers = new ILanguageParser[] { new CSharpParser() };
        var bufferManager = new WorkspaceBufferManager(parsers);

        try
        {
            var searchTool = new SearchCodebaseGraphTool(workspaceService, bufferManager);

            // Substring search (default)
            var substringResult = await searchTool.SearchCodebaseGraphAsync("Model", targetDirectory: tempDir, exactMatch: false);
            Assert.True(substringResult.Matches.Count >= 3);

            // Exact match search
            var exactResult = await searchTool.SearchCodebaseGraphAsync("Model", targetDirectory: tempDir, exactMatch: true);
            Assert.Single(exactResult.Matches);
            Assert.Equal("Model", exactResult.Matches[0].SymbolName);
        }
        finally
        {
            dbManager.Dispose();
            SqliteConnection.ClearAllPools();
            try { if (Directory.Exists(tempDir)) Directory.Delete(tempDir, recursive: true); } catch { }
        }
    }

    [Fact]
    public async Task SearchCodebaseGraphAsync_WithRegexPattern_FiltersSymbolNames()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "SearchRegexTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        File.WriteAllText(Path.Combine(tempDir, "Services.cs"), """
            namespace Sample;
            public class UserService {}
            public class OrderService {}
            public class PaymentHandler {}
            """);

        using var dbManager = new WorkspaceDbManager();
        var workspaceService = new DynamicWorkspaceService(dbManager);
        var parsers = new ILanguageParser[] { new CSharpParser() };
        var bufferManager = new WorkspaceBufferManager(parsers);

        try
        {
            var searchTool = new SearchCodebaseGraphTool(workspaceService, bufferManager);

            // Regex match ending with Service
            var regexResult = await searchTool.SearchCodebaseGraphAsync("%", targetDirectory: tempDir, regex: "Service$");
            Assert.Equal(2, regexResult.Matches.Count);
            Assert.All(regexResult.Matches, m => Assert.EndsWith("Service", m.SymbolName));

            // Invalid regex throws ArgumentException
            await Assert.ThrowsAsync<ArgumentException>(() =>
                searchTool.SearchCodebaseGraphAsync("%", targetDirectory: tempDir, regex: "[unclosed_group"));
        }
        finally
        {
            dbManager.Dispose();
            SqliteConnection.ClearAllPools();
            try { if (Directory.Exists(tempDir)) Directory.Delete(tempDir, recursive: true); } catch { }
        }
    }

    [Fact]
    public async Task SearchCodebaseGraphAsync_WithCustomLimitAndRelevanceSorting_AppliesLimitAndSorts()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "SearchLimitTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        File.WriteAllText(Path.Combine(tempDir, "Sample.cs"), """
            namespace Sample;
            public class UserItem 
            {
                public string UserItemProp { get; set; }
                public void UserItemMethod() {}
            }
            public class UserItemHelper {}
            """);

        using var dbManager = new WorkspaceDbManager();
        var workspaceService = new DynamicWorkspaceService(dbManager);
        var parsers = new ILanguageParser[] { new CSharpParser() };
        var bufferManager = new WorkspaceBufferManager(parsers);

        try
        {
            var searchTool = new SearchCodebaseGraphTool(workspaceService, bufferManager);

            // Test custom limit = 2
            var limitedResult = await searchTool.SearchCodebaseGraphAsync("UserItem%", targetDirectory: tempDir, limit: 2);
            Assert.Equal(2, limitedResult.Matches.Count);

            // Test relevance sorting (Class declarations should come before Method and Property)
            var fullResult = await searchTool.SearchCodebaseGraphAsync("UserItem%", targetDirectory: tempDir, limit: 10);
            Assert.True(fullResult.Matches.Count >= 3);
            Assert.Equal("Class", fullResult.Matches[0].SymbolType);
        }
        finally
        {
            dbManager.Dispose();
            SqliteConnection.ClearAllPools();
            try { if (Directory.Exists(tempDir)) Directory.Delete(tempDir, recursive: true); } catch { }
        }
    }

    [Fact]
    public void SymbolQueries_FindByName_WithLanguageFilter_DoesNotGetBlockedByOtherLanguages()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "SearchLangFilterTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var dbPath = Path.Combine(tempDir, "test.db");

        try
        {
            using var db = new CodeGraphDb(dbPath);
            db.Migrate();

            // Insert 600 JSON symbols named "raw"
            var jsonSymbols = Enumerable.Range(1, 600).Select(i => new Symbol
            {
                SymbolName = "raw",
                SymbolType = SymbolType.Property,
                Language = "json",
                FilePath = "postman/test.json",
                StartLine = i,
                EndLine = i,
                StartByte = 0,
                EndByte = 10
            }).ToList();

            // Insert 1 C# symbol named "raw"
            var csharpSymbols = new List<Symbol>
            {
                new()
                {
                    SymbolName = "raw",
                    SymbolType = SymbolType.Method,
                    Language = "csharp",
                    FilePath = "src/Service.cs",
                    StartLine = 10,
                    EndLine = 15,
                    StartByte = 100,
                    EndByte = 200
                }
            };

            db.Symbols.UpsertFile("postman/test.json", jsonSymbols);
            db.Symbols.UpsertFile("src/Service.cs", csharpSymbols);

            // 1. Query specifically for csharp with limit 500
            var csharpResults = db.Symbols.FindByName("raw", limit: 500, language: "csharp");
            Assert.Single(csharpResults);
            Assert.Equal("csharp", csharpResults[0].Language);
            Assert.Equal("src/Service.cs", csharpResults[0].FilePath);

            // 2. Query with limit 0 (unlimited) across all languages
            var allResults = db.Symbols.FindByName("raw", limit: 0);
            Assert.Equal(601, allResults.Count);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            try { if (Directory.Exists(tempDir)) Directory.Delete(tempDir, recursive: true); } catch { }
        }
    }
}

