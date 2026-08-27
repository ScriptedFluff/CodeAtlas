namespace CodeGraphMcp.Filtering;

public enum QueryPatternKind
{
    PureIdentifier,
    GlobPattern,
    ComplexRegex
}

/// <summary>
/// Fast single-pass scanner that classifies a search string into:
/// - PureIdentifier (plain symbol name for exact + substring matching)
/// - GlobPattern (wildcards/alternations translatable directly to SQL)
/// - ComplexRegex (untranslatable regex constructs like lookarounds, non-greedy quantifiers)
/// </summary>
public static class QueryPatternClassifier
{
    public static QueryPatternKind Classify(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return QueryPatternKind.PureIdentifier;

        input = input.Trim();

        bool hasGlobTokens = false;

        for (int i = 0; i < input.Length; i++)
        {
            char c = input[i];

            if (c == '\\')
            {
                if (i + 1 < input.Length)
                {
                    char next = input[i + 1];
                    // Complex regex character classes / anchors / backreferences
                    if (next is 'b' or 'B' or 'd' or 'D' or 'w' or 'W' or 's' or 'S' or 'p' or 'P' or 'k' or '1' or '2' or '3' or '4' or '5' or '6' or '7' or '8' or '9')
                    {
                        return QueryPatternKind.ComplexRegex;
                    }
                    i++; // skip escaped char
                }
                continue;
            }

            // Lookarounds or named groups: (?=, (?!, (?<=, (?<!, (?<name>
            if (c == '(' && i + 1 < input.Length && input[i + 1] == '?')
            {
                return QueryPatternKind.ComplexRegex;
            }

            // Quantifiers like {2,4} or + or lazy quantifiers +?, *?
            if (c is '+' or '{' or '}')
            {
                return QueryPatternKind.ComplexRegex;
            }

            if (c is '*' or '?' && i + 1 < input.Length && input[i + 1] == '?')
            {
                return QueryPatternKind.ComplexRegex;
            }

            // Glob / Pattern syntax
            if (c is '*' or '?' or '[' or ']' or '|' or '^' or '$' or '(' or ')')
            {
                hasGlobTokens = true;
            }
        }

        return hasGlobTokens ? QueryPatternKind.GlobPattern : QueryPatternKind.PureIdentifier;
    }
}
