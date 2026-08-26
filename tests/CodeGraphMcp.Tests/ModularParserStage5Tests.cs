using CodeGraphMcp.Db;
using CodeGraphMcp.Git;
using CodeGraphMcp.Indexer;
using CodeGraphMcp.Mcp.Tools;
using CodeGraphMcp.Parser;
using CodeGraphMcp.Plugins;
using Microsoft.Data.Sqlite;
using Xunit;

namespace CodeGraphMcp.Tests;

public class ModularParserStage5Tests
{
    [Fact]
    public async Task GetSupportedLanguagesTool_ReturnsAllRegisteredDynamicParsers()
    {
        var registry = ParserRegistry.CreateDefault();
        var tool = new GetSupportedLanguagesTool(registry);

        var result = await tool.GetSupportedLanguagesAsync();

        Assert.NotNull(result);
        Assert.NotNull(result.SupportedLanguages);
        Assert.True(result.SupportedLanguages.Count >= 8);

        var csharp = Assert.Single(result.SupportedLanguages, l => l.Language.Equals("csharp", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(".cs", csharp.FileExtensions);

        var ts = Assert.Single(result.SupportedLanguages, l => l.Language.Equals("typescript", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(".ts", ts.FileExtensions);
        Assert.Contains(".tsx", ts.FileExtensions);

        var ps = Assert.Single(result.SupportedLanguages, l => l.Language.Equals("powershell", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(".ps1", ps.FileExtensions);

        var json = Assert.Single(result.SupportedLanguages, l => l.Language.Equals("json", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(".json", json.FileExtensions);
    }

    [Fact]
    public async Task DynamicWorkspaceService_WithDynamicParsers_IndexesMultiLanguageRepo()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"cg_stage5_multi_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        File.WriteAllText(Path.Combine(tempDir, "Order.cs"), """
            namespace Store;
            public class OrderEntity {
                public void Process() {}
            }
            """);

        File.WriteAllText(Path.Combine(tempDir, "Payment.ts"), """
            export interface IPaymentGateway {
                charge(amount: number): boolean;
            }
            """);

        File.WriteAllText(Path.Combine(tempDir, "deploy.ps1"), """
            function Start-Deployment {
                param([string]$Env)
            }
            """);

        File.WriteAllText(Path.Combine(tempDir, "config.json"), """
            {
                "serviceName": "OrderProcessor",
                "port": 5000
            }
            """);

        using var dbManager = new WorkspaceDbManager();
        var registry = ParserRegistry.CreateDefault();
        var workspaceService = new DynamicWorkspaceService(dbManager, parserRegistry: registry);

        try
        {
            var context = await workspaceService.EnsureIndexedAsync(tempDir);
            Assert.NotNull(context);
            Assert.NotNull(context.Db);

            var symbols = context.Db.Symbols.FindByName("");
            Assert.NotEmpty(symbols);

            // C#
            Assert.Contains(symbols, s => s.SymbolName == "OrderEntity" && s.Language == "csharp");
            // TypeScript
            Assert.Contains(symbols, s => s.SymbolName == "IPaymentGateway" && s.Language == "typescript");
            // PowerShell
            Assert.Contains(symbols, s => s.SymbolName == "Start-Deployment" && s.Language == "powershell");
            // JSON
            Assert.Contains(symbols, s => s.SymbolName == "serviceName" && s.Language == "json");
        }
        finally
        {
            dbManager.Dispose();
            SqliteConnection.ClearAllPools();
            try
            {
                if (Directory.Exists(tempDir))
                {
                    Directory.Delete(tempDir, recursive: true);
                }
            }
            catch { }
        }
    }

    [Fact]
    public async Task SearchCodebaseGraphTool_WithDynamicParsers_FindsSymbolsAcrossLanguages()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"cg_stage5_search_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        File.WriteAllText(Path.Combine(tempDir, "Customer.cs"), """
            namespace Accounts;
            public class CustomerModel {}
            """);

        File.WriteAllText(Path.Combine(tempDir, "CustomerService.ts"), """
            export class CustomerApiClient {}
            """);

        using var dbManager = new WorkspaceDbManager();
        var registry = ParserRegistry.CreateDefault();
        var workspaceService = new DynamicWorkspaceService(dbManager, parserRegistry: registry);
        var bufferManager = new WorkspaceBufferManager(registry);

        try
        {
            var searchTool = new SearchCodebaseGraphTool(workspaceService, bufferManager);

            var searchResult = await searchTool.SearchCodebaseGraphAsync("Customer%", targetDirectory: tempDir);
            Assert.NotNull(searchResult);
            Assert.True(searchResult.Matches.Count >= 2);

            Assert.Contains(searchResult.Matches, m => m.SymbolName == "CustomerModel" && m.Language == "csharp");
            Assert.Contains(searchResult.Matches, m => m.SymbolName == "CustomerApiClient" && m.Language == "typescript");
        }
        finally
        {
            dbManager.Dispose();
            SqliteConnection.ClearAllPools();
            try
            {
                if (Directory.Exists(tempDir))
                {
                    Directory.Delete(tempDir, recursive: true);
                }
            }
            catch { }
        }
    }
}
