using CodeGraphMcp.Models;
using TreeSitter;

namespace CodeGraphMcp.Parser;

public sealed class TypeScriptParser : JsTsParserBase, ILanguageParser
{
    public IReadOnlyList<string> FileExtensions => [".ts", ".tsx"];
    public string Language => "typescript";

    private static readonly TreeSitter.Language TsLanguage = new("tree-sitter-typescript", "tree_sitter_typescript");
    private static readonly ThreadLocal<TreeSitter.Parser> ThreadParser = new(() => new TreeSitter.Parser());

    private const string QueryString = """
        (interface_declaration
            name: (type_identifier) @interface.name) @interface.def

        (class_declaration
            name: (type_identifier) @class.name) @class.def

        (function_declaration
            name: (identifier) @function.name) @function.def

        (method_definition
            name: (property_identifier) @method.name) @method.def

        (type_alias_declaration
            name: (type_identifier) @typealias.name) @typealias.def
        """;


    public FileParseResult ParseFile(string filePath, string source)
    {
        var symbols = new List<Symbol>();
        var refs = new List<SymbolReference>();
        try
        {
            var parser = ThreadParser.Value!;
            parser.Language = TsLanguage;

            using var tree = parser.Parse(source);
            if (tree == null || tree.RootNode.Id == IntPtr.Zero)
                return new FileParseResult(symbols, refs);

            using var query = new Query(TsLanguage, QueryString);
            using var cursor = new QueryCursor();
            cursor.Execute(query, tree.RootNode);

            foreach (var match in cursor.Matches)
            {
                foreach (var capture in match.Captures)
                {
                    var captureName = capture.Name;
                    if (captureName == null || !captureName.EndsWith(".def")) continue;

                    var kind = captureName.Replace(".def", "");
                    var node = capture.Node;
                    var nameCapture = match.Captures
                        .FirstOrDefault(c => c.Name == $"{kind}.name");

                    if (nameCapture == null || nameCapture.Node.Id == IntPtr.Zero) continue;

                    var nameText = nameCapture.Node.Text;
                    if (string.IsNullOrWhiteSpace(nameText)) continue;

                    var symbolType = kind switch
                    {
                        "interface" => Models.SymbolType.Interface,
                        "class"     => Models.SymbolType.Class,
                        "function"  => Models.SymbolType.Function,
                        "method"    => Models.SymbolType.Method,
                        "typealias" => Models.SymbolType.TypeAlias,
                        "namespace" => Models.SymbolType.Class,
                        _           => Models.SymbolType.Class
                    };

                    symbols.Add(new Symbol
                    {
                        SymbolName = nameText,
                        SymbolType = symbolType,
                        Language   = Language,
                        FilePath   = filePath,
                        StartLine  = (int)node.StartPosition.Row + 1,
                        EndLine    = (int)node.EndPosition.Row + 1,
                        StartByte  = (int)node.StartIndex,
                        EndByte    = (int)node.EndIndex,
                        DocComment = ExtractDocComment(node),
                        IsPartial  = symbolType is Models.SymbolType.Interface || kind == "namespace"
                    });
                }
            }

            WalkForReferences(tree.RootNode, filePath, refs, enclosingSymbol: null);
        }
        catch
        {
            // Catch parser exception gracefully per ILanguageParser contract
        }

        return new FileParseResult(symbols, refs);
    }

    public IEnumerable<Symbol> Parse(string filePath, string source)
        => ParseFile(filePath, source).Symbols;

    public IEnumerable<SymbolReference> ParseReferences(string filePath, string source)
        => ParseFile(filePath, source).References;


    private static void WalkForReferences(Node node, string filePath, List<SymbolReference> refs, string? enclosingSymbol)
    {
        if (node.Id == IntPtr.Zero) return;
        var type = node.Type;

        string? currentScope = enclosingSymbol;

        if (type is "function_declaration" or "method_definition" or "class_declaration" or "interface_declaration")
        {
            foreach (var child in node.NamedChildren)
            {
                if (child.Id != IntPtr.Zero && child.Type is "identifier" or "property_identifier" or "type_identifier")
                {
                    if (!string.IsNullOrWhiteSpace(child.Text))
                    {
                        currentScope = child.Text;
                        break;
                    }
                }
            }
        }

        if (type == "call_expression")
        {
            var targetName = ExtractTsCallTarget(node);
            if (targetName is not null && !string.IsNullOrWhiteSpace(targetName))
            {
                refs.Add(new SymbolReference(
                    currentScope ?? Path.GetFileNameWithoutExtension(filePath),
                    targetName, "Call", filePath,
                    (int)node.StartPosition.Row + 1));
            }
        }
        else if (type == "new_expression")
        {
            var targetName = ExtractTsNewTarget(node);
            if (targetName is not null && !string.IsNullOrWhiteSpace(targetName))
            {
                refs.Add(new SymbolReference(
                    currentScope ?? Path.GetFileNameWithoutExtension(filePath),
                    targetName, "TypeUsage", filePath,
                    (int)node.StartPosition.Row + 1));
            }
        }
        else if (type == "import_statement")
        {
            ExtractTsImports(node, filePath, refs, currentScope);
        }

        foreach (var child in node.Children)
            WalkForReferences(child, filePath, refs, currentScope);
    }

    private static string? ExtractTsCallTarget(Node callNode)
    {
        foreach (var child in callNode.NamedChildren)
        {
            if (child.Id == IntPtr.Zero) continue;

            if (child.Type == "member_expression")
            {
                var memberChildren = child.NamedChildren.ToList();
                foreach (var memberChild in memberChildren)
                {
                    if (memberChild.Id != IntPtr.Zero && memberChild.Type == "property_identifier")
                        return memberChild.Text;
                }
                if (memberChildren.Count > 0)
                {
                    var last = memberChildren[^1];
                    if (last.Id != IntPtr.Zero) return last.Text;
                }
            }
            if (child.Type is "identifier" or "property_identifier")
            {
                return child.Text;
            }
        }
        return null;
    }

    private static string? ExtractTsNewTarget(Node newExpr)
    {
        foreach (var child in newExpr.NamedChildren)
        {
            if (child.Id != IntPtr.Zero && child.Type is "identifier" or "type_identifier")
                return child.Text;
        }
        return null;
    }

    private static void ExtractTsImports(Node importNode, string filePath, List<SymbolReference> refs, string? currentScope)
    {
        WalkImportSpecifiers(importNode, filePath, refs, currentScope);
    }

    private static void WalkImportSpecifiers(Node node, string filePath, List<SymbolReference> refs, string? currentScope)
    {
        if (node.Id == IntPtr.Zero) return;

        if (node.Type == "import_specifier")
        {
            var text = node.Text;
            if (!string.IsNullOrWhiteSpace(text))
            {
                refs.Add(new SymbolReference(
                    currentScope ?? Path.GetFileNameWithoutExtension(filePath),
                    text, "Import", filePath,
                    (int)node.StartPosition.Row + 1));
            }
            return;
        }

        foreach (var child in node.NamedChildren)
            WalkImportSpecifiers(child, filePath, refs, currentScope);
    }
}
