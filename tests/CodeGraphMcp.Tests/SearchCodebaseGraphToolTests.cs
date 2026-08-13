using CodeGraphMcp.Db;
using CodeGraphMcp.Git;
using CodeGraphMcp.Indexer;
using CodeGraphMcp.Mcp.Tools;
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
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, recursive: true);
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
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, recursive: true);
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
            Assert.Equal("Class", fullResult.Matches[1].SymbolType);
        }
        finally
        {
            dbManager.Dispose();
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, recursive: true);
        }
    }
}
