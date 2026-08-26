using CodeGraphMcp.Commands;
using CodeGraphMcp.Plugins;
using Xunit;

namespace CodeGraphMcp.Tests;

public class ModularParserStage4Tests
{
    [Fact]
    public void ListParsers_ReturnsZero_AndOutputsParsers()
    {
        var exitCode = ParsersCommandHandler.ListParsers(json: false, verbose: true);
        Assert.Equal(0, exitCode);
    }

    [Fact]
    public void ListParsers_JsonMode_ReturnsZero()
    {
        var exitCode = ParsersCommandHandler.ListParsers(json: true);
        Assert.Equal(0, exitCode);
    }

    [Fact]
    public void ShowParserInfo_WithValidId_ReturnsZero()
    {
        var exitCode = ParsersCommandHandler.ShowParserInfo("csharp", json: false);
        Assert.Equal(0, exitCode);
    }

    [Fact]
    public void ShowParserInfo_WithInvalidId_ReturnsOne()
    {
        var exitCode = ParsersCommandHandler.ShowParserInfo("nonexistent_parser_12345", json: false);
        Assert.Equal(1, exitCode);
    }

    [Fact]
    public void EnableAndDisable_MovesFolderBetweenParsersAndDisabledParsers()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), $"cg_stage4_test_{Guid.NewGuid():N}");
        var userParsers = Path.Combine(tempRoot, "user", "Parsers");
        var dummyParserDir = Path.Combine(userParsers, "MockLang");
        Directory.CreateDirectory(dummyParserDir);

        // Copy a valid parser dll (e.g. Json parser) into mock dir
        var jsonParserDll = typeof(Parser.JsonParser).Assembly.Location;
        File.Copy(jsonParserDll, Path.Combine(dummyParserDir, "MockLang.dll"));

        try
        {
            var resolver = new ParserDirectoryResolver(customUserDir: userParsers);

            // Verify active initially
            using (var loader = new DirectoryParserPluginLoader(resolver))
            {
                var initialResult = loader.DiscoverAndLoad();
                Assert.Contains(initialResult.Evaluations, e => e.Candidate.Id.Equals("json", StringComparison.OrdinalIgnoreCase));
            }

            // Disable parser
            var disableExit = ParsersCommandHandler.DisableParser("MockLang", resolver);
            Assert.Equal(0, disableExit);

            var userDisabled = resolver.GetUserDisabledParsersDirectory();
            Assert.True(Directory.Exists(Path.Combine(userDisabled, "MockLang")), "Expected MockLang folder in DisabledParsers");
            Assert.False(Directory.Exists(dummyParserDir), "MockLang should no longer exist in Parsers/");

            // Re-discover: should be reported as Disabled
            using (var loader = new DirectoryParserPluginLoader(resolver))
            {
                var disabledResult = loader.DiscoverAndLoad();
                var disabledEval = Assert.Single(disabledResult.Evaluations, e =>
                    e.Status == ParserRegistrationStatus.Disabled &&
                    (e.Candidate.Id.Equals("MockLang", StringComparison.OrdinalIgnoreCase) || e.Candidate.DirectoryPath.Contains("MockLang")));
                Assert.Equal(ParserRegistrationStatus.Disabled, disabledEval.Status);
            }

            // Enable parser back
            var enableExit = ParsersCommandHandler.EnableParser("MockLang", resolver);
            Assert.Equal(0, enableExit);

            Assert.True(Directory.Exists(dummyParserDir), "MockLang folder should be restored in Parsers/");
            Assert.False(Directory.Exists(Path.Combine(userDisabled, "MockLang")), "MockLang should no longer exist in DisabledParsers");
        }
        finally
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            try
            {
                if (Directory.Exists(tempRoot))
                {
                    Directory.Delete(tempRoot, recursive: true);
                }
            }
            catch { }
        }
    }

    [Fact]
    public void EnableParser_WhenAlreadyActive_ReturnsZero()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), $"cg_stage4_already_active_{Guid.NewGuid():N}");
        var userParsers = Path.Combine(tempRoot, "user", "Parsers");
        var dummyParserDir = Path.Combine(userParsers, "MockLang");
        Directory.CreateDirectory(dummyParserDir);
        var jsonParserDll = typeof(Parser.JsonParser).Assembly.Location;
        File.Copy(jsonParserDll, Path.Combine(dummyParserDir, "MockLang.dll"));

        try
        {
            var resolver = new ParserDirectoryResolver(customUserDir: userParsers);
            var exitCode = ParsersCommandHandler.EnableParser("json", resolver);
            Assert.Equal(0, exitCode);
        }
        finally
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            try
            {
                if (Directory.Exists(tempRoot))
                {
                    Directory.Delete(tempRoot, recursive: true);
                }
            }
            catch { }
        }
    }
}
