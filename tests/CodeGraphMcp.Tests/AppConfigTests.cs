using CodeGraphMcp.Config;
using Xunit;

namespace CodeGraphMcp.Tests;

public class AppConfigTests
{
    [Fact]
    public void AppConfig_ContainsDefaultIgnoreForWwwRootLibAndLib()
    {
        var config = AppConfig.Load();

        Assert.Contains("wwwroot/lib", config.IgnoreDirectories);
        Assert.Contains("lib", config.IgnoreDirectories);
        Assert.Contains("**/wwwroot/lib/**", config.IgnorePatterns);
    }
}
