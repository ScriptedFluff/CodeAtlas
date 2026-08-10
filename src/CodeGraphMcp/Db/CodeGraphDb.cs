using Microsoft.Data.Sqlite;
using CodeGraphMcp.Db.Queries;

namespace CodeGraphMcp.Db;

public sealed class CodeGraphDb : IDisposable
{
    private readonly SqliteConnection _connection;
    public SymbolQueries Symbols { get; }
    public ReferenceQueries References { get; }
    public IndexStateQueries IndexState { get; }
    public CrossStackQueries CrossStack { get; }

    public CodeGraphDb(string dbPath, bool readOnly = false)
    {
        if (!readOnly)
        {
            var dir = Path.GetDirectoryName(dbPath);
            if (dir is not null) Directory.CreateDirectory(dir);
        }

        var connectionString = readOnly
            ? $"Data Source={dbPath};Mode=ReadOnly;Cache=Shared"
            : $"Data Source={dbPath};Cache=Shared";

        _connection = new SqliteConnection(connectionString);
        _connection.Open();

        if (!readOnly)
        {
            using var wal = _connection.CreateCommand();
            wal.CommandText = "PRAGMA journal_mode=WAL;";
            wal.ExecuteNonQuery();
        }

        Symbols    = new SymbolQueries(_connection);
        References = new ReferenceQueries(_connection);
        IndexState = new IndexStateQueries(_connection);
        CrossStack = new CrossStackQueries(_connection);
    }

    public void Migrate()
    {
        MigrationRunner.Migrate(_connection);
    }

    public void Dispose() => _connection.Dispose();
}
