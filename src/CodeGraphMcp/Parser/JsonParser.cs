using System.Text.RegularExpressions;
using CodeGraphMcp.Models;

namespace CodeGraphMcp.Parser;

public sealed partial class JsonParser : ILanguageParser
{
    public IReadOnlyList<string> FileExtensions => [".json"];
    public string Language => "json";

    [GeneratedRegex(""""(?<key>[a-zA-Z0-9_\-\.]+)"\s*:"""", RegexOptions.Compiled)]
    private static partial Regex JsonKeyRegex();

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
            var match = JsonKeyRegex().Match(line);
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
