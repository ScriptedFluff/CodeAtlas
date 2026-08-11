using Microsoft.Data.Sqlite;
using CodeGraphMcp.Models;

namespace CodeGraphMcp.Db.Queries;

/// <summary>
/// Reference queries container for symbol_references graph operations.
/// </summary>
public sealed class ReferenceQueries(SqliteConnection connection)
{
    /// <summary>Returns the total number of symbol references stored in the index.</summary>
    public int Count()
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM symbol_references;";
        var result = cmd.ExecuteScalar();
        return result is DBNull or null ? 0 : Convert.ToInt32(result);
    }

    /// <summary>
    /// Finds all callers of a given symbol by joining symbol_references → symbols.
    /// Returns an empty list if symbol_references is empty.
    /// </summary>
    public IReadOnlyList<CallerEntry> FindCallers(string symbolName)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            SELECT s_caller.symbol_name, s_caller.file_path, sr.line_number, sr.reference_type
            FROM symbol_references sr
            JOIN symbols s_target ON sr.target_symbol_id = s_target.id
            JOIN symbols s_caller ON sr.caller_symbol_id = s_caller.id
            WHERE s_target.symbol_name = @symbolName COLLATE NOCASE
            ORDER BY s_caller.file_path, sr.line_number
            LIMIT 50;
            """;
        cmd.Parameters.AddWithValue("@symbolName", symbolName);

        var results = new List<CallerEntry>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            results.Add(new CallerEntry(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetInt32(2),
                reader.GetString(3)));
        }
        return results;
    }

    /// <summary>Delete all references originating from a given file.</summary>
    public void DeleteByFile(string filePath)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "DELETE FROM symbol_references WHERE file_path = @filePath;";
        cmd.Parameters.AddWithValue("@filePath", filePath);
        cmd.ExecuteNonQuery();
    }

    /// <summary>
    /// Insert references for a file. Resolves caller/target names to symbol IDs
    /// via lookup. Unresolved references are silently skipped.
    /// Supports scoped resolution with file preference and top-level file fallback.
    /// </summary>
    public void UpsertFileReferences(
        string filePath,
        IReadOnlyList<SymbolReference> references,
        Func<string, string?, long?> resolveSymbolId,
        Func<string, long?>? getFileSymbolId = null)
    {
        DeleteByFile(filePath);

        using var tx = connection.BeginTransaction();
        using var insert = connection.CreateCommand();
        insert.CommandText = """
            INSERT INTO symbol_references
                (caller_symbol_id, target_symbol_id, reference_type, file_path, line_number)
            VALUES (@callerId, @targetId, @refType, @filePath, @lineNumber);
            """;
        insert.Transaction = tx;

        var pCallerId   = insert.Parameters.Add("@callerId",   SqliteType.Integer);
        var pTargetId   = insert.Parameters.Add("@targetId",   SqliteType.Integer);
        var pRefType    = insert.Parameters.Add("@refType",    SqliteType.Text);
        var pFilePath   = insert.Parameters.Add("@filePath",   SqliteType.Text);
        var pLineNumber = insert.Parameters.Add("@lineNumber", SqliteType.Integer);

        foreach (var r in references)
        {
            var callerId = resolveSymbolId(r.CallerSymbolName, filePath)
                        ?? getFileSymbolId?.Invoke(filePath);
            var targetId = resolveSymbolId(r.TargetSymbolName, null);

            if (callerId is null || targetId is null) continue;

            pCallerId.Value   = callerId.Value;
            pTargetId.Value   = targetId.Value;
            pRefType.Value    = r.ReferenceType;
            pFilePath.Value   = r.FilePath;
            pLineNumber.Value = r.LineNumber;
            insert.ExecuteNonQuery();
        }
        tx.Commit();
    }

    /// <summary>
    /// Bulk inserts references for multiple files within a single transaction using the SymbolLookupCache.
    /// </summary>
    public void UpsertReferencesBatch(
        IReadOnlyList<(string FilePath, IReadOnlyList<SymbolReference> Refs)> fileRefsBatch,
        SymbolLookupCache cache)
    {
        using var tx = connection.BeginTransaction();

        using var delete = connection.CreateCommand();
        delete.Transaction = tx;
        delete.CommandText = "DELETE FROM symbol_references WHERE file_path = @filePath;";
        var pDelPath = delete.Parameters.Add("@filePath", SqliteType.Text);

        foreach (var (filePath, _) in fileRefsBatch)
        {
            pDelPath.Value = filePath;
            delete.ExecuteNonQuery();
        }

        using var insert = connection.CreateCommand();
        insert.Transaction = tx;
        insert.CommandText = """
            INSERT INTO symbol_references
                (caller_symbol_id, target_symbol_id, reference_type, file_path, line_number)
            VALUES (@callerId, @targetId, @refType, @filePath, @lineNumber);
            """;

        var pCallerId   = insert.Parameters.Add("@callerId",   SqliteType.Integer);
        var pTargetId   = insert.Parameters.Add("@targetId",   SqliteType.Integer);
        var pRefType    = insert.Parameters.Add("@refType",    SqliteType.Text);
        var pFilePath   = insert.Parameters.Add("@filePath",   SqliteType.Text);
        var pLineNumber = insert.Parameters.Add("@lineNumber", SqliteType.Integer);

        foreach (var (filePath, references) in fileRefsBatch)
        {
            foreach (var r in references)
            {
                var callerId = cache.ResolveId(r.CallerSymbolName, filePath)
                            ?? cache.GetPrimarySymbolIdForFile(filePath);
                var targetId = cache.ResolveId(r.TargetSymbolName, null);

                if (callerId is null || targetId is null) continue;

                pCallerId.Value   = callerId.Value;
                pTargetId.Value   = targetId.Value;
                pRefType.Value    = r.ReferenceType;
                pFilePath.Value   = r.FilePath;
                pLineNumber.Value = r.LineNumber;
                insert.ExecuteNonQuery();
            }
        }

        tx.Commit();
    }
}

