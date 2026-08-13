using CodeGraphMcp.Models;
using CodeGraphMcp.Parser;
using Xunit;

namespace CodeGraphMcp.Tests;

public class JsonAstParserTests
{
    private static readonly JsonParser Parser = new();

    [Fact]
    public void Parse_NestedJsonObject_ExtractsFlatAndQualifiedPaths()
    {
        var source = """
        {
          "name": "CodeGraphMcp",
          "scripts": {
            "build": "dotnet build",
            "test": "dotnet test"
          }
        }
        """;

        var symbols = Parser.Parse("package.json", source).ToList();

        Assert.Contains(symbols, s => s.SymbolName == "name" && s.SymbolType == SymbolType.Property);
        Assert.Contains(symbols, s => s.SymbolName == "scripts" && s.SymbolType == SymbolType.Property);
        Assert.Contains(symbols, s => s.SymbolName == "build" && s.SymbolType == SymbolType.Property);
        Assert.Contains(symbols, s => s.SymbolName == "scripts.build" && s.SymbolType == SymbolType.Property);
        Assert.Contains(symbols, s => s.SymbolName == "scripts.test" && s.SymbolType == SymbolType.Property);
    }

    [Fact]
    public void Parse_JsonWithCommentsAndTrailingCommas_ParsesSuccessfully()
    {
        var source = """
        {
          // Global compiler options
          "compilerOptions": {
            "target": "ES2022",
            "module": "NodeNext",
          },
        }
        """;

        var symbols = Parser.Parse("tsconfig.json", source).ToList();

        Assert.Contains(symbols, s => s.SymbolName == "compilerOptions" && s.SymbolType == SymbolType.Property);
        Assert.Contains(symbols, s => s.SymbolName == "compilerOptions.target" && s.SymbolType == SymbolType.Property);
        Assert.Contains(symbols, s => s.SymbolName == "target" && s.SymbolType == SymbolType.Property);
    }

    [Fact]
    public void Parse_MalformedJson_UsesRegexFallbackWithoutThrowing()
    {
        var source = """
        {
          "validKey": "value",
          "invalidSection": {{{
        }
        """;

        var symbols = Parser.Parse("bad.json", source).ToList();

        Assert.Contains(symbols, s => s.SymbolName == "validKey");
    }
}
