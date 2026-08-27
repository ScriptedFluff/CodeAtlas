using Microsoft.Data.Sqlite;
using CodeAtlas.Models;

namespace CodeAtlas.Core.Db.Queries;

public sealed class CrossStackQueries(SqliteConnection connection)
{
    /// <summary>Upserts a cross-stack mapping between a C# DTO and a TypeScript interface.</summary>
    public void UpsertMapping(
        string dtoName,
        long? csClassId,
        long? tsInterfaceId,
        string? angularComponentPath)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            INSERT INTO cross_stack_mappings
                (dto_name, cs_class_id, ts_interface_id, angular_component_path)
            VALUES (@name, @csId, @tsId, @angular)
            ON CONFLICT(dto_name) DO UPDATE SET
                cs_class_id            = excluded.cs_class_id,
                ts_interface_id        = excluded.ts_interface_id,
                angular_component_path = excluded.angular_component_path;
            """;
        cmd.Parameters.AddWithValue("@name",    dtoName);
        cmd.Parameters.AddWithValue("@csId",    (object?)csClassId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@tsId",    (object?)tsInterfaceId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@angular", (object?)angularComponentPath ?? DBNull.Value);
        cmd.ExecuteNonQuery();
    }

    /// <summary>Retrieves a cross-stack mapping by DTO name.</summary>
    public CrossStackMapping? GetMapping(string dtoName)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            SELECT m.dto_name,
                   cs.symbol_name, cs.file_path, cs.start_line, cs.end_line,
                   ts.symbol_name, ts.file_path, ts.start_line, ts.end_line,
                   m.angular_component_path
            FROM cross_stack_mappings m
            LEFT JOIN symbols cs ON m.cs_class_id    = cs.id
            LEFT JOIN symbols ts ON m.ts_interface_id = ts.id
            WHERE m.dto_name = @name COLLATE NOCASE;
            """;
        cmd.Parameters.AddWithValue("@name", dtoName);

        using var r = cmd.ExecuteReader();
        if (!r.Read()) return null;

        return new CrossStackMapping(
            DtoName:              r.GetString(0),
            CsSymbolName:         r.IsDBNull(1) ? null : r.GetString(1),
            CsFilePath:           r.IsDBNull(2) ? null : r.GetString(2),
            CsStartLine:          r.IsDBNull(3) ? null : r.GetInt32(3),
            CsEndLine:            r.IsDBNull(4) ? null : r.GetInt32(4),
            TsSymbolName:         r.IsDBNull(5) ? null : r.GetString(5),
            TsFilePath:           r.IsDBNull(6) ? null : r.GetString(6),
            TsStartLine:          r.IsDBNull(7) ? null : r.GetInt32(7),
            TsEndLine:            r.IsDBNull(8) ? null : r.GetInt32(8),
            AngularComponentPath: r.IsDBNull(9) ? null : r.GetString(9));
    }

    /// <summary>Returns DTO names that have been indexed but not yet linked to a TS interface.</summary>
    public IReadOnlyList<string> GetUnlinkedDtoNames()
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT dto_name FROM cross_stack_mappings WHERE ts_interface_id IS NULL LIMIT 50;";
        var results = new List<string>();
        using var r = cmd.ExecuteReader();
        while (r.Read()) results.Add(r.GetString(0));
        return results;
    }
}
