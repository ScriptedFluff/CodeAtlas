using System.Collections.Concurrent;
using CodeGraphMcp.Config;
using CodeGraphMcp.Parser;

namespace CodeGraphMcp.Git;

public interface IWorkspaceBufferManager
{
    DirtyFileBuffer GetOrCreateBuffer(string targetDirectory);
}

public sealed class WorkspaceBufferManager : IWorkspaceBufferManager
{
    private readonly IReadOnlyList<ILanguageParser> _parsers;
    private readonly ConcurrentDictionary<string, Lazy<DirtyFileBuffer>> _buffers = new(StringComparer.OrdinalIgnoreCase);

    public WorkspaceBufferManager(IReadOnlyList<ILanguageParser> parsers)
    {
        _parsers = parsers;
    }

    public DirtyFileBuffer GetOrCreateBuffer(string targetDirectory)
    {
        var workspaceRoot = WorkspacePathResolver.FindWorkspaceRoot(targetDirectory);

        var lazyBuf = _buffers.GetOrAdd(workspaceRoot, rootKey => new Lazy<DirtyFileBuffer>(() =>
        {
            var buf = new DirtyFileBuffer(rootKey, _parsers);
            buf.Refresh();
            return buf;
        }, LazyThreadSafetyMode.ExecutionAndPublication));

        return lazyBuf.Value;
    }
}
