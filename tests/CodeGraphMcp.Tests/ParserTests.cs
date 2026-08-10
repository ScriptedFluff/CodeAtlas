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
}
