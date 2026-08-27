using CodeAtlas.Core.Config;
using Xunit;

namespace CodeAtlas.Tests;

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
