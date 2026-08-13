using System.Text.RegularExpressions;
using CodeGraphMcp.Models;

namespace CodeGraphMcp.Parser;

public sealed partial class HtmlParser : ILanguageParser
{
    public IReadOnlyList<string> FileExtensions => [".html", ".htm", ".xml", ".csproj", ".config", ".vcxproj", ".props"];
    public string Language => "html";

    [GeneratedRegex(@"<(?<name>[a-zA-Z0-9_\-\.\:]+)", RegexOptions.Compiled)]
    private static partial Regex TagRegex();

    [GeneratedRegex(@"(?:id|name|Include|Update|class)\s*=\s*[""'](?<val>[^""']+)[""']", RegexOptions.Compiled)]
    private static partial Regex AttributeValueRegex();

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

            foreach (Match match in TagRegex().Matches(line))
            {
                var group = match.Groups["name"];
                if (group.Success && !group.Value.StartsWith("!"))
                {
                    AddSymbol(group, SymbolType.Class, i, currentByteOffset);
                }
            }

            foreach (Match match in AttributeValueRegex().Matches(line))
            {
                var group = match.Groups["val"];
                if (group.Success && !string.IsNullOrWhiteSpace(group.Value))
                {
                    AddSymbol(group, SymbolType.Property, i, currentByteOffset);
                }
            }

            currentByteOffset += line.Length + 1;
        }

        return symbols;

        void AddSymbol(Group group, SymbolType symbolType, int lineIdx, int lineByteOffset)
        {
            var symbolName = group.Value;
            var startByte = lineByteOffset + group.Index;
            var endByte = startByte + group.Length;

            symbols.Add(new Symbol
            {
                SymbolName = symbolName,
                SymbolType = symbolType,
                Language = Language,
                FilePath = filePath,
                StartLine = lineIdx + 1,
                EndLine = lineIdx + 1,
                StartByte = startByte,
                EndByte = endByte,
                Signature = null,
                DocComment = null,
                IsPartial = false
            });
        }
    }
}

