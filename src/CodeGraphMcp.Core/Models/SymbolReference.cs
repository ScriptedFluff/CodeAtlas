namespace CodeGraphMcp.Models;

/// <summary>
/// A single reference edge: "caller at line N references target by name".
/// Caller and target are resolved to symbol IDs during DB insertion.
/// </summary>
public sealed record SymbolReference(
    string CallerSymbolName,
    string TargetSymbolName,
    string ReferenceType,     // "Call", "TypeUsage", "Inheritance", "Import"
    string FilePath,
    int LineNumber);
