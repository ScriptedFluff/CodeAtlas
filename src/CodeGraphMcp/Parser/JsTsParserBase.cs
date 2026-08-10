using CodeGraphMcp.Models;
using TreeSitter;

namespace CodeGraphMcp.Parser;

public abstract class JsTsParserBase
{
    protected static Node? GetChildByType(Node parent, params string[] types)
    {
        if (parent.Id == IntPtr.Zero) return null;
        foreach (var child in parent.NamedChildren)
        {
            if (child.Id != IntPtr.Zero && child.Type is not null && types.Contains(child.Type))
                return child;
        }
        return null;
    }

    protected static Node? GetValueChild(Node declaratorNode)
    {
        if (declaratorNode.Id == IntPtr.Zero) return null;
        bool pastEquals = false;
        foreach (var child in declaratorNode.Children)
        {
            if (child.Id != IntPtr.Zero && string.Equals(child.Type, "=", StringComparison.Ordinal))
            {
                pastEquals = true;
                continue;
            }
            if (pastEquals && child.IsNamed && child.Id != IntPtr.Zero)
            {
                return child;
            }
        }
        return null;
    }

    protected static string? ExtractDocComment(Node node)
    {
        if (node.Id == IntPtr.Zero) return null;
        try
        {
            var prev = node.PreviousNamedSibling;
            if (prev.Id == IntPtr.Zero || prev.Type is null || !string.Equals(prev.Type, "comment", StringComparison.Ordinal))
                return null;
            var text = prev.Text;
            if (string.IsNullOrWhiteSpace(text)) return null;
            return text.TrimStart('/', '*', ' ');
        }
        catch
        {
            return null;
        }
    }
}
