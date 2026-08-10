using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using CodeGraphMcp.CrossStack;
using CodeGraphMcp.Db;
using CodeGraphMcp.Git;
using CodeGraphMcp.Indexer;
using CodeGraphMcp.Parser;
using ModelContextProtocol.Server;

namespace CodeGraphMcp.Mcp;

/// <summary>Carries the repository root path into DI without string-keyed services.</summary>
public sealed record RepoContext(string? RepoPath);

public static class McpServer
{
    public static async Task RunAsync(
        string? repoPath = null,
        string? dbPath = null,
        CancellationToken ct = default)
    {
        var parsers = GetParsers();

        var builder = Host.CreateApplicationBuilder();

        // Suppress all framework logs below Warning.
        // The MCP SDK routes remaining logs to stderr — never stdout —
        // so the JSON-RPC pipe on stdout stays clean.
        builder.Logging.SetMinimumLevel(LogLevel.Warning);

        var jsonOptions = new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web);
        jsonOptions.TypeInfoResolverChain.Add(McpJsonContext.Default);

        builder.Services
            .AddSingleton<RepoContext>(_ => new RepoContext(repoPath))
            .AddSingleton(_ => parsers)
            .AddSingleton<IWorkspaceDbManager>(_ => new WorkspaceDbManager(dbDirOverride: dbPath))
            .AddSingleton<IWorkspaceBufferManager>(sp =>
                new WorkspaceBufferManager(sp.GetRequiredService<IReadOnlyList<ILanguageParser>>()))
            .AddSingleton<IWorkspaceLinkerManager, WorkspaceLinkerManager>()
            .AddSingleton<IDynamicWorkspaceService>(sp =>
                new DynamicWorkspaceService(sp.GetRequiredService<IWorkspaceDbManager>(), repoPath))
            .AddSingleton(sp =>
            {
                var dbManager = sp.GetRequiredService<IWorkspaceDbManager>();
                var effectiveRepo = !string.IsNullOrWhiteSpace(repoPath) ? repoPath : Environment.CurrentDirectory;
                return dbManager.GetOrCreateDb(effectiveRepo);
            })
            .AddSingleton(sp =>
            {
                var bufferManager = sp.GetRequiredService<IWorkspaceBufferManager>();
                var effectiveRepo = !string.IsNullOrWhiteSpace(repoPath) ? repoPath : Environment.CurrentDirectory;
                return bufferManager.GetOrCreateBuffer(effectiveRepo);
            })
            .AddMcpServer()
            .WithStdioServerTransport()
            // Explicit generic registration with AOT-safe JSON options
            .WithTools<Tools.GetSymbolDefinitionTool>(jsonOptions)
            .WithTools<Tools.FindSymbolCallersTool>(jsonOptions)
            .WithTools<Tools.SearchCodebaseGraphTool>(jsonOptions)
            .WithTools<Tools.GetDiffImpactTool>(jsonOptions)
            .WithTools<Tools.GetServiceStackDtoContractTool>(jsonOptions);

        var host = builder.Build();

        if (!string.IsNullOrWhiteSpace(repoPath))
        {
            // Eagerly warm the dirty-file buffer BEFORE the host starts accepting connections in bound mode.
            host.Services.GetRequiredService<DirtyFileBuffer>();
        }

        await host.RunAsync(ct);
    }

    private static IReadOnlyList<ILanguageParser> GetParsers() =>
    [
        new CSharpParser(),
        new TypeScriptParser(),
        new HtmlParser(),
        new PowerShellParser()
    ];
}
