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
    /// </summary>
    public void UpsertFileReferences(
        string filePath,
        IReadOnlyList<SymbolReference> references,
        Func<string, long?> resolveSymbolId)
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
            var callerId = resolveSymbolId(r.CallerSymbolName);
            var targetId = resolveSymbolId(r.TargetSymbolName);
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
}
