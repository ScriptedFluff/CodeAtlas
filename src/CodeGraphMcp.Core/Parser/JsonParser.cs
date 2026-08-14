using System.Text.Json;
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

        var options = new JsonDocumentOptions
        {
            CommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true
        };

        try
        {
            using var doc = JsonDocument.Parse(source, options);
            var symbols = new List<Symbol>();
            var lineOffsets = BuildLineOffsets(source);
            int searchStartByte = 0;

            TraverseElement(doc.RootElement, "", filePath, source, lineOffsets, symbols, ref searchStartByte);
            return symbols;
        }
        catch (JsonException)
        {
            // Fault-tolerant regex fallback for malformed JSON documents
            return ParseRegexFallback(filePath, source);
        }
    }

    private static void TraverseElement(
        JsonElement element,
        string currentPath,
        string filePath,
        string source,
        List<int> lineOffsets,
        List<Symbol> symbols,
        ref int searchStartByte)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var prop in element.EnumerateObject())
            {
                var propName = prop.Name;
                var fullPath = string.IsNullOrEmpty(currentPath) ? propName : $"{currentPath}.{propName}";

                (int startLine, int startByte) = FindKeyLocation(source, propName, lineOffsets, ref searchStartByte);

                // Add flat key symbol
                symbols.Add(new Symbol
                {
                    SymbolName = propName,
                    SymbolType = SymbolType.Property,
                    Language = "json",
                    FilePath = filePath,
                    StartLine = startLine,
                    EndLine = startLine,
                    StartByte = startByte,
                    EndByte = startByte + propName.Length,
                    Signature = fullPath != propName ? $"Path: {fullPath}" : null,
                    DocComment = null,
                    IsPartial = false
                });

                // If nested path, also emit full path symbol if distinct
                if (fullPath != propName)
                {
                    symbols.Add(new Symbol
                    {
                        SymbolName = fullPath,
                        SymbolType = SymbolType.Property,
                        Language = "json",
                        FilePath = filePath,
                        StartLine = startLine,
                        EndLine = startLine,
                        StartByte = startByte,
                        EndByte = startByte + propName.Length,
                        Signature = $"Property: {propName}",
                        DocComment = null,
                        IsPartial = false
                    });
                }

                TraverseElement(prop.Value, fullPath, filePath, source, lineOffsets, symbols, ref searchStartByte);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                TraverseElement(item, currentPath, filePath, source, lineOffsets, symbols, ref searchStartByte);
            }
        }
    }

    private static List<int> BuildLineOffsets(string source)
    {
        var offsets = new List<int> { 0 };
        for (int i = 0; i < source.Length; i++)
        {
            if (source[i] == '\n')
            {
                offsets.Add(i + 1);
            }
        }
        return offsets;
    }

    private static (int Line, int ByteOffset) FindKeyLocation(string source, string keyName, List<int> lineOffsets, ref int searchStartByte)
    {
        var target = $"\"{keyName}\"";
        var idx = source.IndexOf(target, searchStartByte, StringComparison.Ordinal);
        if (idx < 0)
        {
            // Fallback to searching from start if order shifted
            idx = source.IndexOf(target, StringComparison.Ordinal);
            if (idx < 0) return (1, 0);
        }

        searchStartByte = idx + target.Length;

        int line = 1;
        for (int i = 0; i < lineOffsets.Count; i++)
        {
            if (lineOffsets[i] <= idx) line = i + 1;
            else break;
        }

        return (line, idx);
    }

    private IEnumerable<Symbol> ParseRegexFallback(string filePath, string source)
    {
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
