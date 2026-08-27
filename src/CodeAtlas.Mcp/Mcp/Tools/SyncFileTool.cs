using System.ComponentModel;
using CodeAtlas.Core.Git;
using CodeAtlas.Core.Indexer;
using ModelContextProtocol.Server;

namespace CodeAtlas.Mcp.Tools;

public sealed record SyncFileResult(
    string FilePath,
    string Operation,
    int SymbolsUpdated,
    int SymbolsAdded,
    int SymbolsDeleted,
    int ReferencesUpdated,
    long ElapsedMs,
    string? Message = null);

[McpServerToolType]
public sealed class SyncFileTool(
    IDynamicWorkspaceService workspaceService,
    IWorkspaceBufferManager bufferManager,
    ISingleFileIndexer singleFileIndexer)
{
    [McpServerTool]
    [Description("""
        Surgically indexes or deletes a single file in the SQLite knowledge graph in milliseconds.
        Atomically updates symbol definitions, line numbers, doc comments, signatures, and caller reference edges.
        Incoming references to existing symbols are preserved across signature/line updates.
        Supports passing in-memory 'content' to sync editor buffers before saving to disk.
        Set delete: true to remove the file and all its associated symbols and caller edges from the index.
        """)]
    public async Task<SyncFileResult> SyncFileAsync(
        [Description("Relative or absolute path of the file to upsert or delete")] string filePath,
        [Description("Optional: in-memory file content string. If omitted, reads directly from disk.")] string? content = null,
        [Description("Optional: if true, surgically purges the file and its symbols/edges from the index instead of parsing")] bool delete = false,
        [Description("Optional: target repository/workspace directory to query (defaults to active workspace)")] string? targetDirectory = null,
        CancellationToken ct = default)
    {
        var context = await workspaceService.GetOrIndexWorkspaceAsync(targetDirectory, ct);
        var db = context.Db;
        var buffer = bufferManager.GetOrCreateBuffer(context.WorkspaceRoot);

        if (GlobFileIndexer.IsGlobPattern(filePath))
        {
            var globIndexer = new GlobFileIndexer(singleFileIndexer);
            var globResult = await globIndexer.SyncGlobAsync(context.WorkspaceRoot, db, filePath, isDelete: delete, ct: ct);
            buffer.Invalidate(filePath);

            return new SyncFileResult(
                FilePath:          globResult.Pattern,
                Operation:         delete ? "delete" : "upsert",
                SymbolsUpdated:    globResult.SymbolsUpdated,
                SymbolsAdded:      globResult.SymbolsAdded,
                SymbolsDeleted:    globResult.SymbolsDeleted,
                ReferencesUpdated: globResult.ReferencesUpdated,
                ElapsedMs:         globResult.ElapsedMs,
                Message:           globResult.Message);
        }

        if (delete)
        {
            var delResult = await singleFileIndexer.DeleteFileAsync(context.WorkspaceRoot, db, filePath, ct);
            buffer.Invalidate(delResult.FilePath);

            return new SyncFileResult(
                FilePath:          delResult.FilePath,
                Operation:         "delete",
                SymbolsUpdated:    0,
                SymbolsAdded:      0,
                SymbolsDeleted:    delResult.SymbolsDeleted,
                ReferencesUpdated: delResult.ReferencesDeleted,
                ElapsedMs:         delResult.ElapsedMs,
                Message:           delResult.Message ?? $"Deleted {delResult.SymbolsDeleted} symbols and {delResult.ReferencesDeleted} references.");
        }
        else
        {
            var upsertResult = await singleFileIndexer.UpsertFileAsync(context.WorkspaceRoot, db, filePath, content, ct);

            // Invalidate/refresh the dirty file buffer so in-memory cache reflects newly indexed state
            buffer.Invalidate(upsertResult.FilePath);

            var op = upsertResult.SymbolsUpdated > 0 || upsertResult.SymbolsAdded > 0 ? "upsert" : "noop";

            return new SyncFileResult(
                FilePath:          upsertResult.FilePath,
                Operation:         op,
                SymbolsUpdated:    upsertResult.SymbolsUpdated,
                SymbolsAdded:      upsertResult.SymbolsAdded,
                SymbolsDeleted:    upsertResult.SymbolsDeleted,
                ReferencesUpdated: upsertResult.ReferencesUpdated,
                ElapsedMs:         upsertResult.ElapsedMs,
                Message:           upsertResult.Message);
        }
    }
}
