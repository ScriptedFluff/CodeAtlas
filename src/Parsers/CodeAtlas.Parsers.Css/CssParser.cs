using System.Text.RegularExpressions;
using CodeAtlas.Models;

namespace CodeAtlas.Parser;

public sealed partial class CssParser : ILanguageParser
{
    public ParserMetadata Metadata => new("css", "CSS Parser", "1.0.0", "CSS, SCSS, and LESS stylesheet parser", "CodeAtlas");
    public IReadOnlyList<string> FileExtensions => [".css", ".scss", ".less"];
    public string Language => "css";

    [GeneratedRegex(@"\.(?<name>[a-zA-Z0-9_\-]+)", RegexOptions.Compiled)]
    private static partial Regex ClassSelectorRegex();

    [GeneratedRegex(@"#(?<name>[a-zA-Z0-9_\-]+)", RegexOptions.Compiled)]
    private static partial Regex IdSelectorRegex();

    [GeneratedRegex(@"--(?<name>[a-zA-Z0-9_\-]+)\s*:", RegexOptions.Compiled)]
    private static partial Regex CustomPropertyRegex();

    [GeneratedRegex(@"@keyframes\s+(?<name>[a-zA-Z0-9_\-]+)", RegexOptions.Compiled)]
    private static partial Regex KeyframesRegex();

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
            if (trimmed.StartsWith("/*") || trimmed.StartsWith("//"))
            {
                currentByteOffset += line.Length + 1;
                continue;
            }

            var keyframesMatch = KeyframesRegex().Match(line);
            if (keyframesMatch.Success)
            {
                var group = keyframesMatch.Groups["name"];
                AddSymbol(group, SymbolType.Function, i, currentByteOffset);
            }

            foreach (Match match in CustomPropertyRegex().Matches(line))
            {
                var group = match.Groups["name"];
                AddSymbol(group, SymbolType.Variable, i, currentByteOffset);
            }

            foreach (Match match in ClassSelectorRegex().Matches(line))
            {
                var group = match.Groups["name"];
                AddSymbol(group, SymbolType.Class, i, currentByteOffset);
            }

            foreach (Match match in IdSelectorRegex().Matches(line))
            {
                var group = match.Groups["name"];
                var name = group.Value;
                if (!IsHexColor(name))
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

    private static bool IsHexColor(string name)
    {
        return (name.Length == 3 || name.Length == 6 || name.Length == 8) &&
               name.All(Uri.IsHexDigit);
    }
}
