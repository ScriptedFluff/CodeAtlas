namespace CodeGraphMcp.Config;

/// <summary>
/// Pre-processes command line arguments on Windows to fix C runtime quote escaping issues
/// when paths with trailing backslashes inside quotes (e.g. "C:\Path\") escape the quote
/// and collapse trailing option flags into a single argument string.
/// </summary>
public static class CommandLineSanitizer
{
    public static string[] SanitizeArgs(string[] args)
    {
        if (args is null || args.Length == 0) return [];

        var result = new List<string>();

        foreach (var arg in args)
        {
            if (arg.Contains('"'))
            {
                var parts = arg.Split('"')
                    .Select(p => p.Trim())
                    .Where(p => p.Length > 0);

                foreach (var part in parts)
                {
                    if (part.StartsWith('-') && part.Contains(' '))
                    {
                        var flags = part.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                        result.AddRange(flags);
                    }
                    else
                    {
                        result.Add(part);
                    }
                }
            }
            else
            {
                result.Add(arg);
            }
        }

        return result.ToArray();
    }
}
