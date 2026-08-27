using CodeAtlas.Core.Config;
using Xunit;

namespace CodeAtlas.Tests;

public class WorkspacePathResolverTests
{
    [Fact]
    public void FindWorkspaceRoot_WithPackageJson_DetectsBoundary()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "ResolverTest_" + Guid.NewGuid().ToString("N"));
        var subDir = Path.Combine(tempDir, "src", "components");
        Directory.CreateDirectory(subDir);
        File.WriteAllText(Path.Combine(tempDir, "package.json"), "{}");

        try
        {
            var root = WorkspacePathResolver.FindWorkspaceRoot(subDir);
            Assert.Equal(WorkspacePathResolver.NormalizePath(tempDir), root);
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void FindWorkspaceRoot_WithCsProj_DetectsBoundary()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "ResolverTest_CsProj_" + Guid.NewGuid().ToString("N"));
        var subDir = Path.Combine(tempDir, "Controllers");
        Directory.CreateDirectory(subDir);
        File.WriteAllText(Path.Combine(tempDir, "TestApp.csproj"), "<Project />");

        try
        {
            var root = WorkspacePathResolver.FindWorkspaceRoot(subDir);
            Assert.Equal(WorkspacePathResolver.NormalizePath(tempDir), root);
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void FindWorkspaceRoot_WithTsConfig_DetectsBoundary()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "ResolverTest_TsConfig_" + Guid.NewGuid().ToString("N"));
        var subDir = Path.Combine(tempDir, "src");
        Directory.CreateDirectory(subDir);
        File.WriteAllText(Path.Combine(tempDir, "tsconfig.json"), "{}");

        try
        {
            var root = WorkspacePathResolver.FindWorkspaceRoot(subDir);
            Assert.Equal(WorkspacePathResolver.NormalizePath(tempDir), root);
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void NormalizePath_ReturnsCleanAbsolutePath()
    {
        var current = Environment.CurrentDirectory;
        var normalized = WorkspacePathResolver.NormalizePath(current + "\\");
        Assert.False(normalized.EndsWith("\\"));
        Assert.False(normalized.EndsWith("/"));
    }
}
