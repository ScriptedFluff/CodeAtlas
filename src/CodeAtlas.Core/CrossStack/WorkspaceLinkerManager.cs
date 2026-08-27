using System.Collections.Concurrent;
using CodeAtlas.Core.Db;

namespace CodeAtlas.Core.CrossStack;

public interface IWorkspaceLinkerManager
{
    ServiceStackLinker GetOrCreateLinker(string workspaceRoot, CodeAtlasDb db);
}

public sealed class WorkspaceLinkerManager : IWorkspaceLinkerManager
{
    private readonly ConcurrentDictionary<string, ServiceStackLinker> _linkers = new(StringComparer.OrdinalIgnoreCase);

    public ServiceStackLinker GetOrCreateLinker(string workspaceRoot, CodeAtlasDb db) =>
        _linkers.GetOrAdd(workspaceRoot, _ => new ServiceStackLinker(db));
}
