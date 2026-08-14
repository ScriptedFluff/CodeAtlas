namespace CodeGraphMcp.Models;

public sealed record IndexResult(int FilesProcessed, int SymbolsWritten, long ElapsedMs);
