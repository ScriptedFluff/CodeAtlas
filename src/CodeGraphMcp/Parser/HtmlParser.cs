using CodeGraphMcp.Models;

namespace CodeGraphMcp.Parser;

public sealed class HtmlParser : ILanguageParser
{
    public IReadOnlyList<string> FileExtensions => [".html", ".htm"];
    public string Language => "html";
    public IEnumerable<Symbol> Parse(string filePath, string source) => [];
}
