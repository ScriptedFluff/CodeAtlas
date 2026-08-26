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
    private readonly IParserRegistry _parserRegistry;
    private readonly ConcurrentDictionary<string, Lazy<DirtyFileBuffer>> _buffers = new(StringComparer.OrdinalIgnoreCase);

    public WorkspaceBufferManager(IParserRegistry parserRegistry)
    {
        _parserRegistry = parserRegistry;
    }

    public WorkspaceBufferManager(IReadOnlyList<ILanguageParser> parsers)
        : this(new ParserRegistry(parsers))
    {
    }

    public DirtyFileBuffer GetOrCreateBuffer(string targetDirectory)
    {
        var workspaceRoot = WorkspacePathResolver.FindWorkspaceRoot(targetDirectory);

        var lazyBuf = _buffers.GetOrAdd(workspaceRoot, rootKey => new Lazy<DirtyFileBuffer>(() =>
        {
            var buf = new DirtyFileBuffer(rootKey, _parserRegistry);
            buf.Refresh();
            return buf;
        }, LazyThreadSafetyMode.ExecutionAndPublication));

        return lazyBuf.Value;
    }
}
