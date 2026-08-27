namespace CodeAtlas.Core.Config;

public static class LanguageNormalizer
{
    public static string? Normalize(string? input)
    {
        if (string.IsNullOrWhiteSpace(input)) return null;

        var clean = input.Trim().TrimStart('.').ToLowerInvariant();
        return clean switch
        {
            "ps" or "ps1" or "psm1" or "psd1" or "powershell" or "pwsh" => "powershell",
            "cs" or "csharp" or "c#" => "csharp",
            "ts" or "typescript" => "typescript",
            "js" or "javascript" => "javascript",
            "py" or "python" => "python",
            "html" or "htm" or "cshtml" or "razor" => "html",
            "css" => "css",
            "json" => "json",
            "yml" or "yaml" => "yaml",
            _ => clean
        };
    }
}
