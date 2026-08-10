namespace CodeGraphMcp.Indexer;

using CodeGraphMcp.Config;

public static class SolutionRootResolver
{
    public static string ResolveRoot(string targetDirectory)
    {
        return WorkspacePathResolver.FindWorkspaceRoot(targetDirectory);
    }
}
