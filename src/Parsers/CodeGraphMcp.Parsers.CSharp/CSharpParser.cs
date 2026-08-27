using CodeGraphMcp.Models;
using TreeSitter;
using SymbolType = CodeGraphMcp.Models.SymbolType;

namespace CodeGraphMcp.Parser;

public sealed class CSharpParser : ILanguageParser
{
    public ParserMetadata Metadata => new("csharp", "C# Parser", "1.0.0", "C# TreeSitter AST parser", "CodeGraphMcp");
    public IReadOnlyList<string> FileExtensions => [".cs"];
    public string Language => "csharp";

    private static readonly TreeSitter.Language CSharpLanguage = new("tree-sitter-c-sharp", "tree_sitter_c_sharp");
    private static readonly ThreadLocal<TreeSitter.Parser> ThreadParser = new(() => new TreeSitter.Parser());

    public FileParseResult ParseFile(string filePath, string source)
    {
        var symbols = new List<Symbol>();
        var refs = new List<SymbolReference>();
        try
        {
            var parser = ThreadParser.Value!;
            parser.Language = CSharpLanguage;

            using var tree = parser.Parse(source);
            if (tree == null || tree.RootNode.Id == IntPtr.Zero)
                return new FileParseResult(symbols, refs);

            string? fileNamespace = null;

            foreach (var node in tree.RootNode.NamedChildren)
            {
                if (node.Type is "file_scoped_namespace_declaration" or "namespace_declaration")
                {
                    var nameNode = GetNameNode(node);
                    if (nameNode.Id != IntPtr.Zero)
                        fileNamespace = nameNode.Text;

                    TraverseNode(node, filePath, symbols, fileNamespace, currentClass: null);
                }
                else
                {
                    TraverseNode(node, filePath, symbols, fileNamespace, currentClass: null);
                }
            }

            WalkForReferences(tree.RootNode, filePath, refs, enclosingSymbol: null);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[CSharpParser Error] {ex}");
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

        if (type is "method_declaration" or "property_declaration" or "constructor_declaration" or "class_declaration" or "struct_declaration" or "interface_declaration")
        {
            var nameNode = GetNameNode(node);
            if (nameNode.Id != IntPtr.Zero && !string.IsNullOrWhiteSpace(nameNode.Text))
                currentScope = nameNode.Text;
        }

        if (type == "invocation_expression")
        {
            var targetName = ExtractInvocationTarget(node);
            if (targetName is not null && !string.IsNullOrWhiteSpace(targetName))
            {
                refs.Add(new SymbolReference(
                    currentScope ?? Path.GetFileNameWithoutExtension(filePath),
                    targetName, "Call", filePath,
                    (int)node.StartPosition.Row + 1));
            }
        }
        else if (type == "object_creation_expression")
        {
            var typeNode = GetFieldByName(node, "type");
            if (typeNode.Id != IntPtr.Zero)
            {
                var typeName = ExtractTypeName(typeNode);
                if (typeName is not null && !string.IsNullOrWhiteSpace(typeName))
                {
                    refs.Add(new SymbolReference(
                        currentScope ?? Path.GetFileNameWithoutExtension(filePath),
                        typeName, "TypeUsage", filePath,
                        (int)node.StartPosition.Row + 1));
                }
            }
        }
        else if (type == "base_list")
        {
            foreach (var child in node.NamedChildren)
            {
                var baseName = ExtractTypeName(child);
                if (baseName is not null && !string.IsNullOrWhiteSpace(baseName))
                {
                    refs.Add(new SymbolReference(
                        currentScope ?? Path.GetFileNameWithoutExtension(filePath),
                        baseName, "Inheritance", filePath,
                        (int)child.StartPosition.Row + 1));
                }
            }
        }

        foreach (var child in node.Children)
            WalkForReferences(child, filePath, refs, currentScope);
    }

    private static string? ExtractInvocationTarget(Node invocation)
    {
        foreach (var child in invocation.NamedChildren)
        {
            if (child.Type == "member_access_expression")
            {
                var nameField = GetFieldByName(child, "name");
                return nameField.Id != IntPtr.Zero ? nameField.Text : null;
            }
            if (child.Type == "identifier")
            {
                return child.Text;
            }
        }
        return null;
    }

    private static string? ExtractTypeName(Node typeNode)
    {
        if (typeNode.Type == "identifier") return typeNode.Text;
        if (typeNode.Type is "qualified_name" or "generic_name")
        {
            var nameNode = GetNameNode(typeNode);
            return nameNode.Id != IntPtr.Zero ? nameNode.Text : typeNode.Text;
        }
        foreach (var child in typeNode.NamedChildren)
        {
            if (child.Type == "identifier") return child.Text;
        }
        return null;
    }

    private static Node GetFieldByName(Node node, string fieldName)
    {
        try
        {
            foreach (var field in node.Fields)
            {
                if (field.Key != null && string.Equals(field.Key, fieldName, StringComparison.Ordinal) && field.Value.Id != IntPtr.Zero)
                    return field.Value;
            }
        }
        catch { }
        return default;
    }

    private static void TraverseNode(Node node, string filePath, List<Symbol> symbols, string? currentNamespace, string? currentClass)
    {
        if (node.Id == IntPtr.Zero) return;

        var type = node.Type;

        if (type is "class_declaration" or "interface_declaration" or "struct_declaration" or "enum_declaration")
        {
            var nameNode = GetNameNode(node);
            if (nameNode.Id != IntPtr.Zero)
            {
                var nameText = nameNode.Text;
                var symbolType = type switch
                {
                    "class_declaration"     => SymbolType.Class,
                    "interface_declaration" => SymbolType.Interface,
                    "struct_declaration"    => SymbolType.Struct,
                    "enum_declaration"      => SymbolType.Enum,
                    _                       => SymbolType.Class
                };

                currentClass = nameText;
                bool isPartial = HasPartialModifier(node);

                symbols.Add(new Symbol
                {
                    SymbolName = nameText,
                    SymbolType = symbolType,
                    ClassName  = null,
                    Namespace  = currentNamespace,
                    Language   = "csharp",
                    FilePath   = filePath,
                    StartLine  = (int)node.StartPosition.Row + 1,
                    EndLine    = (int)node.EndPosition.Row + 1,
                    StartByte  = (int)node.StartIndex,
                    EndByte    = (int)node.EndIndex,
                    Signature  = null,
                    DocComment = ExtractDocComment(node),
                    IsPartial  = isPartial
                });
            }
        }
        else if (type is "method_declaration" or "property_declaration")
        {
            var nameNode = GetNameNode(node);
            if (nameNode.Id != IntPtr.Zero)
            {
                var nameText = nameNode.Text;
                var symbolType = type == "method_declaration" ? SymbolType.Method : SymbolType.Property;

                symbols.Add(new Symbol
                {
                    SymbolName = nameText,
                    SymbolType = symbolType,
                    ClassName  = currentClass,
                    Namespace  = currentNamespace,
                    Language   = "csharp",
                    FilePath   = filePath,
                    StartLine  = (int)node.StartPosition.Row + 1,
                    EndLine    = (int)node.EndPosition.Row + 1,
                    StartByte  = (int)node.StartIndex,
                    EndByte    = (int)node.EndIndex,
                    Signature  = ExtractSignature(node, symbolType),
                    DocComment = ExtractDocComment(node),
                    IsPartial  = HasPartialModifier(node)
                });
            }
        }

        foreach (var child in node.Children)
        {
            TraverseNode(child, filePath, symbols, currentNamespace, currentClass);
        }
    }

    private static Node GetNameNode(Node node)
    {
        try
        {
            foreach (var field in node.Fields)
            {
                if (field.Key != null && string.Equals(field.Key, "name", StringComparison.Ordinal) && field.Value.Id != IntPtr.Zero)
                    return field.Value;
            }

            foreach (var child in node.NamedChildren)
            {
                if (child.Type is "identifier" or "qualified_name" && child.Id != IntPtr.Zero)
                    return child;
            }
        }
        catch { }

        return default;
    }

    private static string? ExtractSignature(Node node, SymbolType type)
    {
        if (type is not SymbolType.Method) return null;

        try
        {
            Node? returnNode = null;
            Node? paramNode = null;

            foreach (var field in node.Fields)
            {
                if (field.Key == null) continue;

                if ((string.Equals(field.Key, "returns", StringComparison.Ordinal) || string.Equals(field.Key, "type", StringComparison.Ordinal)) && field.Value.Id != IntPtr.Zero)
                    returnNode = field.Value;
                else if ((string.Equals(field.Key, "parameters", StringComparison.Ordinal) || string.Equals(field.Key, "parameter_list", StringComparison.Ordinal)) && field.Value.Id != IntPtr.Zero)
                    paramNode = field.Value;
            }

            string? returnType = returnNode?.Id != IntPtr.Zero ? returnNode?.Text : null;
            string? parameters = paramNode?.Id != IntPtr.Zero ? paramNode?.Text : null;

            if (returnType == null && parameters == null) return null;
            return returnType == null ? parameters : $"{returnType} {parameters}".Trim();
        }
        catch
        {
            return null;
        }
    }

    private static string? ExtractDocComment(Node node)
    {
        if (node.Id == IntPtr.Zero) return null;

        try
        {
            var comments = new List<string>();
            var prev = node.PreviousSibling;

            while (prev is { Id: not 0 } p && p.Type is "comment" or "single_line_comment" or "multiline_comment")
            {
                var text = p.Text;
                if (!string.IsNullOrWhiteSpace(text))
                {
                    comments.Insert(0, text.TrimStart('/', '*', ' '));
                }
                prev = p.PreviousSibling;
            }

            if (comments.Count == 0)
            {
                var prevNamed = node.PreviousNamedSibling;
                if (prevNamed is { Id: not 0 } pn && pn.Type is "comment" or "single_line_comment" or "multiline_comment")
                {
                    var text = pn.Text;
                    if (!string.IsNullOrWhiteSpace(text))
                        comments.Add(text.TrimStart('/', '*', ' '));
                }
            }

            return comments.Count > 0 ? string.Join("\n", comments) : null;
        }
        catch
        {
            return null;
        }
    }

    private static bool HasPartialModifier(Node node)
    {
        try
        {
            foreach (var child in node.Children)
            {
                if (child.Type is "modifier" or "modifier_list" && child.Text.Contains("partial", StringComparison.OrdinalIgnoreCase))
                    return true;
                if (child.Type == "partial" || string.Equals(child.Text, "partial", StringComparison.OrdinalIgnoreCase))
                    return true;
            }
        }
        catch { }
        return false;
    }
}
