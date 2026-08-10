using CodeGraphMcp.Models;
using TreeSitter;
using SymbolType = CodeGraphMcp.Models.SymbolType;

namespace CodeGraphMcp.Parser;

public sealed class CSharpParser : ILanguageParser
{
    public IReadOnlyList<string> FileExtensions => [".cs"];
    public string Language => "csharp";

    private static readonly TreeSitter.Language CSharpLanguage = new("tree-sitter-c-sharp", "tree_sitter_c_sharp");

    public IEnumerable<Symbol> Parse(string filePath, string source)
    {
        var symbols = new List<Symbol>();
        try
        {
            using var parser = new TreeSitter.Parser();
            parser.Language = CSharpLanguage;

            using var tree = parser.Parse(source);
            if (tree == null || tree.RootNode.Id == IntPtr.Zero)
                return symbols;

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
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[CSharpParser Error] {ex}");
        }

        return symbols;
    }

    public IEnumerable<SymbolReference> ParseReferences(string filePath, string source)
    {
        var refs = new List<SymbolReference>();
        try
        {
            using var parser = new TreeSitter.Parser();
            parser.Language = CSharpLanguage;

            using var tree = parser.Parse(source);
            if (tree == null || tree.RootNode.Id == IntPtr.Zero)
                return refs;

            WalkForReferences(tree.RootNode, filePath, refs, enclosingSymbol: null);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[CSharpParser Reference Error] {ex}");
        }

        return refs;
    }

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
                    DocComment = ExtractDocComment(node)
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
                    DocComment = ExtractDocComment(node)
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
        try
        {
            var prev = node.PreviousNamedSibling;
            if (prev.Id == IntPtr.Zero || !string.Equals(prev.Type, "comment", StringComparison.Ordinal)) return null;

            var text = prev.Text;
            if (string.IsNullOrWhiteSpace(text)) return null;
            return text.TrimStart('/', ' ');
        }
        catch
        {
            return null;
        }
    }
}
