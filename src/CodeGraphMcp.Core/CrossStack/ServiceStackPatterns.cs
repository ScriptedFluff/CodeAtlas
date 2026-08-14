namespace CodeGraphMcp.CrossStack;

public static class ServiceStackPatterns
{
    public static readonly string[] DtoNamespacePatterns =
    [
        "%.Requests%",
        "%.Responses%",
        "%.Dtos%",
        "%.ServiceModel%",
        "%.Messages%"
    ];

    public static readonly string[] DtoPathHeuristics =
    [
        "Requests",
        "Responses",
        "Dtos",
        "ServiceModel",
        "Messages"
    ];

    /// <summary>Derives candidate TypeScript interface names from a C# DTO class name.</summary>
    public static IEnumerable<string> GetCandidateTsNames(string csharpClassName) =>
    [
        csharpClassName,
        $"I{csharpClassName}"
    ];

    /// <summary>True if a namespace or path matches DTO patterns.</summary>
    public static bool IsDtoCandidate(string? namespaceName, string filePath)
    {
        if (namespaceName is not null && DtoNamespacePatterns.Any(p => SqlLikeMatch(namespaceName, p)))
            return true;

        var normalizedPath = filePath.Replace('\\', '/');
        var parts = normalizedPath.Split('/');
        return parts.Any(p => DtoPathHeuristics.Any(h => string.Equals(p, h, StringComparison.OrdinalIgnoreCase)));
    }

    private static bool SqlLikeMatch(string input, string pattern)
    {
        var parts = pattern.Split('%');
        var pos = 0;
        for (var i = 0; i < parts.Length; i++)
        {
            if (parts[i].Length == 0) continue;
            var idx = input.IndexOf(parts[i], pos, StringComparison.OrdinalIgnoreCase);
            if (idx < 0) return false;
            pos = idx + parts[i].Length;
        }
        return true;
    }
}
