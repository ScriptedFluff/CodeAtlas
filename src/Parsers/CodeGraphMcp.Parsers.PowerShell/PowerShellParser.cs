using System.Text.RegularExpressions;
using CodeGraphMcp.Models;

namespace CodeGraphMcp.Parser;

public sealed partial class PowerShellParser : ILanguageParser
{
    public ParserMetadata Metadata => new("powershell", "PowerShell Parser", "1.0.0", "PowerShell script and module parser", "CodeGraphMcp");
    public IReadOnlyList<string> FileExtensions => [".ps1", ".psm1", ".psd1"];
    public string Language => "powershell";

    // Matches: function [scope:]Name or filter [scope:]Name or workflow [scope:]Name
    [GeneratedRegex(@"\b(?:function|filter|workflow)\s+(?:[a-zA-Z0-9_-]+:)?(?<name>[a-zA-Z0-9_\-\.]+)", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex FunctionRegex();

    // Matches param variables $ParamName or $Param-Name
    [GeneratedRegex(@"\$([a-zA-Z0-9_\-]+)", RegexOptions.Compiled)]
    private static partial Regex ParamVarRegex();

    // Matches Cmdlet / Function invocations: Verb-Noun
    [GeneratedRegex(@"\b(?<cmdlet>[a-zA-Z]+-[a-zA-Z0-9]+)\b", RegexOptions.Compiled)]
    private static partial Regex CmdletInvocationRegex();

    public IEnumerable<Symbol> Parse(string filePath, string source)
    {
        if (string.IsNullOrWhiteSpace(source))
            return [];

        try
        {
            return ParsePowerShellSymbols(filePath, source);
        }
        catch
        {
            return [];
        }
    }

    public IEnumerable<SymbolReference> ParseReferences(string filePath, string source)
    {
        if (string.IsNullOrWhiteSpace(source))
            return [];

        try
        {
            return ParsePowerShellReferences(filePath, source);
        }
        catch
        {
            return [];
        }
    }

    private static IEnumerable<Symbol> ParsePowerShellSymbols(string filePath, string source)
    {
        var symbols = new List<Symbol>();
        var lines = source.Split('\n');
        int currentByteOffset = 0;

        var scriptName = Path.GetFileNameWithoutExtension(filePath);
        if (!string.IsNullOrWhiteSpace(scriptName))
        {
            var topDoc = ExtractDocComment(lines, 0);
            symbols.Add(new Symbol
            {
                SymbolName = scriptName,
                SymbolType = SymbolType.Function,
                Language = "powershell",
                FilePath = filePath,
                StartLine = 1,
                EndLine = Math.Max(1, lines.Length),
                StartByte = 0,
                EndByte = source.Length,
                Signature = $"script {Path.GetFileName(filePath)}",
                DocComment = topDoc,
                IsPartial = false
            });
        }

        bool inTopLevelParamBlock = false;

        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            var trimmed = line.TrimStart();

            if (trimmed.StartsWith('#'))
            {
                currentByteOffset += line.Length + 1;
                continue;
            }

            var funcMatch = FunctionRegex().Match(line);
            if (funcMatch.Success)
            {
                var nameGroup = funcMatch.Groups["name"];
                var funcName = nameGroup.Value;
                var startLine = i + 1;
                var startByte = currentByteOffset + nameGroup.Index;
                var endByte = startByte + nameGroup.Length;

                int endLine = FindMatchingEndLine(lines, i);
                string? signature = ExtractSignature(lines, i, funcName);
                string? docComment = ExtractDocComment(lines, i);

                symbols.Add(new Symbol
                {
                    SymbolName = funcName,
                    SymbolType = SymbolType.Function,
                    Language = "powershell",
                    FilePath = filePath,
                    StartLine = startLine,
                    EndLine = endLine,
                    StartByte = startByte,
                    EndByte = endByte,
                    Signature = signature,
                    DocComment = docComment,
                    IsPartial = false
                });

                var funcParams = ExtractParameters(lines, i, endLine, filePath, funcName, currentByteOffset);
                symbols.AddRange(funcParams);
            }
            else
            {
                if (trimmed.StartsWith("param", StringComparison.OrdinalIgnoreCase))
                {
                    inTopLevelParamBlock = true;
                }

                if (inTopLevelParamBlock)
                {
                    var matches = ParamVarRegex().Matches(line);
                    foreach (Match match in matches)
                    {
                        var paramName = "$" + match.Groups[1].Value;
                        if (!symbols.Any(s => s.SymbolName == paramName && s.StartLine == i + 1))
                        {
                            symbols.Add(new Symbol
                            {
                                SymbolName = paramName,
                                SymbolType = SymbolType.Variable,
                                ClassName = scriptName,
                                Language = "powershell",
                                FilePath = filePath,
                                StartLine = i + 1,
                                EndLine = i + 1,
                                StartByte = currentByteOffset + match.Index,
                                EndByte = currentByteOffset + match.Index + match.Length,
                                Signature = $"Param: {paramName}",
                                DocComment = null,
                                IsPartial = false
                            });
                        }
                    }

                    if (line.Contains(')'))
                    {
                        inTopLevelParamBlock = false;
                    }
                }
            }

            currentByteOffset += line.Length + 1;
        }

        return symbols;
    }

    private static int FindMatchingEndLine(string[] lines, int startLineIndex)
    {
        int braceDepth = 0;
        bool foundOpeningBrace = false;

        for (int j = startLineIndex; j < lines.Length; j++)
        {
            var line = lines[j];
            foreach (char c in line)
            {
                if (c == '{')
                {
                    braceDepth++;
                    foundOpeningBrace = true;
                }
                else if (c == '}')
                {
                    braceDepth--;
                    if (foundOpeningBrace && braceDepth <= 0)
                    {
                        return j + 1;
                    }
                }
            }
        }

        return startLineIndex + 1;
    }

    private static string? ExtractSignature(string[] lines, int lineIndex, string funcName)
    {
        var line = lines[lineIndex].Trim();
        if (line.Contains('(') && line.Contains(')'))
        {
            var paramStart = line.IndexOf('(');
            var paramEnd = line.LastIndexOf(')');
            if (paramEnd > paramStart)
            {
                var paramsText = line.Substring(paramStart, paramEnd - paramStart + 1);
                return $"{funcName}{paramsText}";
            }
        }
        return $"function {funcName}";
    }

    private static string? ExtractDocComment(string[] lines, int funcLineIndex)
    {
        if (funcLineIndex == 0) return null;

        var comments = new List<string>();
        for (int j = funcLineIndex - 1; j >= 0; j--)
        {
            var line = lines[j].Trim();
            if (line.StartsWith('#'))
            {
                comments.Add(line.TrimStart('#', ' '));
            }
            else if (line.EndsWith("#>"))
            {
                for (int k = j; k >= 0; k--)
                {
                    var commentLine = lines[k].Trim();
                    comments.Add(commentLine.Trim('#', '<', '>', ' '));
                    if (commentLine.StartsWith("<#"))
                    {
                        j = k;
                        break;
                    }
                }
            }
            else
            {
                break;
            }
        }

        if (comments.Count == 0) return null;

        comments.Reverse();
        return string.Join("\n", comments).Trim();
    }

    private static IEnumerable<Symbol> ExtractParameters(string[] lines, int startLineIndex, int endLineIndex, string filePath, string funcName, int baseByteOffset)
    {
        var paramSymbols = new List<Symbol>();
        int byteOffset = baseByteOffset;
        bool insideParamBlock = false;

        for (int j = startLineIndex; j < Math.Min(lines.Length, endLineIndex); j++)
        {
            var line = lines[j];
            var trimmed = line.TrimStart();

            if (trimmed.StartsWith("param", StringComparison.OrdinalIgnoreCase))
            {
                insideParamBlock = true;
            }

            if (insideParamBlock || j == startLineIndex)
            {
                var matches = ParamVarRegex().Matches(line);
                foreach (Match match in matches)
                {
                    var paramName = "$" + match.Groups[1].Value;
                    paramSymbols.Add(new Symbol
                    {
                        SymbolName = paramName,
                        SymbolType = SymbolType.Variable,
                        ClassName = funcName,
                        Language = "powershell",
                        FilePath = filePath,
                        StartLine = j + 1,
                        EndLine = j + 1,
                        StartByte = byteOffset + match.Index,
                        EndByte = byteOffset + match.Index + match.Length,
                        Signature = $"Param: {paramName}",
                        DocComment = null,
                        IsPartial = false
                    });
                }

                if (insideParamBlock && line.Contains(')'))
                {
                    insideParamBlock = false;
                }
            }

            byteOffset += line.Length + 1;
        }

        return paramSymbols;
    }

    private static IEnumerable<SymbolReference> ParsePowerShellReferences(string filePath, string source)
    {
        var refs = new List<SymbolReference>();
        var lines = source.Split('\n');

        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            var trimmed = line.TrimStart();
            if (trimmed.StartsWith('#'))
            {
                continue;
            }

            var matches = CmdletInvocationRegex().Matches(line);
            foreach (Match m in matches)
            {
                var cmdlet = m.Groups["cmdlet"].Value;
                refs.Add(new SymbolReference(
                    CallerSymbolName: "<top-level>",
                    TargetSymbolName: cmdlet,
                    ReferenceType: "Call",
                    FilePath: filePath,
                    LineNumber: i + 1
                ));
            }
        }

        return refs;
    }
}
