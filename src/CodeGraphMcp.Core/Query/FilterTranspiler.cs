using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;

namespace CodeGraphMcp.Filtering;

/// <summary>
/// Parser and SQL transpiler for filter expressions and primary query globs.
/// Translates pattern and regex expressions to native SQLite WHERE clauses (LIKE, GLOB, OR)
/// or provides a compiled .NET Regex for streaming chunked fallback.
/// </summary>
public static class FilterTranspiler
{
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(500);

    /// <summary>
    /// Transpiles a filter pattern into a SQLite WHERE clause or returns a compiled fallback Regex.
    /// </summary>
    public static FilterTranspileResult Transpile(
        string? pattern,
        string columnName = "symbol_name",
        string paramPrefix = "fp")
    {
        if (string.IsNullOrWhiteSpace(pattern))
        {
            var matchAll = new Regex(".*", RegexOptions.Compiled, RegexTimeout);
            return new FilterTranspileResult(
                IsTranslatable: true,
                SqlClause: null,
                Parameters: Array.Empty<SqliteParameter>(),
                FallbackRegex: null,
                RegexMatcher: matchAll,
                OriginalPattern: string.Empty);
        }

        pattern = pattern.Trim();

        // 1. Try parsing into AST
        if (TryParseAst(pattern, out var ast) && ast is not null)
        {
            try
            {
                var parameters = new List<SqliteParameter>();
                int paramCounter = 0;
                var sql = EmitSqlClause(ast, columnName, paramPrefix, ref paramCounter, parameters, exactGlobBoundaries: false);
                if (!string.IsNullOrWhiteSpace(sql))
                {
                    var regexMatcher = BuildRegexMatcher(ast, pattern, exactGlobBoundaries: false);
                    return new FilterTranspileResult(
                        IsTranslatable: true,
                        SqlClause: sql,
                        Parameters: parameters,
                        FallbackRegex: null,
                        RegexMatcher: regexMatcher,
                        OriginalPattern: pattern);
                }
            }
            catch
            {
                // Fall through to Regex fallback on any SQL translation issue
            }
        }

        // 2. Regex fallback
        try
        {
            var fallbackRegex = new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.Compiled, RegexTimeout);
            return new FilterTranspileResult(
                IsTranslatable: false,
                SqlClause: null,
                Parameters: Array.Empty<SqliteParameter>(),
                FallbackRegex: fallbackRegex,
                RegexMatcher: fallbackRegex,
                OriginalPattern: pattern);
        }
        catch (ArgumentException ex)
        {
            throw new ArgumentException($"Invalid pattern or regular expression '{pattern}': {ex.Message}", nameof(pattern), ex);
        }
    }

    /// <summary>
    /// Transpiles a primary query pattern into a SQLite WHERE clause with strict boundary preservation.
    /// Throws ArgumentException if the pattern contains complex regex constructs not translatable to SQL.
    /// </summary>
    public static FilterTranspileResult TranspilePrimaryQuery(
        string pattern,
        string columnName = "symbol_name",
        string paramPrefix = "pq")
    {
        if (string.IsNullOrWhiteSpace(pattern))
        {
            var matchAll = new Regex(".*", RegexOptions.Compiled, RegexTimeout);
            return new FilterTranspileResult(
                IsTranslatable: true,
                SqlClause: null,
                Parameters: Array.Empty<SqliteParameter>(),
                FallbackRegex: null,
                RegexMatcher: matchAll,
                OriginalPattern: string.Empty);
        }

        pattern = pattern.Trim();
        var kind = QueryPatternClassifier.Classify(pattern);

        if (kind == QueryPatternKind.ComplexRegex)
        {
            throw new ArgumentException(
                $"Primary query pattern '{pattern}' contains complex regular expression constructs that cannot be pushed down to SQL. Use the --filter flag for regex post-filtering.",
                nameof(pattern));
        }

        if (kind == QueryPatternKind.PureIdentifier)
        {
            return Transpile(pattern, columnName, paramPrefix);
        }

        // GlobPattern: parse AST and emit boundary-preserving SQL
        if (TryParseAst(pattern, out var ast) && ast is not null)
        {
            var parameters = new List<SqliteParameter>();
            int paramCounter = 0;
            var sql = EmitSqlClause(ast, columnName, paramPrefix, ref paramCounter, parameters, exactGlobBoundaries: true);
            if (!string.IsNullOrWhiteSpace(sql))
            {
                var regexMatcher = BuildRegexMatcher(ast, pattern, exactGlobBoundaries: true);
                return new FilterTranspileResult(
                    IsTranslatable: true,
                    SqlClause: sql,
                    Parameters: parameters,
                    FallbackRegex: null,
                    RegexMatcher: regexMatcher,
                    OriginalPattern: pattern);
            }
        }

        throw new ArgumentException(
            $"Primary query pattern '{pattern}' could not be transpiled to a SQL glob expression. Use the --filter flag for regex post-filtering.",
            nameof(pattern));
    }

    private static Regex BuildRegexMatcher(FilterNode? ast, string pattern, bool exactGlobBoundaries = false)
    {
        bool hasCharClass = HasCharClassNode(ast);
        var options = hasCharClass
            ? RegexOptions.Compiled
            : RegexOptions.IgnoreCase | RegexOptions.Compiled;

        if (exactGlobBoundaries)
        {
            var sb = new StringBuilder();
            sb.Append('^');
            BuildRegexFromAst(ast, sb, exactGlobBoundaries: true);
            sb.Append('$');
            return new Regex(sb.ToString(), options, RegexTimeout);
        }

        try
        {
            return new Regex(pattern, options, RegexTimeout);
        }
        catch
        {
            var sb = new StringBuilder();
            BuildRegexFromAst(ast, sb, exactGlobBoundaries: false);
            return new Regex(sb.ToString(), options, RegexTimeout);
        }
    }

    private static bool HasCharClassNode(FilterNode? node)
    {
        if (node == null) return false;
        switch (node)
        {
            case CharClassNode:
                return true;
            case GroupNode g:
                return HasCharClassNode(g.Inner);
            case AlternationNode alt:
                return alt.Branches.Any(HasCharClassNode);
            case SequenceNode seq:
                return seq.Nodes.Any(HasCharClassNode);
            default:
                return false;
        }
    }

    private static void BuildRegexFromAst(FilterNode? node, StringBuilder sb, bool exactGlobBoundaries = false)
    {
        if (node == null) return;
        switch (node)
        {
            case AlternationNode alt:
                sb.Append('(');
                for (int i = 0; i < alt.Branches.Count; i++)
                {
                    if (i > 0) sb.Append('|');
                    BuildRegexFromAst(alt.Branches[i], sb, exactGlobBoundaries);
                }
                sb.Append(')');
                break;
            case GroupNode grp:
                sb.Append('(');
                BuildRegexFromAst(grp.Inner, sb, exactGlobBoundaries);
                sb.Append(')');
                break;
            case SequenceNode seq:
                bool hasWildcard = seq.Nodes.Any(n => n is MultiCharWildcardNode or SingleCharWildcardNode or CharClassNode);
                if (exactGlobBoundaries && !hasWildcard)
                {
                    sb.Append(".*");
                }
                foreach (var n in seq.Nodes) BuildRegexFromAst(n, sb, exactGlobBoundaries);
                if (exactGlobBoundaries && !hasWildcard)
                {
                    sb.Append(".*");
                }
                break;
            case LiteralNode lit:
                sb.Append(Regex.Escape(lit.Value));
                break;
            case SingleCharWildcardNode:
                sb.Append('.');
                break;
            case MultiCharWildcardNode:
                sb.Append(".*");
                break;
            case CharClassNode cc:
                sb.Append('[').Append(cc.ClassContent).Append(']');
                break;
            case StartAnchorNode:
                sb.Append('^');
                break;
            case EndAnchorNode:
                sb.Append('$');
                break;
        }
    }

    /// <summary>
    /// Attempts to parse the filter string into an AST.
    /// Returns false if complex regex features are detected (lookarounds, backreferences, quantifiers, etc.).
    /// </summary>
    public static bool TryParseAst(string input, out FilterNode? ast)
    {
        ast = null;
        if (string.IsNullOrEmpty(input)) return false;

        // Quick rejection for complex regex features
        if (ContainsComplexRegexFeatures(input))
        {
            return false;
        }

        int index = 0;
        try
        {
            ast = ParseAlternation(input, ref index);
            if (index == input.Length)
            {
                return true;
            }
            ast = null;
            return false;
        }
        catch
        {
            ast = null;
            return false;
        }
    }

    private static bool ContainsComplexRegexFeatures(string s)
    {
        for (int i = 0; i < s.Length; i++)
        {
            char c = s[i];
            if (c == '\\')
            {
                if (i + 1 < s.Length)
                {
                    char next = s[i + 1];
                    // Word boundaries, digits, whitespace, word characters, unicode categories
                    if (next is 'b' or 'B' or 'd' or 'D' or 'w' or 'W' or 's' or 'S' or 'p' or 'P' or 'k' or '1' or '2' or '3' or '4' or '5' or '6' or '7' or '8' or '9')
                    {
                        return true;
                    }
                    i++; // skip escaped char
                }
                continue;
            }

            // Lookarounds or named groups: (?=, (?!, (?<=, (?<!, (?<name>
            if (c == '(' && i + 1 < s.Length && s[i + 1] == '?')
            {
                return true;
            }

            // Quantifiers like {2,4} or + or lazy quantifiers +?, *?
            if (c is '+' or '{' or '}')
            {
                return true;
            }

            if (c is '*' or '?' && i + 1 < s.Length && s[i + 1] == '?')
            {
                return true;
            }
        }
        return false;
    }

    private static FilterNode ParseAlternation(string input, ref int index)
    {
        var branches = new List<FilterNode>();

        while (index <= input.Length)
        {
            var seq = ParseSequence(input, ref index);
            branches.Add(seq);

            if (index < input.Length && input[index] == '|')
            {
                index++; // consume '|'
                continue;
            }
            break;
        }

        return branches.Count == 1 ? branches[0] : new AlternationNode(branches);
    }

    private static FilterNode ParseSequence(string input, ref int index)
    {
        var nodes = new List<FilterNode>();
        var literalBuilder = new StringBuilder();

        void FlushLiteral()
        {
            if (literalBuilder.Length > 0)
            {
                nodes.Add(new LiteralNode(literalBuilder.ToString()));
                literalBuilder.Clear();
            }
        }

        while (index < input.Length)
        {
            char c = input[index];

            if (c == '|')
            {
                break;
            }

            if (c == ')')
            {
                break;
            }

            if (c == '(')
            {
                FlushLiteral();
                index++; // consume '('
                var inner = ParseAlternation(input, ref index);
                if (index < input.Length && input[index] == ')')
                {
                    index++; // consume ')'
                }
                nodes.Add(new GroupNode(inner));
                continue;
            }

            if (c == '^')
            {
                FlushLiteral();
                nodes.Add(new StartAnchorNode());
                index++;
                continue;
            }

            if (c == '$')
            {
                FlushLiteral();
                nodes.Add(new EndAnchorNode());
                index++;
                continue;
            }

            if (c == '[')
            {
                FlushLiteral();
                int closeIndex = input.IndexOf(']', index + 1);
                if (closeIndex > index)
                {
                    string content = input.Substring(index + 1, closeIndex - index - 1);
                    nodes.Add(new CharClassNode(content));
                    index = closeIndex + 1;
                    continue;
                }
            }

            if (c == '.')
            {
                if (index + 1 < input.Length && input[index + 1] == '*')
                {
                    FlushLiteral();
                    nodes.Add(new MultiCharWildcardNode());
                    index += 2;
                    continue;
                }
                FlushLiteral();
                nodes.Add(new SingleCharWildcardNode());
                index++;
                continue;
            }

            if (c == '*')
            {
                FlushLiteral();
                nodes.Add(new MultiCharWildcardNode());
                index++;
                continue;
            }

            if (c == '\\')
            {
                if (index + 1 < input.Length)
                {
                    char escaped = input[index + 1];
                    literalBuilder.Append(escaped);
                    index += 2;
                    continue;
                }
            }

            literalBuilder.Append(c);
            index++;
        }

        FlushLiteral();

        if (nodes.Count == 0) return new LiteralNode(string.Empty);
        if (nodes.Count == 1) return nodes[0];
        return new SequenceNode(nodes);
    }

    private static string EmitSqlClause(
        FilterNode node,
        string columnName,
        string paramPrefix,
        ref int paramCounter,
        List<SqliteParameter> parameters,
        bool exactGlobBoundaries = false)
    {
        switch (node)
        {
            case AlternationNode alt:
            {
                var branchClauses = new List<string>();
                foreach (var branch in alt.Branches)
                {
                    var clause = EmitSqlClause(branch, columnName, paramPrefix, ref paramCounter, parameters, exactGlobBoundaries);
                    if (!string.IsNullOrWhiteSpace(clause))
                    {
                        branchClauses.Add(clause);
                    }
                }
                if (branchClauses.Count == 0) return string.Empty;
                if (branchClauses.Count == 1) return branchClauses[0];
                return $"({string.Join(" OR ", branchClauses)})";
            }

            case GroupNode grp:
            {
                var innerClause = EmitSqlClause(grp.Inner, columnName, paramPrefix, ref paramCounter, parameters, exactGlobBoundaries);
                return string.IsNullOrWhiteSpace(innerClause) ? string.Empty : $"({innerClause})";
            }

            case SequenceNode seq:
            {
                return EmitSequenceSql(seq.Nodes, columnName, paramPrefix, ref paramCounter, parameters, exactGlobBoundaries);
            }

            case LiteralNode lit:
            {
                return EmitSequenceSql(new[] { lit }, columnName, paramPrefix, ref paramCounter, parameters, exactGlobBoundaries);
            }

            case CharClassNode cc:
            {
                return EmitSequenceSql(new[] { cc }, columnName, paramPrefix, ref paramCounter, parameters, exactGlobBoundaries);
            }

            case SingleCharWildcardNode sc:
            {
                return EmitSequenceSql(new[] { sc }, columnName, paramPrefix, ref paramCounter, parameters, exactGlobBoundaries);
            }

            case MultiCharWildcardNode mc:
            {
                return EmitSequenceSql(new[] { mc }, columnName, paramPrefix, ref paramCounter, parameters, exactGlobBoundaries);
            }

            default:
                return string.Empty;
        }
    }

    private static string EmitSequenceSql(
        IReadOnlyList<FilterNode> nodes,
        string columnName,
        string paramPrefix,
        ref int paramCounter,
        List<SqliteParameter> parameters,
        bool exactGlobBoundaries = false)
    {
        bool hasStartAnchor = false;
        bool hasEndAnchor = false;
        bool hasCharClass = false;

        var cleanNodes = new List<FilterNode>();
        for (int i = 0; i < nodes.Count; i++)
        {
            var n = nodes[i];
            if (n is StartAnchorNode)
            {
                if (i == 0) hasStartAnchor = true;
                continue;
            }
            if (n is EndAnchorNode)
            {
                if (i == nodes.Count - 1 || (i == nodes.Count - 2 && nodes[^1] is EndAnchorNode)) hasEndAnchor = true;
                continue;
            }
            if (n is CharClassNode)
            {
                hasCharClass = true;
            }
            if (n is GroupNode or AlternationNode)
            {
                // Sub-group within sequence
                var subClause = EmitSqlClause(n, columnName, paramPrefix, ref paramCounter, parameters, exactGlobBoundaries);
                return subClause;
            }
            cleanNodes.Add(n);
        }

        if (cleanNodes.Count == 0)
        {
            return string.Empty;
        }

        bool hasWildcard = cleanNodes.Any(n => n is MultiCharWildcardNode or SingleCharWildcardNode);

        // If it contains a character class or explicit glob wildcards with exact boundaries, use SQLite GLOB
        if (hasCharClass || (exactGlobBoundaries && hasWildcard))
        {
            var globPattern = new StringBuilder();
            if (!exactGlobBoundaries && !hasStartAnchor && cleanNodes[0] is not MultiCharWildcardNode)
            {
                globPattern.Append('*');
            }

            foreach (var n in cleanNodes)
            {
                switch (n)
                {
                    case LiteralNode l:
                        globPattern.Append(l.Value);
                        break;
                    case SingleCharWildcardNode:
                        globPattern.Append('?');
                        break;
                    case MultiCharWildcardNode:
                        globPattern.Append('*');
                        break;
                    case CharClassNode cc:
                        globPattern.Append('[').Append(cc.ClassContent).Append(']');
                        break;
                }
            }

            if (!exactGlobBoundaries && !hasEndAnchor && cleanNodes[^1] is not MultiCharWildcardNode)
            {
                globPattern.Append('*');
            }

            string paramName = $"@{paramPrefix}{paramCounter++}";
            parameters.Add(new SqliteParameter(paramName, globPattern.ToString()));
            return $"{columnName} GLOB {paramName}";
        }
        else
        {
            // Translate to SQLite LIKE ... ESCAPE '\' COLLATE NOCASE
            var likePattern = new StringBuilder();
            bool shouldPrependPercent = !hasStartAnchor && cleanNodes[0] is not MultiCharWildcardNode;
            if (exactGlobBoundaries && hasWildcard) shouldPrependPercent = false;

            if (shouldPrependPercent) likePattern.Append('%');

            foreach (var n in cleanNodes)
            {
                switch (n)
                {
                    case LiteralNode l:
                        foreach (char ch in l.Value)
                        {
                            if (ch is '%' or '_' or '\\')
                            {
                                likePattern.Append('\\');
                            }
                            likePattern.Append(ch);
                        }
                        break;
                    case SingleCharWildcardNode:
                        likePattern.Append('_');
                        break;
                    case MultiCharWildcardNode:
                        likePattern.Append('%');
                        break;
                }
            }

            bool shouldAppendPercent = !hasEndAnchor && cleanNodes[^1] is not MultiCharWildcardNode;
            if (exactGlobBoundaries && hasWildcard) shouldAppendPercent = false;

            if (shouldAppendPercent) likePattern.Append('%');

            string paramName = $"@{paramPrefix}{paramCounter++}";
            parameters.Add(new SqliteParameter(paramName, likePattern.ToString()));
            return $"{columnName} LIKE {paramName} ESCAPE '\\' COLLATE NOCASE";
        }
    }
}
