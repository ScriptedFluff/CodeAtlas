using CodeAtlas.Core.Indexer;
using Xunit;

namespace CodeAtlas.Tests;

public class SolutionRootResolverTests
{
    [Fact]
    public void ResolveRoot_WithSlnFile_FindsTopLevelRoot()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "SlnRootTest_" + Guid.NewGuid().ToString("N"));
        var projDir = Path.Combine(tempDir, "src", "MyProject");
        Directory.CreateDirectory(projDir);
        File.WriteAllText(Path.Combine(tempDir, "MySolution.sln"), "");
        File.WriteAllText(Path.Combine(projDir, "MyProject.csproj"), "<Project />");

        try
        {
            var root = SolutionRootResolver.ResolveRoot(projDir);
            Assert.Equal(WorkspacePathNormalizer.NormalizeDirectoryPath(tempDir), WorkspacePathNormalizer.NormalizeDirectoryPath(root));
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void WorkspacePathNormalizer_NormalizeRelativePath_UsesForwardSlashes()
    {
        var root = @"C:\Projects\SolutionRoot";
        var file = @"C:\Projects\SolutionRoot\src\ProjectA\Service.cs";

        var rel = WorkspacePathNormalizer.NormalizeRelativePath(root, file);

        Assert.Equal("src/ProjectA/Service.cs", rel);
    }

    [Fact]
    public void WorkspacePathNormalizer_NormalizeDirectoryPath_TrimsSlashes()
    {
        var path = @"C:\Projects\SolutionRoot\";
        var normalized = WorkspacePathNormalizer.NormalizeDirectoryPath(path);

        Assert.False(normalized.EndsWith(@"\"));
        Assert.False(normalized.EndsWith("/"));
    }
}
