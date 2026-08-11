using CodeGraphMcp.Config;
using Xunit;

namespace CodeGraphMcp.Tests;

public sealed class CommandLineSanitizerTests
{
    [Fact]
    public void SanitizeArgs_HandlesTrailingBackslashQuoteEscaping()
    {
        string[] rawArgs = ["index", "D:\\Work\\Clever Services\\master\" --force-full"];
        var sanitized = CommandLineSanitizer.SanitizeArgs(rawArgs);

        Assert.Equal(3, sanitized.Length);
        Assert.Equal("index", sanitized[0]);
        Assert.Equal("D:\\Work\\Clever Services\\master", sanitized[1]);
        Assert.Equal("--force-full", sanitized[2]);
    }

    [Fact]
    public void SanitizeArgs_HandlesMultipleFlagsInCollapsedArgument()
    {
        string[] rawArgs = ["index", "D:\\Work\\Clever Services\\master\" --force-full --no-ignore"];
        var sanitized = CommandLineSanitizer.SanitizeArgs(rawArgs);

        Assert.Equal(4, sanitized.Length);
        Assert.Equal("index", sanitized[0]);
        Assert.Equal("D:\\Work\\Clever Services\\master", sanitized[1]);
        Assert.Equal("--force-full", sanitized[2]);
        Assert.Equal("--no-ignore", sanitized[3]);
    }

    [Fact]
    public void SanitizeArgs_LeavesNormalArgumentsUnchanged()
    {
        string[] rawArgs = ["index", "D:\\Work\\Clever Services\\master", "--force-full"];
        var sanitized = CommandLineSanitizer.SanitizeArgs(rawArgs);

        Assert.Equal(3, sanitized.Length);
        Assert.Equal("index", sanitized[0]);
        Assert.Equal("D:\\Work\\Clever Services\\master", sanitized[1]);
        Assert.Equal("--force-full", sanitized[2]);
    }
}
