using System.Collections.Concurrent;
using CodeGraphMcp.Db;

namespace CodeGraphMcp.CrossStack;

public interface IWorkspaceLinkerManager
{
    ServiceStackLinker GetOrCreateLinker(string workspaceRoot, CodeGraphDb db);
}

public sealed class WorkspaceLinkerManager : IWorkspaceLinkerManager
{
    private readonly ConcurrentDictionary<string, ServiceStackLinker> _linkers = new(StringComparer.OrdinalIgnoreCase);

    public ServiceStackLinker GetOrCreateLinker(string workspaceRoot, CodeGraphDb db) =>
        _linkers.GetOrAdd(workspaceRoot, _ => new ServiceStackLinker(db));
}
