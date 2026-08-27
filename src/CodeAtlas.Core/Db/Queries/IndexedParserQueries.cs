using Microsoft.Data.Sqlite;
using CodeAtlas.Parser;

namespace CodeAtlas.Core.Db.Queries;

public sealed record IndexedParserRecord(
    string ParserId,
    string Language,
    string Extensions,
    string Version,
    string IndexedAt
);

public sealed class IndexedParserQueries(SqliteConnection connection)
{
    public IReadOnlyList<IndexedParserRecord> GetIndexedParsers()
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT parser_id, language, extensions, version, indexed_at FROM indexed_parsers;";
        using var reader = cmd.ExecuteReader();
        var list = new List<IndexedParserRecord>();
        while (reader.Read())
        {
            list.Add(new IndexedParserRecord(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetString(4)
            ));
        }
        return list;
    }

    public void RecordIndexedParser(string parserId, string language, IEnumerable<string> extensions, string version)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            INSERT INTO indexed_parsers (parser_id, language, extensions, version, indexed_at)
            VALUES (@parser_id, @language, @extensions, @version, @now)
            ON CONFLICT(parser_id) DO UPDATE SET
                language   = excluded.language,
                extensions = excluded.extensions,
                version    = excluded.version,
                indexed_at = excluded.indexed_at;
            """;
        cmd.Parameters.AddWithValue("@parser_id", parserId);
        cmd.Parameters.AddWithValue("@language", language);
        cmd.Parameters.AddWithValue("@extensions", string.Join(",", extensions));
        cmd.Parameters.AddWithValue("@version", version);
        cmd.Parameters.AddWithValue("@now", DateTimeOffset.UtcNow.ToString("O"));
        cmd.ExecuteNonQuery();
    }

    public void RecordAllIndexedParsers(IEnumerable<ILanguageParser> parsers)
    {
        foreach (var p in parsers)
        {
            RecordIndexedParser(p.Metadata.Id, p.Language, p.FileExtensions, p.Metadata.Version);
        }
    }

    public void RemoveIndexedParser(string parserId)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "DELETE FROM indexed_parsers WHERE parser_id = @parser_id;";
        cmd.Parameters.AddWithValue("@parser_id", parserId);
        cmd.ExecuteNonQuery();
    }
}
