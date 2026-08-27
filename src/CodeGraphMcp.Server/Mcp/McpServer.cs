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
        // Safeguard: Redirect standard Console.Out to Console.Error so any stray Console.WriteLine calls go to stderr.
        // The MCP stdio server transport opens and writes directly to Console.OpenStandardOutput().
        Console.SetOut(Console.Error);

        var parsers = GetParsers();

        var builder = Host.CreateApplicationBuilder();

        // Route all framework and application logs exclusively to stderr.
        // stdout is strictly reserved for the JSON-RPC stdio transport pipe.
        builder.Logging.ClearProviders();
        builder.Logging.AddConsole(options =>
        {
            options.LogToStandardErrorThreshold = LogLevel.Trace;
        });
        builder.Logging.SetMinimumLevel(LogLevel.Warning);

        var jsonOptions = new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web);
        jsonOptions.TypeInfoResolverChain.Add(McpJsonContext.Default);

        builder.Services
            .AddSingleton<RepoContext>(_ => new RepoContext(repoPath))
            .AddSingleton(_ => parsers)
            .AddSingleton<IParserRegistry>(sp => new ParserRegistry(sp.GetRequiredService<IReadOnlyList<ILanguageParser>>()))
            .AddSingleton<IWorkspaceDbManager>(_ => new WorkspaceDbManager(dbDirOverride: dbPath))
            .AddSingleton<IWorkspaceBufferManager>(sp =>
                new WorkspaceBufferManager(sp.GetRequiredService<IParserRegistry>()))
            .AddSingleton<IWorkspaceLinkerManager, WorkspaceLinkerManager>()
            .AddSingleton<ISingleFileIndexer>(sp =>
                new SingleFileIndexer(sp.GetRequiredService<IParserRegistry>()))
            .AddSingleton<IDynamicWorkspaceService>(sp =>
                new DynamicWorkspaceService(sp.GetRequiredService<IWorkspaceDbManager>(), repoPath, sp.GetRequiredService<IParserRegistry>()))
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
            .WithTools<Tools.GetServiceStackDtoContractTool>(jsonOptions)
            .WithTools<Tools.GetSupportedLanguagesTool>(jsonOptions)
            .WithTools<Tools.SyncFileTool>(jsonOptions)
            .WithTools<Tools.SyncGitDiffTool>(jsonOptions);

        var host = builder.Build();

        if (!string.IsNullOrWhiteSpace(repoPath))
        {
            // Eagerly warm the dirty-file buffer BEFORE the host starts accepting connections in bound mode.
            host.Services.GetRequiredService<DirtyFileBuffer>();
        }

        // Trigger silent non-blocking background startup GC to purge orphaned .db files with configurable timeout
        _ = Task.Run(async () =>
        {
            try
            {
                var timeoutStr = Environment.GetEnvironmentVariable("CODEGRAPH_STARTUP_GC_TIMEOUT_MS");
                var timeoutMs = int.TryParse(timeoutStr, out var parsed) && parsed > 0 ? parsed : 5000;

                using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(timeoutMs));
                var pruner = new Tools.PruneIndexedWorktreesTool();
                await pruner.PruneIndexedWorktreesAsync(dryRun: false, ct: cts.Token);
            }
            catch
            {
                // Silent fail-safe: startup GC should never crash the server process
            }
        }, CancellationToken.None);

        await host.RunAsync(ct);
    }

    private static IReadOnlyList<ILanguageParser> GetParsers() =>
        ParserRegistry.CreateDefault().GetAllParsers();
}
