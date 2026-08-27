namespace CodeGraphMcp.Indexer;

public static class WorkspacePathNormalizer
{
    public static string NormalizeRelativePath(string rootPath, string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            return string.Empty;

        var fullPath = Path.IsPathRooted(filePath)
            ? Path.GetFullPath(filePath)
            : Path.GetFullPath(Path.Combine(rootPath, filePath));

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
