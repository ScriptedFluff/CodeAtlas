using CodeAtlas.Core.Config;
using CodeAtlas.Core.Git;
using CodeAtlas.Parser;
using Xunit;

namespace CodeAtlas.Tests;

public class WorkspaceBufferManagerTests
{
    [Fact]
    public void GetOrCreateBuffer_SameWorkspace_ReturnsSameInstance()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "BufferTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        var parsers = new ILanguageParser[] { new CSharpParser() };
        var manager = new WorkspaceBufferManager(parsers);

        try
        {
            var buf1 = manager.GetOrCreateBuffer(tempDir);
            var buf2 = manager.GetOrCreateBuffer(tempDir);

            Assert.Same(buf1, buf2);
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void GetOrCreateBuffer_DifferentWorkspaces_ReturnsDistinctInstances()
    {
        var tempDir1 = Path.Combine(Path.GetTempPath(), "BufferTest1_" + Guid.NewGuid().ToString("N"));
        var tempDir2 = Path.Combine(Path.GetTempPath(), "BufferTest2_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir1);
        Directory.CreateDirectory(tempDir2);

        var parsers = new ILanguageParser[] { new CSharpParser() };
        var manager = new WorkspaceBufferManager(parsers);

        try
        {
            var buf1 = manager.GetOrCreateBuffer(tempDir1);
            var buf2 = manager.GetOrCreateBuffer(tempDir2);

            Assert.NotSame(buf1, buf2);
        }
        finally
        {
            if (Directory.Exists(tempDir1)) Directory.Delete(tempDir1, recursive: true);
            if (Directory.Exists(tempDir2)) Directory.Delete(tempDir2, recursive: true);
        }
    }
}
