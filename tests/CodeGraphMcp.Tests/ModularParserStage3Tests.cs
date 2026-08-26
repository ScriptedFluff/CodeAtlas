using CodeGraphMcp.Parser;
using CodeGraphMcp.Plugins;
using Xunit;

namespace CodeGraphMcp.Tests;

public class ModularParserStage3Tests
{
    [Fact]
    public void DynamicDiscovery_LoadsAllEightModularParsers()
    {
        var loader = new DirectoryParserPluginLoader();
        var result = loader.DiscoverAndLoad();

        Assert.NotNull(result);
        Assert.NotNull(result.Registry);

        var parsers = result.Registry.GetAllParsers();
        Assert.True(parsers.Count >= 8, $"Expected >= 8 parsers but got {parsers.Count}");

        var extensions = result.Registry.GetSupportedExtensions();
        Assert.Contains(".cs", extensions);
        Assert.Contains(".ts", extensions);
        Assert.Contains(".js", extensions);
        Assert.Contains(".html", extensions);
        Assert.Contains(".ps1", extensions);
        Assert.Contains(".json", extensions);
        Assert.Contains(".yaml", extensions);
        Assert.Contains(".css", extensions);
    }

    [Fact]
    public void DynamicCSharpParser_ParsesSourceCodeCorrectly()
    {
        var loader = new DirectoryParserPluginLoader();
        var registry = loader.DiscoverAndLoad().Registry;

        var parser = registry.GetParserForExtension(".cs");
        Assert.NotNull(parser);
        Assert.Equal("csharp", parser.Language);

        var source = """
            namespace SampleNamespace;
            public class SampleClass
            {
                public void DoWork() {}
            }
            """;

        var result = parser.ParseFile("Sample.cs", source);
        Assert.NotEmpty(result.Symbols);
        Assert.Contains(result.Symbols, s => s.SymbolName == "SampleClass");
    }

    [Fact]
    public void DynamicTypeScriptParser_ParsesSourceCodeCorrectly()
    {
        var loader = new DirectoryParserPluginLoader();
        var registry = loader.DiscoverAndLoad().Registry;

        var parser = registry.GetParserForExtension(".ts");
        Assert.NotNull(parser);
        Assert.Equal("typescript", parser.Language);

        var source = """
            export interface IUserService {
                getUser(id: string): User;
            }
            """;

        var result = parser.ParseFile("UserService.ts", source);
        Assert.NotEmpty(result.Symbols);
        Assert.Contains(result.Symbols, s => s.SymbolName == "IUserService");
    }

    [Fact]
    public void DynamicPowerShellParser_ParsesSourceCodeCorrectly()
    {
        var loader = new DirectoryParserPluginLoader();
        var registry = loader.DiscoverAndLoad().Registry;

        var parser = registry.GetParserForExtension(".ps1");
        Assert.NotNull(parser);
        Assert.Equal("powershell", parser.Language);

        var source = """
            function Get-UserData {
                param([string]$UserId)
                Write-Output $UserId
            }
            """;

        var result = parser.ParseFile("GetUserData.ps1", source);
        Assert.NotEmpty(result.Symbols);
        Assert.Contains(result.Symbols, s => s.SymbolName == "Get-UserData");
    }

    [Fact]
    public void DynamicJsonParser_ParsesSourceCodeCorrectly()
    {
        var loader = new DirectoryParserPluginLoader();
        var registry = loader.DiscoverAndLoad().Registry;

        var parser = registry.GetParserForExtension(".json");
        Assert.NotNull(parser);
        Assert.Equal("json", parser.Language);

        var source = """
            {
                "name": "CodeGraphMcp",
                "version": "1.0.0"
            }
            """;

        var result = parser.ParseFile("appsettings.json", source);
        Assert.NotEmpty(result.Symbols);
        Assert.Contains(result.Symbols, s => s.SymbolName == "name");
        Assert.Contains(result.Symbols, s => s.SymbolName == "version");
    }

    [Fact]
    public void DynamicJavaScriptParser_ParsesSourceCodeCorrectly()
    {
        var loader = new DirectoryParserPluginLoader();
        var registry = loader.DiscoverAndLoad().Registry;

        var parser = registry.GetParserForExtension(".js");
        Assert.NotNull(parser);
        Assert.Equal("javascript", parser.Language);

        var source = """
            function calculateTotal(items) {
                return items.reduce((a, b) => a + b, 0);
            }
            """;

        var result = parser.ParseFile("calc.js", source);
        Assert.NotEmpty(result.Symbols);
        Assert.Contains(result.Symbols, s => s.SymbolName == "calculateTotal");
    }

    [Fact]
    public void DynamicHtmlParser_ParsesSourceCodeCorrectly()
    {
        var loader = new DirectoryParserPluginLoader();
        var registry = loader.DiscoverAndLoad().Registry;

        var parser = registry.GetParserForExtension(".html");
        Assert.NotNull(parser);
        Assert.Equal("html", parser.Language);

        var source = """
            <div id="main-container" class="layout-flex">
                <button name="submitBtn">Click</button>
            </div>
            """;

        var result = parser.ParseFile("index.html", source);
        Assert.NotEmpty(result.Symbols);
        Assert.Contains(result.Symbols, s => s.SymbolName == "main-container");
    }

    [Fact]
    public void DynamicYamlParser_ParsesSourceCodeCorrectly()
    {
        var loader = new DirectoryParserPluginLoader();
        var registry = loader.DiscoverAndLoad().Registry;

        var parser = registry.GetParserForExtension(".yaml");
        Assert.NotNull(parser);
        Assert.Equal("yaml", parser.Language);

        var source = """
            server:
              port: 8080
              host: localhost
            """;

        var result = parser.ParseFile("config.yaml", source);
        Assert.NotEmpty(result.Symbols);
        Assert.Contains(result.Symbols, s => s.SymbolName == "server");
    }

    [Fact]
    public void DynamicCssParser_ParsesSourceCodeCorrectly()
    {
        var loader = new DirectoryParserPluginLoader();
        var registry = loader.DiscoverAndLoad().Registry;

        var parser = registry.GetParserForExtension(".css");
        Assert.NotNull(parser);
        Assert.Equal("css", parser.Language);

        var source = """
            .primary-btn {
                background-color: blue;
            }
            #header-title {
                color: white;
            }
            """;

        var result = parser.ParseFile("styles.css", source);
        Assert.NotEmpty(result.Symbols);
        Assert.Contains(result.Symbols, s => s.SymbolName == "primary-btn");
        Assert.Contains(result.Symbols, s => s.SymbolName == "header-title");
    }
}

