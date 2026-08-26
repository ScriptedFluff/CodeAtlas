using CodeGraphMcp.Parser;
using CodeGraphMcp.Plugins;
using Xunit;

namespace CodeGraphMcp.Tests;

public class ModularParserStage2Tests
{
    [Fact]
    public void ParserLoadContext_DelegatesCodeGraphMcpCore_ToDefaultContext()
    {
        var dummyPath = typeof(ILanguageParser).Assembly.Location;
        var context = new ParserLoadContext(dummyPath);

        Assert.True(context.IsCollectible);
        Assert.Equal(dummyPath, context.PluginAssemblyPath);

        // Core assembly should delegate (return null from Load to fallback to host ALC)
        var asm = typeof(ILanguageParser).Assembly;
        var loaded = context.LoadFromAssemblyName(asm.GetName());
        Assert.Same(asm, loaded);
    }

    [Fact]
    public void DirectoryParserPluginLoader_WithBuiltIns_ReturnsActiveBuiltInParsers()
    {
        var loader = new DirectoryParserPluginLoader();
        var result = loader.DiscoverAndLoad();

        Assert.NotNull(result);
        Assert.NotNull(result.Registry);
        Assert.True(result.Registry.GetAllParsers().Count >= 8);

        Assert.NotNull(result.Registry.GetParserForExtension(".cs"));
        Assert.NotNull(result.Registry.GetParserForExtension(".ts"));
        Assert.NotNull(result.Registry.GetParserForExtension(".js"));
        Assert.NotNull(result.Registry.GetParserForExtension(".html"));
        Assert.NotNull(result.Registry.GetParserForExtension(".ps1"));
        Assert.NotNull(result.Registry.GetParserForExtension(".json"));
        Assert.NotNull(result.Registry.GetParserForExtension(".yaml"));
        Assert.NotNull(result.Registry.GetParserForExtension(".css"));

        var csEval = Assert.Single(result.Evaluations, e => e.Candidate.Id.Equals("csharp", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(ParserRegistrationStatus.Active, csEval.Status);
    }

    [Fact]
    public void DirectoryParserPluginLoader_ScansDisabledDirectories_ReportsDisabledStatus()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), $"cg_stage2_disabled_{Guid.NewGuid():N}");
        var envParsers = Path.Combine(tempRoot, "env", "Parsers");
        var userParsers = Path.Combine(tempRoot, "user", "Parsers");
        var appParsers = Path.Combine(tempRoot, "app", "Parsers");

        var appDisabled = Path.Combine(tempRoot, "app", "DisabledParsers", "Rust");
        Directory.CreateDirectory(appDisabled);
        File.WriteAllText(Path.Combine(appDisabled, "Rust.dll"), "mock-dll-content");

        try
        {
            var resolver = new ParserDirectoryResolver(envParsers, userParsers, appParsers);
            var loader = new DirectoryParserPluginLoader(resolver);

            var result = loader.DiscoverAndLoad();

            var disabledEval = Assert.Single(result.Evaluations, e => e.Candidate.Id.Equals("rust", StringComparison.OrdinalIgnoreCase));
            Assert.Equal(ParserRegistrationStatus.Disabled, disabledEval.Status);
            Assert.True(disabledEval.Candidate.IsDisabledFolder);
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }

    [Fact]
    public void DirectoryParserPluginLoader_HandlesCorruptAssembly_GracefullyAsFailed()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), $"cg_stage2_corrupt_{Guid.NewGuid():N}");
        var userParsers = Path.Combine(tempRoot, "user", "Parsers", "BrokenLang");
        Directory.CreateDirectory(userParsers);
        File.WriteAllText(Path.Combine(userParsers, "BrokenLang.dll"), "not a real PE dll");

        try
        {
            var resolver = new ParserDirectoryResolver(customUserDir: Path.Combine(tempRoot, "user", "Parsers"));
            var loader = new DirectoryParserPluginLoader(resolver);

            var result = loader.DiscoverAndLoad();

            var failedEval = Assert.Single(result.Evaluations, e => e.Candidate.Id.Equals("BrokenLang", StringComparison.OrdinalIgnoreCase));
            Assert.Equal(ParserRegistrationStatus.Failed, failedEval.Status);
            Assert.NotEmpty(result.Diagnostics);
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }
}
