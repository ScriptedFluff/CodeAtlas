using Microsoft.Data.Sqlite;
using CodeGraphMcp.Db.Queries;

namespace CodeGraphMcp.Db;

public sealed class CodeGraphDb : IDisposable
{
    private readonly SqliteConnection _connection;
    public SqliteConnection Connection => _connection;
    public string DbFilePath { get; }
    public SymbolQueries Symbols { get; }
    public ReferenceQueries References { get; }
    public IndexStateQueries IndexState { get; }
    public CrossStackQueries CrossStack { get; }
    public IndexedParserQueries IndexedParsers { get; }


    public CodeGraphDb(string dbPath, bool readOnly = false)
    {
        DbFilePath = dbPath;

        if (!readOnly)
        {
            var dir = Path.GetDirectoryName(dbPath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        }

        var connectionString = readOnly
            ? $"Data Source={dbPath};Mode=ReadOnly;Cache=Shared"
            : $"Data Source={dbPath};Cache=Shared";

        _connection = new SqliteConnection(connectionString);
        _connection.Open();

        using var pragma = _connection.CreateCommand();
        pragma.CommandText = readOnly ? "PRAGMA foreign_keys=ON;" : "PRAGMA journal_mode=WAL; PRAGMA foreign_keys=ON;";
        pragma.ExecuteNonQuery();

        Symbols        = new SymbolQueries(_connection);
        References     = new ReferenceQueries(_connection);
        IndexState     = new IndexStateQueries(_connection);
        CrossStack     = new CrossStackQueries(_connection);
        IndexedParsers = new IndexedParserQueries(_connection);
    }

    public void Migrate()
    {
        MigrationRunner.Migrate(_connection);
    }

    public void Dispose() => _connection.Dispose();
}
