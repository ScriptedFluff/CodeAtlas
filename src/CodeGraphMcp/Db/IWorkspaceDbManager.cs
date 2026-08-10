namespace CodeGraphMcp.Db;

public interface IWorkspaceDbManager : IDisposable
{
    /// <summary>
    /// Returns an open, migrated CodeGraphDb instance for the specified target directory.
    /// Thread-safe and cached per canonical workspace root.
    /// </summary>
    CodeGraphDb GetOrCreateDb(string targetDirectory);

    /// <summary>
    /// Resolves the canonical workspace root directory for a given target path (e.g. Git root).
    /// </summary>
    string ResolveWorkspaceRoot(string targetDirectory);

    /// <summary>
    /// Gets the list of canonical workspace root paths currently cached in memory.
    /// </summary>
    IReadOnlyCollection<string> ActiveWorkspaces { get; }
}
