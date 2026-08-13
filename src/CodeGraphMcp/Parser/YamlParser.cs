using System.Text.RegularExpressions;
using CodeGraphMcp.Models;

namespace CodeGraphMcp.Parser;

public sealed partial class YamlParser : ILanguageParser
{
    public IReadOnlyList<string> FileExtensions => [".yaml", ".yml"];
    public string Language => "yaml";

    [GeneratedRegex(@"^\s*[-]?\s*(?<key>[a-zA-Z0-9_\-\.]+)\s*:", RegexOptions.Compiled)]
    private static partial Regex YamlKeyRegex();

    public IEnumerable<Symbol> Parse(string filePath, string source)
    {
        if (string.IsNullOrWhiteSpace(source))
            return [];

        var symbols = new List<Symbol>();
        var lines = source.Split('\n');
        int currentByteOffset = 0;

        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            var trimmed = line.TrimStart();
            if (trimmed.StartsWith('#'))
            {
                currentByteOffset += line.Length + 1;
                continue;
            }

            var match = YamlKeyRegex().Match(line);
            if (match.Success)
            {
                var keyGroup = match.Groups["key"];
                var keyName = keyGroup.Value;
                var startByte = currentByteOffset + keyGroup.Index;
                var endByte = startByte + keyGroup.Length;

                symbols.Add(new Symbol
                {
                    SymbolName = keyName,
                    SymbolType = SymbolType.Property,
                    Language = Language,
                    FilePath = filePath,
                    StartLine = i + 1,
                    EndLine = i + 1,
                    StartByte = startByte,
                    EndByte = endByte,
                    Signature = null,
                    DocComment = null,
                    IsPartial = false
                });
            }

            currentByteOffset += line.Length + 1;
        }

        return symbols;
    }
}
