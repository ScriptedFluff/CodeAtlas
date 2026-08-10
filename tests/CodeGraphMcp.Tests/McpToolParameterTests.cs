using CodeGraphMcp.CrossStack;
using CodeGraphMcp.Db;
using CodeGraphMcp.Git;
using CodeGraphMcp.Indexer;
using CodeGraphMcp.Mcp.Tools;
using CodeGraphMcp.Parser;
using Microsoft.Data.Sqlite;
using Xunit;

namespace CodeGraphMcp.Tests;

public class McpToolParameterTests
{
    [Fact]
    public async Task McpTools_WithTargetDirectory_ExecuteSuccessfullyOnTargetWorkspace()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "McpToolParamTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        File.WriteAllText(Path.Combine(tempDir, "OrderService.cs"), """
            namespace Sample;
            public class OrderService
            {
                public void CreateOrder() {}
            }
            """);

        using var dbManager = new WorkspaceDbManager();
        var workspaceService = new DynamicWorkspaceService(dbManager);
        var parsers = new ILanguageParser[] { new CSharpParser(), new TypeScriptParser(), new HtmlParser(), new PowerShellParser() };
        var bufferManager = new WorkspaceBufferManager(parsers);

        try
        {
            // 1. GetSymbolDefinitionTool with targetDirectory
            var defTool = new GetSymbolDefinitionTool(workspaceService, bufferManager);
            var defResult = await defTool.GetSymbolDefinitionAsync("OrderService", targetDirectory: tempDir);
            Assert.NotNull(defResult);
            Assert.Equal("OrderService", defResult.SymbolName);

            // 2. SearchCodebaseGraphTool with targetDirectory
            var searchTool = new SearchCodebaseGraphTool(workspaceService, bufferManager);
            var searchResult = await searchTool.SearchCodebaseGraphAsync("Order%", targetDirectory: tempDir);
            Assert.NotEmpty(searchResult.Matches);
            Assert.Contains(searchResult.Matches, m => m.SymbolName == "OrderService");

            // 3. FindSymbolCallersTool with targetDirectory
            var callersTool = new FindSymbolCallersTool(workspaceService);
            var callersResult = await callersTool.FindSymbolCallersAsync("CreateOrder", targetDirectory: tempDir);
            Assert.NotNull(callersResult);
            Assert.Equal("CreateOrder", callersResult.TargetSymbol);

            // 4. GetDiffImpactTool with targetDirectory
            var diffTool = new GetDiffImpactTool(workspaceService);
            var diffResult = await diffTool.GetDiffImpactAsync(targetDirectory: tempDir);
            Assert.NotNull(diffResult);

            // 5. GetServiceStackDtoContractTool with targetDirectory & WorkspaceLinkerManager
            var linkerManager = new WorkspaceLinkerManager();
            var dtoTool = new GetServiceStackDtoContractTool(workspaceService, linkerManager);
            var dtoResult = await dtoTool.GetServiceStackDtoContractAsync("CreateOrderRequest", targetDirectory: tempDir);
            Assert.NotNull(dtoResult);

            var context = await workspaceService.GetOrIndexWorkspaceAsync(tempDir);
            var linker1 = linkerManager.GetOrCreateLinker(context.WorkspaceRoot, context.Db);
            var linker2 = linkerManager.GetOrCreateLinker(context.WorkspaceRoot, context.Db);
            Assert.Same(linker1, linker2);
        }
        finally
        {
            dbManager.Dispose();
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, recursive: true);
        }
    }
}
