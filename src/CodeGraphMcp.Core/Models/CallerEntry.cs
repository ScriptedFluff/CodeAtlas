namespace CodeGraphMcp.Models;

public sealed record CallerEntry(
    string CallerName,
    string FilePath,
    int LineNumber,
    string ReferenceType);
