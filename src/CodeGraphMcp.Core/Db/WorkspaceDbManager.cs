using System.Collections.Concurrent;
using CodeGraphMcp.Config;

namespace CodeGraphMcp.Db;

public sealed class WorkspaceDbManager : IWorkspaceDbManager
{
    private readonly string? _dbDirOverride;
    private readonly ConcurrentDictionary<string, Lazy<CodeGraphDb>> _databases = new(StringComparer.OrdinalIgnoreCase);
    private bool _disposed;

    public WorkspaceDbManager(string? dbDirOverride = null)
    {
        _dbDirOverride = dbDirOverride;
    }

    public string ResolveWorkspaceRoot(string targetDirectory)
    {
        if (string.IsNullOrWhiteSpace(targetDirectory))
            targetDirectory = Environment.CurrentDirectory;

        return WorkspacePathResolver.FindWorkspaceRoot(targetDirectory);
    }

    public CodeGraphDb GetOrCreateDb(string targetDirectory)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var workspaceRoot = ResolveWorkspaceRoot(targetDirectory);

        var lazyDb = _databases.GetOrAdd(workspaceRoot, rootKey => new Lazy<CodeGraphDb>(() =>
        {
            var dbPath = StorageConfig.ResolveDbPath(_dbDirOverride, rootKey);
            var db = new CodeGraphDb(dbPath);
            db.Migrate();
            return db;
        }, LazyThreadSafetyMode.ExecutionAndPublication));

        return lazyDb.Value;
    }

    public IReadOnlyCollection<string> ActiveWorkspaces => _databases.Keys.ToList();

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        foreach (var entry in _databases.Values)
        {
            if (entry.IsValueCreated)
            {
                entry.Value.Dispose();
            }
        }
        _databases.Clear();
    }
}
