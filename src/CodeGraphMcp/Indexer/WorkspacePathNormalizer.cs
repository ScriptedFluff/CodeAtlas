namespace CodeGraphMcp.Indexer;

public static class WorkspacePathNormalizer
{
    public static string NormalizeRelativePath(string rootPath, string fullPath)
    {
        var relative = Path.GetRelativePath(rootPath, fullPath);
        return relative.Replace('\\', '/');
    }

    public static string NormalizeDirectoryPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return string.Empty;
        var full = Path.GetFullPath(path);
        return full.Replace('\\', '/').TrimEnd('/');
    }
}
