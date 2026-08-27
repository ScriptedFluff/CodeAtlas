using CodeAtlas.Models;
using CodeAtlas.Parser;
using CodeAtlas.Core.Plugins;
using Xunit;

namespace CodeAtlas.Tests;

public class ModularParserStage1Tests
{
    private sealed class DummyParser : ILanguageParser
    {
        public ParserMetadata Metadata { get; }
        public IReadOnlyList<string> FileExtensions { get; }
        public string Language { get; }

        public DummyParser(string id, string language, IReadOnlyList<string> extensions, string version = "1.0.0")
        {
            Metadata = new ParserMetadata(id, $"{language} Test Parser", version, "Test description", "Author");
            Language = language;
            FileExtensions = extensions;
        }

        public IEnumerable<Symbol> Parse(string filePath, string source) => [];
    }

    [Fact]
    public void ParserMetadata_DefaultImplementation_UsesLanguage()
    {
        var parser = new CSharpParser();
        Assert.NotNull(parser.Metadata);
        Assert.Equal("csharp", parser.Metadata.Id);
        Assert.Equal("1.0.0", parser.Metadata.Version);
    }

    [Fact]
    public void ParserRegistry_RegisterAndLookup_WorksCaseInsensitively()
    {
        var registry = new ParserRegistry();
        var dummy = new DummyParser("custom-lang", "custom", [".custom", ".cst"]);
        registry.Register(dummy);

        Assert.Single(registry.GetAllParsers());
        Assert.Same(dummy, registry.GetParserForExtension(".custom"));
        Assert.Same(dummy, registry.GetParserForExtension("custom"));
        Assert.Same(dummy, registry.GetParserForExtension(".CST"));
        Assert.Same(dummy, registry.GetParserForFile("src/file.CUSTOM"));
        Assert.Same(dummy, registry.GetParserById("custom-lang"));
        Assert.Same(dummy, registry.GetParserById("CUSTOM-LANG"));
        Assert.Same(dummy, registry.GetParserById("custom"));
        Assert.Null(registry.GetParserForExtension(".unknown"));
        Assert.Null(registry.GetParserForFile(""));
    }

    [Fact]
    public void ParserRegistry_CreateDefault_ContainsAllBuiltInParsers()
    {
        var registry = ParserRegistry.CreateDefault();
        var all = registry.GetAllParsers();

        Assert.True(all.Count >= 8);
        Assert.NotNull(registry.GetParserForExtension(".cs"));
        Assert.NotNull(registry.GetParserForExtension(".ts"));
        Assert.NotNull(registry.GetParserForExtension(".tsx"));
        Assert.NotNull(registry.GetParserForExtension(".js"));
        Assert.NotNull(registry.GetParserForExtension(".html"));
        Assert.NotNull(registry.GetParserForExtension(".ps1"));
        Assert.NotNull(registry.GetParserForExtension(".json"));
        Assert.NotNull(registry.GetParserForExtension(".yaml"));
        Assert.NotNull(registry.GetParserForExtension(".css"));
    }

    [Fact]
    public void ParserDirectoryResolver_CustomPaths_ResolvesCorrectly()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), $"cg_test_{Guid.NewGuid():N}");
        var envDir = Path.Combine(tempRoot, "env", "Parsers");
        var userDir = Path.Combine(tempRoot, "user", "Parsers");
        var appDir = Path.Combine(tempRoot, "app", "Parsers");

        Directory.CreateDirectory(envDir);
        Directory.CreateDirectory(userDir);
        Directory.CreateDirectory(appDir);

        try
        {
            var resolver = new ParserDirectoryResolver(envDir, userDir, appDir);

            var activeDirs = resolver.GetActiveSearchDirectories();
            Assert.Equal(3, activeDirs.Count);
            Assert.Equal(ParserSourceTier.Environment, activeDirs[0].Tier);
            Assert.Equal(Path.GetFullPath(envDir), activeDirs[0].Path);
            Assert.Equal(ParserSourceTier.User, activeDirs[1].Tier);
            Assert.Equal(Path.GetFullPath(userDir), activeDirs[1].Path);
            Assert.Equal(ParserSourceTier.App, activeDirs[2].Tier);
            Assert.Equal(Path.GetFullPath(appDir), activeDirs[2].Path);

            Assert.True(ParserDirectoryResolver.IsDirectoryWritable(tempRoot));
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
    public void ParserSecurityEvaluator_StrictPolicy_BlocksShadowingAppParser()
    {
        var appCandidate = new DiscoveredParserCandidate(
            "csharp", "csharp", [".cs"], "C:\\Program Files\\App\\Parsers\\CSharp.dll", "C:\\Program Files\\App\\Parsers\\CSharp", ParserSourceTier.App);

        var userCandidate = new DiscoveredParserCandidate(
            "csharp", "csharp", [".cs"], "C:\\Users\\User\\AppData\\Parsers\\CSharp.dll", "C:\\Users\\User\\AppData\\Parsers\\CSharp", ParserSourceTier.User);

        var settings = new ParserSecuritySettings { OverridePolicy = ParserOverridePolicy.Strict };

        var eval = ParserSecurityEvaluator.Evaluate(userCandidate, [appCandidate], settings);
        Assert.Equal(ParserRegistrationStatus.BlockedShadowing, eval.Status);
        Assert.Contains("blocked by Strict security policy", eval.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ParserSecurityEvaluator_AllowAllPolicy_AllowsOverride()
    {
        var appCandidate = new DiscoveredParserCandidate(
            "csharp", "csharp", [".cs"], "C:\\Program Files\\App\\Parsers\\CSharp.dll", "C:\\Program Files\\App\\Parsers\\CSharp", ParserSourceTier.App);

        var userCandidate = new DiscoveredParserCandidate(
            "csharp", "csharp", [".cs"], "C:\\Users\\User\\AppData\\Parsers\\CSharp.dll", "C:\\Users\\User\\AppData\\Parsers\\CSharp", ParserSourceTier.User);

        var settings = new ParserSecuritySettings { OverridePolicy = ParserOverridePolicy.AllowAll };

        var eval = ParserSecurityEvaluator.Evaluate(userCandidate, [appCandidate], settings);
        Assert.Equal(ParserRegistrationStatus.ActiveOverride, eval.Status);
    }

    [Fact]
    public void ParserSecurityEvaluator_AllowListPolicy_EvaluatesCorrectly()
    {
        var appCandidate = new DiscoveredParserCandidate(
            "csharp", "csharp", [".cs"], "C:\\Program Files\\App\\Parsers\\CSharp.dll", "C:\\Program Files\\App\\Parsers\\CSharp", ParserSourceTier.App);

        var allowedUser = new DiscoveredParserCandidate(
            "csharp", "csharp", [".cs"], "C:\\Users\\User\\AppData\\Parsers\\CSharp.dll", "C:\\Users\\User\\AppData\\Parsers\\CSharp", ParserSourceTier.User);

        var disallowedUser = new DiscoveredParserCandidate(
            "typescript", "typescript", [".ts"], "C:\\Users\\User\\AppData\\Parsers\\TypeScript.dll", "C:\\Users\\User\\AppData\\Parsers\\TypeScript", ParserSourceTier.User);

        var tsApp = new DiscoveredParserCandidate(
            "typescript", "typescript", [".ts"], "C:\\Program Files\\App\\Parsers\\TypeScript.dll", "C:\\Program Files\\App\\Parsers\\TypeScript", ParserSourceTier.App);

        var settings = new ParserSecuritySettings
        {
            OverridePolicy = ParserOverridePolicy.AllowList,
            AllowedOverrides = ["csharp"]
        };

        var evalAllowed = ParserSecurityEvaluator.Evaluate(allowedUser, [appCandidate, tsApp], settings);
        Assert.Equal(ParserRegistrationStatus.ActiveOverride, evalAllowed.Status);

        var evalDisallowed = ParserSecurityEvaluator.Evaluate(disallowedUser, [appCandidate, tsApp], settings);
        Assert.Equal(ParserRegistrationStatus.BlockedShadowing, evalDisallowed.Status);
    }

    [Fact]
    public void ParserSecurityEvaluator_NonCollidingUserParser_IsActive()
    {
        var appCandidate = new DiscoveredParserCandidate(
            "csharp", "csharp", [".cs"], "C:\\Program Files\\App\\Parsers\\CSharp.dll", "C:\\Program Files\\App\\Parsers\\CSharp", ParserSourceTier.App);

        var userCandidate = new DiscoveredParserCandidate(
            "python", "python", [".py"], "C:\\Users\\User\\AppData\\Parsers\\Python.dll", "C:\\Users\\User\\AppData\\Parsers\\Python", ParserSourceTier.User);

        var settings = new ParserSecuritySettings { OverridePolicy = ParserOverridePolicy.Strict };

        var eval = ParserSecurityEvaluator.Evaluate(userCandidate, [appCandidate], settings);
        Assert.Equal(ParserRegistrationStatus.Active, eval.Status);
    }
}
