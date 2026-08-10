using Microsoft.Data.Sqlite;
using CodeGraphMcp.Models;

namespace CodeGraphMcp.Db.Queries;

public sealed class SymbolQueries(SqliteConnection connection)
{
    /// <summary>
    /// Delete all symbols for a given file path, then insert the new set.
    /// This is the Phase 1 upsert strategy — full file replacement.
    /// </summary>
    public void UpsertFile(string filePath, IReadOnlyList<Symbol> symbols)
    {
        using var tx = connection.BeginTransaction();
        try
        {
            // 1. Delete existing symbols for this file
            using var delete = connection.CreateCommand();
            delete.CommandText = "DELETE FROM symbols WHERE file_path = @filePath";
            delete.Parameters.AddWithValue("@filePath", filePath);
            delete.Transaction = tx;
            delete.ExecuteNonQuery();

            // 2. Insert new symbols
            using var insert = connection.CreateCommand();
            insert.CommandText = """
                INSERT INTO symbols
                    (class_name, symbol_type, symbol_name, language, file_path,
                     start_line, end_line, start_byte, end_byte, signature, doc_comment)
                VALUES
                    (@className, @symbolType, @symbolName, @language, @filePath,
                     @startLine, @endLine, @startByte, @endByte, @signature, @docComment)
                """;
            insert.Transaction = tx;

            var pClassName  = insert.Parameters.Add("@className",  SqliteType.Text);
            var pSymbolType = insert.Parameters.Add("@symbolType", SqliteType.Text);
            var pSymbolName = insert.Parameters.Add("@symbolName", SqliteType.Text);
            var pLanguage   = insert.Parameters.Add("@language",   SqliteType.Text);
            var pFilePath   = insert.Parameters.Add("@filePath",   SqliteType.Text);
            var pStartLine  = insert.Parameters.Add("@startLine",  SqliteType.Integer);
            var pEndLine    = insert.Parameters.Add("@endLine",    SqliteType.Integer);
            var pStartByte  = insert.Parameters.Add("@startByte",  SqliteType.Integer);
            var pEndByte    = insert.Parameters.Add("@endByte",    SqliteType.Integer);
            var pSignature  = insert.Parameters.Add("@signature",  SqliteType.Text);
            var pDocComment = insert.Parameters.Add("@docComment", SqliteType.Text);

            foreach (var sym in symbols)
            {
                pClassName.Value  = (object?)sym.ClassName  ?? DBNull.Value;
                pSymbolType.Value = sym.SymbolType.ToString();
                pSymbolName.Value = sym.SymbolName;
                pLanguage.Value   = sym.Language ?? "unknown";
                pFilePath.Value   = filePath;
                pStartLine.Value  = sym.StartLine;
                pEndLine.Value    = sym.EndLine;
                pStartByte.Value  = sym.StartByte;
                pEndByte.Value    = sym.EndByte;
                pSignature.Value  = (object?)sym.Signature  ?? DBNull.Value;
                pDocComment.Value = (object?)sym.DocComment ?? DBNull.Value;
                insert.ExecuteNonQuery();
            }

            tx.Commit();
        }
        catch
        {
            tx.Rollback();
            throw;
        }
    }

    /// <summary>Deletes all symbols associated with a specific file path.</summary>
    public void DeleteByFile(string filePath)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "DELETE FROM symbols WHERE file_path = @filePath;";
        cmd.Parameters.AddWithValue("@filePath", filePath);
        cmd.ExecuteNonQuery();
    }

    /// <summary>Returns symbol count for a single file path.</summary>
    public int CountByFile(string filePath)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM symbols WHERE file_path = @filePath;";
        cmd.Parameters.AddWithValue("@filePath", filePath);
        return (int)(long)(cmd.ExecuteScalar() ?? 0L);
    }

    /// <summary>Upserts symbols for a single changed file in delta mode.</summary>
    public void UpsertFileDelta(string filePath, IReadOnlyList<Symbol> symbols)
    {
        UpsertFile(filePath, symbols);
    }

    /// <summary>Find symbols by name (exact or substring match, case-insensitive).</summary>
    public IReadOnlyList<Symbol> FindByName(string symbolName)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            SELECT id, symbol_name, symbol_type, class_name, language,
                   file_path, start_line, end_line, start_byte, end_byte,
                   signature, doc_comment
            FROM symbols
            WHERE symbol_name = @exact COLLATE NOCASE
               OR symbol_name LIKE @pattern COLLATE NOCASE
            ORDER BY CASE WHEN symbol_name = @exact COLLATE NOCASE THEN 0 ELSE 1 END, file_path, start_line
            LIMIT 50
            """;
        cmd.Parameters.AddWithValue("@exact", symbolName);
        cmd.Parameters.AddWithValue("@pattern", $"%{symbolName}%");

        var results = new List<Symbol>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            results.Add(new Symbol
            {
                Id         = reader.GetInt64(0),
                SymbolName = reader.GetString(1),
                SymbolType = Enum.Parse<SymbolType>(reader.GetString(2)),
                ClassName  = reader.IsDBNull(3) ? null : reader.GetString(3),
                Language   = reader.IsDBNull(4) ? null : reader.GetString(4),
                FilePath   = reader.GetString(5),
                StartLine  = reader.GetInt32(6),
                EndLine    = reader.GetInt32(7),
                StartByte  = reader.GetInt32(8),
                EndByte    = reader.GetInt32(9),
                Signature  = reader.IsDBNull(10) ? null : reader.GetString(10),
                DocComment = reader.IsDBNull(11) ? null : reader.GetString(11),
            });
        }
        return results;
    }

    /// <summary>Returns all symbols of a specific type, optionally filtered by language.</summary>
    public IReadOnlyList<Symbol> FindByType(SymbolType symbolType, string? language = null)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = language is null
            ? """
                SELECT id, symbol_name, symbol_type, class_name, language,
                       file_path, start_line, end_line, start_byte, end_byte,
                       signature, doc_comment
                FROM symbols
                WHERE symbol_type = @type
                ORDER BY file_path, start_line;
                """
            : """
                SELECT id, symbol_name, symbol_type, class_name, language,
                       file_path, start_line, end_line, start_byte, end_byte,
                       signature, doc_comment
                FROM symbols
                WHERE symbol_type = @type AND language = @lang COLLATE NOCASE
                ORDER BY file_path, start_line;
                """;

        cmd.Parameters.AddWithValue("@type", symbolType.ToString());
        if (language is not null) cmd.Parameters.AddWithValue("@lang", language);

        var results = new List<Symbol>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            results.Add(new Symbol
            {
                Id         = reader.GetInt64(0),
                SymbolName = reader.GetString(1),
                SymbolType = Enum.Parse<SymbolType>(reader.GetString(2)),
                ClassName  = reader.IsDBNull(3) ? null : reader.GetString(3),
                Language   = reader.IsDBNull(4) ? null : reader.GetString(4),
                FilePath   = reader.GetString(5),
                StartLine  = reader.GetInt32(6),
                EndLine    = reader.GetInt32(7),
                StartByte  = reader.GetInt32(8),
                EndByte    = reader.GetInt32(9),
                Signature  = reader.IsDBNull(10) ? null : reader.GetString(10),
                DocComment = reader.IsDBNull(11) ? null : reader.GetString(11),
            });
        }
        return results;
    }

    /// <summary>Count symbols per file — useful for sanity checks during development.</summary>
    public IReadOnlyDictionary<string, int> CountPerFile()
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT file_path, COUNT(*) FROM symbols GROUP BY file_path ORDER BY file_path";
        var result = new Dictionary<string, int>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
            result[reader.GetString(0)] = reader.GetInt32(1);
        return result;
    }

    /// <summary>Resolve a symbol name to its ID. Returns null if not found.</summary>
    public long? ResolveId(string symbolName)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            SELECT id FROM symbols
            WHERE symbol_name = @name COLLATE NOCASE
            LIMIT 1;
            """;
        cmd.Parameters.AddWithValue("@name", symbolName);
        var result = cmd.ExecuteScalar();
        return result is DBNull or null ? null : Convert.ToInt64(result);
    }
}
