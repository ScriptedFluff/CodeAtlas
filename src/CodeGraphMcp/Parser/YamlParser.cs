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

        try
        {
            return ParseIndentedYaml(filePath, source);
        }
        catch
        {
            return ParseRegexFallback(filePath, source);
        }
    }

    private static IEnumerable<Symbol> ParseIndentedYaml(string filePath, string source)
    {
        var symbols = new List<Symbol>();
        var lines = source.Split('\n');
        int currentByteOffset = 0;

        // Stack to maintain hierarchy: (indentation level, key name)
        var stack = new Stack<(int Indent, string Key)>();

        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            var trimmed = line.TrimStart();

            if (string.IsNullOrWhiteSpace(trimmed) || trimmed.StartsWith('#'))
            {
                currentByteOffset += line.Length + 1;
                continue;
            }

            var indent = GetIndentLevel(line);
            var match = YamlKeyRegex().Match(line);

            if (match.Success)
            {
                var keyGroup = match.Groups["key"];
                var keyName = keyGroup.Value;
                var startByte = currentByteOffset + keyGroup.Index;
                var endByte = startByte + keyGroup.Length;

                // Pop stack frames that are at or deeper than current indentation
                while (stack.Count > 0 && stack.Peek().Indent >= indent)
                {
                    stack.Pop();
                }

                var parentPath = string.Join(".", stack.Select(s => s.Key).Reverse());
                var fullPath = string.IsNullOrEmpty(parentPath) ? keyName : $"{parentPath}.{keyName}";

                stack.Push((indent, keyName));

                // Add single key symbol
                symbols.Add(new Symbol
                {
                    SymbolName = keyName,
                    SymbolType = SymbolType.Property,
                    Language = "yaml",
                    FilePath = filePath,
                    StartLine = i + 1,
                    EndLine = i + 1,
                    StartByte = startByte,
                    EndByte = endByte,
                    Signature = fullPath != keyName ? $"Path: {fullPath}" : null,
                    DocComment = null,
                    IsPartial = false
                });

                // If nested path, also emit full path symbol if distinct
                if (fullPath != keyName)
                {
                    symbols.Add(new Symbol
                    {
                        SymbolName = fullPath,
                        SymbolType = SymbolType.Property,
                        Language = "yaml",
                        FilePath = filePath,
                        StartLine = i + 1,
                        EndLine = i + 1,
                        StartByte = startByte,
                        EndByte = endByte,
                        Signature = $"Property: {keyName}",
                        DocComment = null,
                        IsPartial = false
                    });
                }
            }

            currentByteOffset += line.Length + 1;
        }

        return symbols;
    }

    private static int GetIndentLevel(string line)
    {
        int count = 0;
        foreach (char c in line)
        {
            if (c == ' ') count++;
            else if (c == '\t') count += 4;
            else break;
        }
        return count;
    }

    private IEnumerable<Symbol> ParseRegexFallback(string filePath, string source)
    {
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
