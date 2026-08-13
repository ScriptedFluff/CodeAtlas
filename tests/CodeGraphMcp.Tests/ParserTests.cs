using CodeGraphMcp.Models;
using CodeGraphMcp.Parser;
using Xunit;

namespace CodeGraphMcp.Tests;

public class CSharpParserTests
{
    private static readonly CSharpParser Parser = new();

    private static string FixtureSource(string filename) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", filename));

    [Fact]
    public void Parse_SampleClass_ExtractsExpectedSymbols()
    {
        var source  = FixtureSource("SampleClass.cs");
        var symbols = Parser.Parse("Fixtures/SampleClass.cs", source).ToList();

        Assert.NotEmpty(symbols);
        Assert.Contains(symbols, s => s.SymbolName == "PersonService" && s.SymbolType == SymbolType.Class);
        Assert.Contains(symbols, s => s.SymbolName == "GetGreeting"   && s.SymbolType == SymbolType.Method);
        Assert.Contains(symbols, s => s.SymbolName == "Name"          && s.SymbolType == SymbolType.Property);
        Assert.Contains(symbols, s => s.SymbolName == "IPersonRepository" && s.SymbolType == SymbolType.Interface);
    }

    [Fact]
    public void Parse_SampleClass_ExtractsNamespace()
    {
        var source  = FixtureSource("SampleClass.cs");
        var symbols = Parser.Parse("Fixtures/SampleClass.cs", source).ToList();

        var cls = symbols.Single(s => s.SymbolName == "PersonService");
        Assert.Equal("Fixtures.Sample", cls.Namespace);
    }

    [Fact]
    public void Parse_SampleClass_LineNumbersAreCorrect()
    {
        var source  = FixtureSource("SampleClass.cs");
        var symbols = Parser.Parse("Fixtures/SampleClass.cs", source).ToList();

        var cls = symbols.Single(s => s.SymbolName == "PersonService");
        Assert.True(cls.StartLine > 0);
        Assert.True(cls.EndLine >= cls.StartLine);
    }

    [Fact]
    public void Parse_EmptySource_ReturnsNoSymbols()
    {
        var symbols = Parser.Parse("empty.cs", "").ToList();
        Assert.Empty(symbols);
    }

    [Fact]
    public void Parse_MalformedSource_DoesNotThrow()
    {
        var symbols = Parser.Parse("bad.cs", "class { !!!invalid c#").ToList();
    }

    [Fact]
    public void Parse_PartialClass_SetsIsPartialFlag()
    {
        var source = "namespace Test { public partial class PartialService { public void DoStuff() {} } }";
        var symbols = Parser.Parse("PartialService.cs", source).ToList();

        var cls = symbols.Single(s => s.SymbolName == "PartialService");
        Assert.True(cls.IsPartial);
    }
}


public class TypeScriptParserTests
{
    private static readonly TypeScriptParser Parser = new();

    private static string FixtureSource(string filename) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", filename));

    [Fact]
    public void Parse_SampleComponent_ExtractsInterfaceAndClass()
    {
        var source  = FixtureSource("SampleComponent.ts");
        var symbols = Parser.Parse("Fixtures/SampleComponent.ts", source).ToList();

        Assert.Contains(symbols, s => s.SymbolName == "UserProfile" && s.SymbolType == SymbolType.Interface);
        Assert.Contains(symbols, s => s.SymbolName == "UserService"  && s.SymbolType == SymbolType.Class);
        Assert.Contains(symbols, s => s.SymbolName == "getUser"      && s.SymbolType == SymbolType.Method);
        Assert.Contains(symbols, s => s.SymbolName == "UserId"       && s.SymbolType == SymbolType.TypeAlias);
    }

    [Fact]
    public void Parse_Interface_SetsIsPartialFlagForMerging()
    {
        var source = "export interface Config { host: string; }";
        var symbols = Parser.Parse("config.ts", source).ToList();

        var iface = symbols.Single(s => s.SymbolName == "Config");
        Assert.True(iface.IsPartial);
    }
}

public class JsonParserTests
{
    private static readonly JsonParser Parser = new();

    [Fact]
    public void Parse_PackageJson_ExtractsKeys()
    {
        var source = """
        {
          "name": "my-app",
          "version": "1.0.0",
          "scripts": {
            "build": "tsc"
          }
        }
        """;
        var symbols = Parser.Parse("package.json", source).ToList();

        Assert.Contains(symbols, s => s.SymbolName == "name" && s.SymbolType == SymbolType.Property);
        Assert.Contains(symbols, s => s.SymbolName == "version" && s.SymbolType == SymbolType.Property);
        Assert.Contains(symbols, s => s.SymbolName == "scripts" && s.SymbolType == SymbolType.Property);
        Assert.Contains(symbols, s => s.SymbolName == "build" && s.SymbolType == SymbolType.Property);
        Assert.All(symbols, s => Assert.Equal("json", s.Language));
    }
}

public class YamlParserTests
{
    private static readonly YamlParser Parser = new();

    [Fact]
    public void Parse_DockerCompose_ExtractsKeys()
    {
        var source = """
        version: '3.8'
        services:
          web:
            image: nginx:latest
            ports:
              - "80:80"
        """;
        var symbols = Parser.Parse("docker-compose.yml", source).ToList();

        Assert.Contains(symbols, s => s.SymbolName == "version" && s.SymbolType == SymbolType.Property);
        Assert.Contains(symbols, s => s.SymbolName == "services" && s.SymbolType == SymbolType.Property);
        Assert.Contains(symbols, s => s.SymbolName == "web" && s.SymbolType == SymbolType.Property);
        Assert.Contains(symbols, s => s.SymbolName == "image" && s.SymbolType == SymbolType.Property);
        Assert.All(symbols, s => Assert.Equal("yaml", s.Language));
    }
}

public class CssParserTests
{
    private static readonly CssParser Parser = new();

    [Fact]
    public void Parse_CssSource_ExtractsClassesIdsVariablesAndKeyframes()
    {
        var source = """
        :root {
          --primary-color: #007bff;
        }

        .btn-primary {
          color: var(--primary-color);
          background-color: #ffffff;
        }

        #header-nav {
          display: flex;
        }

        @keyframes fadeIn {
          from { opacity: 0; }
          to { opacity: 1; }
        }
        """;

        var symbols = Parser.Parse("styles.css", source).ToList();

        Assert.Contains(symbols, s => s.SymbolName == "primary-color" && s.SymbolType == SymbolType.Variable);
        Assert.Contains(symbols, s => s.SymbolName == "btn-primary" && s.SymbolType == SymbolType.Class);
        Assert.Contains(symbols, s => s.SymbolName == "header-nav" && s.SymbolType == SymbolType.Property);
        Assert.Contains(symbols, s => s.SymbolName == "fadeIn" && s.SymbolType == SymbolType.Function);
        Assert.DoesNotContain(symbols, s => s.SymbolName == "ffffff");
        Assert.All(symbols, s => Assert.Equal("css", s.Language));
    }
}

public class HtmlParserTests
{
    private static readonly HtmlParser Parser = new();

    [Fact]
    public void Parse_CsprojAndXmlSource_ExtractsTagsAndPackageReferences()
    {
        var source = """
        <Project Sdk="Microsoft.NET.Sdk">
          <ItemGroup>
            <PackageReference Include="Microsoft.Data.Sqlite" Version="9.0.0" />
          </ItemGroup>
        </Project>
        """;

        var symbols = Parser.Parse("App.csproj", source).ToList();

        Assert.Contains(symbols, s => s.SymbolName == "Project" && s.SymbolType == SymbolType.Class);
        Assert.Contains(symbols, s => s.SymbolName == "ItemGroup" && s.SymbolType == SymbolType.Class);
        Assert.Contains(symbols, s => s.SymbolName == "PackageReference" && s.SymbolType == SymbolType.Class);
        Assert.Contains(symbols, s => s.SymbolName == "Microsoft.Data.Sqlite" && s.SymbolType == SymbolType.Property);
        Assert.All(symbols, s => Assert.Equal("html", s.Language));
        Assert.Contains(".xml", Parser.FileExtensions);
        Assert.Contains(".csproj", Parser.FileExtensions);
        Assert.Contains(".config", Parser.FileExtensions);
    }
}




