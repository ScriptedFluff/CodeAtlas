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
}
