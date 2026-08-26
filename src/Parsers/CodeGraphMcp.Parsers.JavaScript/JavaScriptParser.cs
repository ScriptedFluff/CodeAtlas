using CodeGraphMcp.Models;
using TreeSitter;
using SymbolType = CodeGraphMcp.Models.SymbolType;

namespace CodeGraphMcp.Parser;

public sealed class JavaScriptParser : JsTsParserBase, ILanguageParser
{
    public ParserMetadata Metadata => new("javascript", "JavaScript Parser", "1.0.0", "JavaScript and JSX TreeSitter parser", "CodeGraphMcp");
    public IReadOnlyList<string> FileExtensions => [".js", ".jsx", ".mjs", ".cjs"];
    public string Language => "javascript";

    private static readonly TreeSitter.Language JsLanguage = new("tree-sitter-typescript", "tree_sitter_typescript");
    private static readonly ThreadLocal<TreeSitter.Parser> ThreadParser = new(() => new TreeSitter.Parser());

    public FileParseResult ParseFile(string filePath, string source)
    {
        var symbols = new List<Symbol>();
        var refs = new List<SymbolReference>();
        try
        {
            var parser = ThreadParser.Value!;
            parser.Language = JsLanguage;

            using var tree = parser.Parse(source);
            if (tree == null || tree.RootNode.Id == IntPtr.Zero)
                return new FileParseResult(symbols, refs);

            TraverseNode(tree.RootNode, filePath, symbols, currentClass: null);
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

    private static void TraverseNode(Node node, string filePath, List<Symbol> symbols, string? currentClass)
    {
        if (node.Id == IntPtr.Zero) return;
        var type = node.Type;

        string? nextClass = currentClass;

        if (type == "class_declaration")
        {
            var nameNode = GetChildByType(node, "type_identifier", "identifier");
            if (nameNode is { Id: not 0 } n && !string.IsNullOrWhiteSpace(n.Text))
            {
                var className = n.Text;
                nextClass = className;
                symbols.Add(new Symbol
                {
                    SymbolName = className,
                    SymbolType = SymbolType.Class,
                    Language = "javascript",
                    FilePath = filePath,
                    StartLine = (int)node.StartPosition.Row + 1,
                    EndLine = (int)node.EndPosition.Row + 1,
                    StartByte = (int)node.StartIndex,
                    EndByte = (int)node.EndIndex,
                    DocComment = ExtractDocComment(node)
                });
            }
        }
        else if (type is "function_declaration" or "generator_function_declaration")
        {
            var nameNode = GetChildByType(node, "identifier");
            if (nameNode is { Id: not 0 } n && !string.IsNullOrWhiteSpace(n.Text))
            {
                symbols.Add(new Symbol
                {
                    SymbolName = n.Text,
                    SymbolType = SymbolType.Function,
                    ClassName = currentClass,
                    Language = "javascript",
                    FilePath = filePath,
                    StartLine = (int)node.StartPosition.Row + 1,
                    EndLine = (int)node.EndPosition.Row + 1,
                    StartByte = (int)node.StartIndex,
                    EndByte = (int)node.EndIndex,
                    DocComment = ExtractDocComment(node)
                });
            }
        }
        else if (type == "method_definition")
        {
            var nameNode = GetChildByType(node, "property_identifier", "identifier");
            if (nameNode is { Id: not 0 } n && !string.IsNullOrWhiteSpace(n.Text))
            {
                symbols.Add(new Symbol
                {
                    SymbolName = n.Text,
                    SymbolType = SymbolType.Method,
                    ClassName = currentClass,
                    Language = "javascript",
                    FilePath = filePath,
                    StartLine = (int)node.StartPosition.Row + 1,
                    EndLine = (int)node.EndPosition.Row + 1,
                    StartByte = (int)node.StartIndex,
                    EndByte = (int)node.EndIndex,
                    DocComment = ExtractDocComment(node)
                });
            }
        }
        else if (type == "variable_declarator")
        {
            var nameNode = GetChildByType(node, "identifier");
            if (nameNode is { Id: not 0 } n && !string.IsNullOrWhiteSpace(n.Text))
            {
                var valueNode = GetValueChild(node);
                if (valueNode is { Id: not 0 } v && v.Type is "arrow_function" or "function_expression" or "function")
                {
                    symbols.Add(new Symbol
                    {
                        SymbolName = n.Text,
                        SymbolType = SymbolType.Function,
                        ClassName = currentClass,
                        Language = "javascript",
                        FilePath = filePath,
                        StartLine = (int)node.StartPosition.Row + 1,
                        EndLine = (int)node.EndPosition.Row + 1,
                        StartByte = (int)node.StartIndex,
                        EndByte = (int)node.EndIndex,
                        DocComment = ExtractDocComment(node)
                    });
                }
            }
        }

        foreach (var child in node.Children)
        {
            TraverseNode(child, filePath, symbols, nextClass);
        }
    }

    private static void WalkForReferences(Node node, string filePath, List<SymbolReference> refs, string? enclosingSymbol)
    {
        if (node.Id == IntPtr.Zero) return;
        var type = node.Type;

        string? currentScope = enclosingSymbol;

        if (type is "function_declaration" or "generator_function_declaration" or "method_definition" or "class_declaration")
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
            var targetName = ExtractJsCallTarget(node, filePath, refs, currentScope);
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
            var targetName = ExtractJsNewTarget(node);
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
            ExtractJsImports(node, filePath, refs, currentScope);
        }

        foreach (var child in node.Children)
            WalkForReferences(child, filePath, refs, currentScope);
    }

    private static string? ExtractJsCallTarget(Node callNode, string filePath, List<SymbolReference> refs, string? currentScope)
    {
        var namedChildren = callNode.NamedChildren.ToList();
        if (namedChildren.Count == 0) return null;
        var firstChild = namedChildren[0];
        if (firstChild.Id == IntPtr.Zero) return null;

        // CommonJS require('module') check
        if (firstChild.Type == "identifier" && firstChild.Text == "require")
        {
            if (namedChildren.Count > 1)
            {
                var argList = namedChildren[1];
                if (argList.Id != IntPtr.Zero && argList.Type == "arguments")
                {
                    var stringLiteral = argList.NamedChildren.FirstOrDefault(c => c.Id != IntPtr.Zero && c.Type == "string");
                    if (stringLiteral.Id != IntPtr.Zero && stringLiteral.Text is { } litText)
                    {
                        var reqPath = litText.Trim('\'', '"', '`');
                        if (!string.IsNullOrWhiteSpace(reqPath))
                        {
                            refs.Add(new SymbolReference(
                                currentScope ?? Path.GetFileNameWithoutExtension(filePath),
                                reqPath, "Import", filePath,
                                (int)callNode.StartPosition.Row + 1));
                        }
                    }
                }
            }
            return null;
        }

        if (firstChild.Type == "member_expression")
        {
            var memberChildren = firstChild.NamedChildren.ToList();
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

        if (firstChild.Type is "identifier" or "property_identifier")
        {
            return firstChild.Text;
        }

        return null;
    }

    private static string? ExtractJsNewTarget(Node newExpr)
    {
        foreach (var child in newExpr.NamedChildren)
        {
            if (child.Id != IntPtr.Zero && child.Type is "identifier" or "property_identifier" or "type_identifier")
                return child.Text;
        }
        return null;
    }

    private static void ExtractJsImports(Node importNode, string filePath, List<SymbolReference> refs, string? currentScope)
    {
        foreach (var child in importNode.NamedChildren)
        {
            if (child.Id == IntPtr.Zero) continue;

            if (child.Type == "string" && child.Text is not null)
            {
                var importPath = child.Text.Trim('\'', '"', '`');
                if (!string.IsNullOrWhiteSpace(importPath))
                {
                    refs.Add(new SymbolReference(
                        currentScope ?? Path.GetFileNameWithoutExtension(filePath),
                        importPath, "Import", filePath,
                        (int)importNode.StartPosition.Row + 1));
                }
            }
            else if (child.Type == "import_clause")
            {
                WalkImportClause(child, filePath, refs, currentScope);
            }
        }
    }

    private static void WalkImportClause(Node clauseNode, string filePath, List<SymbolReference> refs, string? currentScope)
    {
        foreach (var child in clauseNode.NamedChildren)
        {
            if (child.Id == IntPtr.Zero) continue;

            if (child.Type is "identifier" or "import_specifier")
            {
                var text = child.Text;
                if (!string.IsNullOrWhiteSpace(text))
                {
                    refs.Add(new SymbolReference(
                        currentScope ?? Path.GetFileNameWithoutExtension(filePath),
                        text, "Import", filePath,
                        (int)child.StartPosition.Row + 1));
                }
            }
            else if (child.Type == "named_imports")
            {
                foreach (var specifier in child.NamedChildren)
                {
                    if (specifier.Id != IntPtr.Zero && specifier.Type == "import_specifier")
                    {
                        var nameNode = GetChildByType(specifier, "identifier");
                        var text = nameNode is { Id: not 0 } n && n.Text is not null ? n.Text : specifier.Text;
                        if (!string.IsNullOrWhiteSpace(text))
                        {
                            refs.Add(new SymbolReference(
                                currentScope ?? Path.GetFileNameWithoutExtension(filePath),
                                text, "Import", filePath,
                                (int)specifier.StartPosition.Row + 1));
                        }
                    }
                }
            }
        }
    }
}
