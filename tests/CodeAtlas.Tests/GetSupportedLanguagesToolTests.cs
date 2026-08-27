using CodeAtlas.Mcp.Tools;
using CodeAtlas.Parser;
using Xunit;

namespace CodeAtlas.Tests;

public class GetSupportedLanguagesToolTests
{
    [Fact]
    public async Task GetSupportedLanguagesAsync_ReturnsRegisteredParsersAndExtensions()
    {
        IReadOnlyList<ILanguageParser> parsers =
        [
            new CSharpParser(),
            new TypeScriptParser(),
            new HtmlParser(),
            new PowerShellParser()
        ];

        var tool = new GetSupportedLanguagesTool(parsers);
        var result = await tool.GetSupportedLanguagesAsync();

        Assert.NotNull(result);
        Assert.NotNull(result.SupportedLanguages);
        Assert.Equal(4, result.SupportedLanguages.Count);

        var csharp = Assert.Single(result.SupportedLanguages, l => l.Language.Equals("csharp", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(".cs", csharp.FileExtensions);

        var ts = Assert.Single(result.SupportedLanguages, l => l.Language.Equals("typescript", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(".ts", ts.FileExtensions);
        Assert.Contains(".tsx", ts.FileExtensions);
    }
}
