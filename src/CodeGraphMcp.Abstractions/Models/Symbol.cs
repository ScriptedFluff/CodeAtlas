namespace CodeGraphMcp.Models;

public sealed record Symbol
{
    public long Id { get; set; }
    public required string SymbolName { get; init; }
    public required SymbolType SymbolType { get; init; }
    public string? ClassName { get; init; }         // Enclosing class (null for top-level)
    public string? Namespace { get; init; }         // Resolved namespace/module
    public string? Language { get; init; }          // "csharp" | "typescript" | "html" | "powershell"
    public required string FilePath { get; init; }  // Relative to repo root
    public required int StartLine { get; init; }
    public required int EndLine { get; init; }
    public required int StartByte { get; init; }
    public required int EndByte { get; init; }
    public string? Signature { get; init; }         // Return type + parameters
    public string? DocComment { get; init; }        // XML doc summary (C#) or JSDoc (TS)
    public bool IsPartial { get; init; }            // True for C# partial classes/types or TS merged interfaces/namespaces
}
