using CodeGraphMcp.Commands;
using CodeGraphMcp.Db;
using CodeGraphMcp.Indexer;
using CodeGraphMcp.Parser;
using CodeGraphMcp.Plugins;
using Microsoft.Data.Sqlite;
using Xunit;

namespace CodeGraphMcp.Tests;

public class ModularParserStage6Tests
{
    [Fact]
    public void SecurityEvaluator_StrictMode_BlocksUserTierShadowingOfAppTierParser()
    {
        var settings = new ParserSecuritySettings { OverridePolicy = ParserOverridePolicy.Strict };

        var appCSharp = new DiscoveredParserCandidate(
            Id: "csharp",
            Language: "csharp",
            FileExtensions: [".cs"],
            AssemblyPath: "/app/Parsers/CSharp/CSharp.dll",
            DirectoryPath: "/app/Parsers/CSharp",
            SourceTier: ParserSourceTier.App,
            IsDisabledFolder: false);

        var userMaliciousCSharp = new DiscoveredParserCandidate(
            Id: "csharp",
            Language: "csharp",
            FileExtensions: [".cs"],
            AssemblyPath: "/user/Parsers/CSharp/CSharp.dll",
            DirectoryPath: "/user/Parsers/CSharp",
            SourceTier: ParserSourceTier.User,
            IsDisabledFolder: false);

        var activeCandidates = new List<DiscoveredParserCandidate> { appCSharp };

        var evaluation = ParserSecurityEvaluator.Evaluate(userMaliciousCSharp, activeCandidates, settings);

        Assert.Equal(ParserRegistrationStatus.BlockedShadowing, evaluation.Status);
        Assert.Contains("Strict", evaluation.Reason);
    }

    [Fact]
    public void SecurityEvaluator_AllowAllMode_AllowsUserTierOverrideOfAppTierParser()
    {
        var settings = new ParserSecuritySettings { OverridePolicy = ParserOverridePolicy.AllowAll };

        var appCSharp = new DiscoveredParserCandidate(
            Id: "csharp",
            Language: "csharp",
            FileExtensions: [".cs"],
            AssemblyPath: "/app/Parsers/CSharp/CSharp.dll",
            DirectoryPath: "/app/Parsers/CSharp",
            SourceTier: ParserSourceTier.App,
            IsDisabledFolder: false);

        var userCSharp = new DiscoveredParserCandidate(
            Id: "csharp",
            Language: "csharp",
            FileExtensions: [".cs"],
            AssemblyPath: "/user/Parsers/CSharp/CSharp.dll",
            DirectoryPath: "/user/Parsers/CSharp",
            SourceTier: ParserSourceTier.User,
            IsDisabledFolder: false);

        var activeCandidates = new List<DiscoveredParserCandidate> { appCSharp };

        var evaluation = ParserSecurityEvaluator.Evaluate(userCSharp, activeCandidates, settings);

        Assert.Equal(ParserRegistrationStatus.ActiveOverride, evaluation.Status);
        Assert.Contains("Overriding app-level parser", evaluation.Reason);
    }

    [Fact]
    public void SecurityEvaluator_AllowListMode_RespectsConfiguredParserIds()
    {
        var settings = new ParserSecuritySettings
        {
            OverridePolicy = ParserOverridePolicy.AllowList,
            AllowedOverrides = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "custom-csharp" }
        };

        var appCandidate = new DiscoveredParserCandidate(
            Id: "csharp",
            Language: "csharp",
            FileExtensions: [".cs"],
            AssemblyPath: "/app/Parsers/CSharp/CSharp.dll",
            DirectoryPath: "/app/Parsers/CSharp",
            SourceTier: ParserSourceTier.App,
            IsDisabledFolder: false);

        var allowedUserCandidate = new DiscoveredParserCandidate(
            Id: "custom-csharp",
            Language: "csharp",
            FileExtensions: [".cs"],
            AssemblyPath: "/user/Parsers/CustomCSharp/CustomCSharp.dll",
            DirectoryPath: "/user/Parsers/CustomCSharp",
            SourceTier: ParserSourceTier.User,
            IsDisabledFolder: false);

        var deniedUserCandidate = new DiscoveredParserCandidate(
            Id: "unlisted-csharp",
            Language: "csharp",
            FileExtensions: [".cs"],
            AssemblyPath: "/user/Parsers/UnlistedCSharp/UnlistedCSharp.dll",
            DirectoryPath: "/user/Parsers/UnlistedCSharp",
            SourceTier: ParserSourceTier.User,
            IsDisabledFolder: false);

        var activeCandidates = new List<DiscoveredParserCandidate> { appCandidate };

        var allowedEval = ParserSecurityEvaluator.Evaluate(allowedUserCandidate, activeCandidates, settings);
        var deniedEval = ParserSecurityEvaluator.Evaluate(deniedUserCandidate, activeCandidates, settings);

        Assert.Equal(ParserRegistrationStatus.ActiveOverride, allowedEval.Status);
        Assert.Equal(ParserRegistrationStatus.BlockedShadowing, deniedEval.Status);
    }

    [Fact]
    public void ParserDirectoryResolver_PrioritizesEnvOverUserOverApp()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), $"cg_stage6_resolver_{Guid.NewGuid():N}");
        var envDir = Path.Combine(tempRoot, "env", "Parsers");
        var userDir = Path.Combine(tempRoot, "user", "Parsers");
        var appDir = Path.Combine(tempRoot, "app", "Parsers");

        Directory.CreateDirectory(envDir);
        Directory.CreateDirectory(userDir);
        Directory.CreateDirectory(appDir);

        try
        {
            var resolver = new ParserDirectoryResolver(envDir, userDir, appDir);
            var searchDirs = resolver.GetActiveSearchDirectories();

            Assert.Equal(3, searchDirs.Count);
            Assert.Equal(ParserSourceTier.Environment, searchDirs[0].Tier);
            Assert.Equal(ParserSourceTier.User, searchDirs[1].Tier);
            Assert.Equal(ParserSourceTier.App, searchDirs[2].Tier);

            Assert.True(ParserDirectoryResolver.IsDirectoryWritable(userDir));
        }
        finally
        {
            try { if (Directory.Exists(tempRoot)) Directory.Delete(tempRoot, true); } catch { }
        }
    }

    [Fact]
    public async Task EndToEnd_MultiLanguageIndexing_AndParserLifecycle()
    {
        var tempRepo = Path.Combine(Path.GetTempPath(), $"cg_stage6_e2e_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRepo);

        // 1. Create source files for all 8 supported languages
        File.WriteAllText(Path.Combine(tempRepo, "Controller.cs"), "namespace App; public class MainController { public void Index() {} }");
        File.WriteAllText(Path.Combine(tempRepo, "models.ts"), "export interface UserDto { id: string; name: string; }");
        File.WriteAllText(Path.Combine(tempRepo, "utils.js"), "function formatName(name) { return name.trim(); }");
        File.WriteAllText(Path.Combine(tempRepo, "page.html"), "<div id=\"content-panel\" class=\"container\"><h1 id=\"title-header\">Hello</h1></div>");
        File.WriteAllText(Path.Combine(tempRepo, "build.ps1"), "function Start-Build { param([string]$Target) }");
        File.WriteAllText(Path.Combine(tempRepo, "settings.json"), "{ \"appName\": \"CodeGraphDemo\", \"port\": 8080 }");
        File.WriteAllText(Path.Combine(tempRepo, "manifest.yaml"), "app:\n  name: CodeGraphApp\n  replicas: 3");
        File.WriteAllText(Path.Combine(tempRepo, "theme.css"), ".container { display: flex; } #content-panel { color: black; }");

        using var dbManager = new WorkspaceDbManager();
        var registry = ParserRegistry.CreateDefault();
        var workspaceService = new DynamicWorkspaceService(dbManager, parserRegistry: registry);

        try
        {
            // 2. Perform full indexing
            var context = await workspaceService.EnsureIndexedAsync(tempRepo);
            Assert.NotNull(context);

            var db = context.Db;
            var symbolCount = db.Symbols.GetCount();
            Assert.True(symbolCount >= 8, $"Expected >= 8 symbols across languages, found {symbolCount}");

            // Verify symbol presence for each language
            var allSymbols = db.Symbols.FindByName("");

            var csharpSyms = allSymbols.Where(s => s.Language == "csharp").ToList();
            Assert.NotEmpty(csharpSyms);
            Assert.Contains(csharpSyms, s => s.SymbolName == "MainController");

            var tsSyms = allSymbols.Where(s => s.Language == "typescript").ToList();
            Assert.NotEmpty(tsSyms);
            Assert.Contains(tsSyms, s => s.SymbolName == "UserDto");

            var jsSyms = allSymbols.Where(s => s.Language == "javascript").ToList();
            Assert.NotEmpty(jsSyms);
            Assert.Contains(jsSyms, s => s.SymbolName == "formatName");

            var htmlSyms = allSymbols.Where(s => s.Language == "html").ToList();
            Assert.NotEmpty(htmlSyms);
            Assert.Contains(htmlSyms, s => s.SymbolName == "content-panel");

            var psSyms = allSymbols.Where(s => s.Language == "powershell").ToList();
            Assert.NotEmpty(psSyms);
            Assert.Contains(psSyms, s => s.SymbolName == "Start-Build");

            var jsonSyms = allSymbols.Where(s => s.Language == "json").ToList();
            Assert.NotEmpty(jsonSyms);
            Assert.Contains(jsonSyms, s => s.SymbolName == "appName");

            var yamlSyms = allSymbols.Where(s => s.Language == "yaml").ToList();
            Assert.NotEmpty(yamlSyms);
            Assert.Contains(yamlSyms, s => s.SymbolName == "app");

            var cssSyms = allSymbols.Where(s => s.Language == "css").ToList();
            Assert.NotEmpty(cssSyms);
            Assert.Contains(cssSyms, s => s.SymbolName == "container");
        }
        finally
        {
            dbManager.Dispose();
            SqliteConnection.ClearAllPools();
            try { if (Directory.Exists(tempRepo)) Directory.Delete(tempRepo, recursive: true); } catch { }
        }
    }
}
