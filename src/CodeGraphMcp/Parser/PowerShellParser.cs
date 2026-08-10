using CodeGraphMcp.Models;

namespace CodeGraphMcp.Parser;

public sealed class PowerShellParser : ILanguageParser
{
    public IReadOnlyList<string> FileExtensions => [".ps1", ".psm1", ".psd1"];
    public string Language => "powershell";
    public IEnumerable<Symbol> Parse(string filePath, string source) => [];
}
