namespace CodeAtlas.Core.Indexer;

using CodeAtlas.Core.Config;

public static class SolutionRootResolver
{
    public static string ResolveRoot(string targetDirectory)
    {
        return WorkspacePathResolver.FindWorkspaceRoot(targetDirectory);
    }
}
