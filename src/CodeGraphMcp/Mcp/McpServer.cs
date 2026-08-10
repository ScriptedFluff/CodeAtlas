using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using CodeGraphMcp.CrossStack;
using CodeGraphMcp.Db;
using CodeGraphMcp.Git;
using CodeGraphMcp.Parser;
using ModelContextProtocol.Server;

namespace CodeGraphMcp.Mcp;

/// <summary>Carries the repository root path into DI without string-keyed services.</summary>
public sealed record RepoContext(string RepoPath);

public static class McpServer
{
    public static async Task RunAsync(
        string repoPath,
        string dbPath,
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
            .AddSingleton<CodeGraphDb>(_ =>
            {
                var db = new CodeGraphDb(dbPath);
                db.Migrate();
                return db;
            })
            .AddSingleton(sp =>
            {
                var ps  = sp.GetRequiredService<IReadOnlyList<ILanguageParser>>();
                var buf = new DirtyFileBuffer(repoPath, ps);
                buf.Refresh();  // warm cache before first tool call
                return buf;
            })
            .AddSingleton(sp => new ServiceStackLinker(sp.GetRequiredService<CodeGraphDb>()))
            .AddMcpServer()
            .WithStdioServerTransport()
            // Explicit generic registration with AOT-safe JSON options
            .WithTools<Tools.GetSymbolDefinitionTool>(jsonOptions)
            .WithTools<Tools.FindSymbolCallersTool>(jsonOptions)
            .WithTools<Tools.SearchCodebaseGraphTool>(jsonOptions)
            .WithTools<Tools.GetDiffImpactTool>(jsonOptions)
            .WithTools<Tools.GetServiceStackDtoContractTool>(jsonOptions);

        var host = builder.Build();

        // Eagerly warm the dirty-file buffer BEFORE the host starts accepting connections.
        // DirtyFileBuffer.Refresh() calls `git status` which can take 2-4s.
        // Without eager pre-warming, the first tool call that needs it triggers this delay,
        // and if stdin closes in the meantime the response gets dropped in the race.
        host.Services.GetRequiredService<DirtyFileBuffer>();

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
