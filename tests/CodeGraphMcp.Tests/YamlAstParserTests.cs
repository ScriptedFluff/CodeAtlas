using CodeGraphMcp.Models;
using CodeGraphMcp.Parser;
using Xunit;

namespace CodeGraphMcp.Tests;

public class YamlAstParserTests
{
    private static readonly YamlParser Parser = new();

    [Fact]
    public void Parse_IndentedYaml_ExtractsFlatAndQualifiedPaths()
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
        Assert.Contains(symbols, s => s.SymbolName == "services.web" && s.SymbolType == SymbolType.Property);
        Assert.Contains(symbols, s => s.SymbolName == "web" && s.SymbolType == SymbolType.Property);
        Assert.Contains(symbols, s => s.SymbolName == "services.web.image" && s.SymbolType == SymbolType.Property);
        Assert.Contains(symbols, s => s.SymbolName == "image" && s.SymbolType == SymbolType.Property);
    }

    [Fact]
    public void Parse_YamlWithComments_IgnoresComments()
    {
        var source = """
        # Database config
        database:
          # Main connection
          host: localhost
        """;

        var symbols = Parser.Parse("config.yml", source).ToList();

        Assert.Contains(symbols, s => s.SymbolName == "database");
        Assert.Contains(symbols, s => s.SymbolName == "database.host");
        Assert.Contains(symbols, s => s.SymbolName == "host");
    }
}
