using System.Collections.Concurrent;
using CodeGraphMcp.Config;
using CodeGraphMcp.Db;
using CodeGraphMcp.Parser;
using Microsoft.Extensions.Logging;

namespace CodeGraphMcp.Indexer;

public record WorkspaceContext(string WorkspaceRoot, string TargetDirectory, CodeGraphDb Db);

public interface IDynamicWorkspaceService
{
    Task<WorkspaceContext> GetOrIndexWorkspaceAsync(string? targetDirectory, CancellationToken ct = default);
    Task<WorkspaceContext> EnsureIndexedAsync(string targetDirectory, CancellationToken ct = default);
}

public sealed class DynamicWorkspaceService : IDynamicWorkspaceService
{
    private readonly IWorkspaceDbManager _dbManager;
    private readonly IParserRegistry _parserRegistry;
    private readonly string? _defaultRepoPath;
    private readonly ILogger<DynamicWorkspaceService>? _logger;
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new(StringComparer.OrdinalIgnoreCase);

    public DynamicWorkspaceService(
        IWorkspaceDbManager dbManager,
        string? defaultRepoPath = null,
        IParserRegistry? parserRegistry = null,
        ILogger<DynamicWorkspaceService>? logger = null)
    {
        _dbManager = dbManager;
        _defaultRepoPath = defaultRepoPath;
        _parserRegistry = parserRegistry ?? ParserRegistry.CreateDefault();
        _logger = logger;
    }

    public async Task<WorkspaceContext> GetOrIndexWorkspaceAsync(string? targetDirectory, CancellationToken ct = default)
    {
        var effectivePath = !string.IsNullOrWhiteSpace(targetDirectory)
            ? targetDirectory
            : !string.IsNullOrWhiteSpace(_defaultRepoPath)
                ? _defaultRepoPath
                : Environment.CurrentDirectory;

        return await EnsureIndexedAsync(effectivePath, ct);
    }

    public async Task<WorkspaceContext> EnsureIndexedAsync(string targetDirectory, CancellationToken ct = default)
    {
        var normalizedTarget = WorkspacePathResolver.NormalizePath(targetDirectory);
        var workspaceRoot = _dbManager.ResolveWorkspaceRoot(normalizedTarget);
        var db = _dbManager.GetOrCreateDb(workspaceRoot);

        var gate = _locks.GetOrAdd(workspaceRoot, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct);

        try
        {
            var symbolCount = db.Symbols.GetCount();
            if (symbolCount == 0)
            {
                _logger?.LogInformation("Workspace {WorkspaceRoot} is un-indexed. Initializing index...", workspaceRoot);
                var orchestrator = new IndexOrchestrator(db, _parserRegistry);
                var result = await orchestrator.IndexAsync(workspaceRoot, ct);
                _logger?.LogInformation("Auto-indexed {Symbols} symbols across {Files} files in {Ms}ms for {WorkspaceRoot}",
                    result.SymbolsWritten, result.FilesProcessed, result.ElapsedMs, workspaceRoot);
            }
        }
        finally
        {
            gate.Release();
        }

        return new WorkspaceContext(workspaceRoot, normalizedTarget, db);
    }
}
