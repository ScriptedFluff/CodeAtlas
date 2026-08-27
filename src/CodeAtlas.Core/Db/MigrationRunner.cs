using Microsoft.Data.Sqlite;

namespace CodeAtlas.Core.Db;

public static class MigrationRunner
{
    public static void Migrate(SqliteConnection connection)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS schema_migrations (
                version INTEGER PRIMARY KEY,
                applied_at TEXT NOT NULL
            );
            """;
        cmd.ExecuteNonQuery();

        cmd.CommandText = "SELECT MAX(version) FROM schema_migrations;";
        var currentVersionObj = cmd.ExecuteScalar();
        int currentVersion = currentVersionObj is DBNull or null ? 0 : Convert.ToInt32(currentVersionObj);

        if (currentVersion < 1)
        {
            ApplyMigration(connection, 1, M001_InitialSchema);
        }
        if (currentVersion < 2)
        {
            ApplyMigration(connection, 2, M002_Indices);
        }
        if (currentVersion < 3)
        {
            ApplyMigration(connection, 3, M003_CrossStackIndex);
        }
        if (currentVersion < 4)
        {
            ApplyMigration(connection, 4, M004_ReferenceFilePathIndex);
        }
        if (currentVersion < 5)
        {
            ApplyMigration(connection, 5, M005_IsPartialColumn);
        }
        if (currentVersion < 6)
        {
            ApplyMigration(connection, 6, M006_IndexedParsers);
        }
    }


    private static void ApplyMigration(SqliteConnection connection, int version, string sql)
    {
        using var tx = connection.BeginTransaction();
        using var cmd = connection.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();

        cmd.CommandText = "INSERT INTO schema_migrations (version, applied_at) VALUES (@version, @now);";
        cmd.Parameters.AddWithValue("@version", version);
        cmd.Parameters.AddWithValue("@now", DateTime.UtcNow.ToString("o"));
        cmd.ExecuteNonQuery();

        tx.Commit();
    }

    private const string M001_InitialSchema = """
        CREATE TABLE IF NOT EXISTS index_state (
            branch            TEXT NOT NULL,
            last_commit_hash  TEXT PRIMARY KEY,
            indexed_at        TEXT NOT NULL,
            dirty_files_count INTEGER DEFAULT 0
        );

        CREATE TABLE IF NOT EXISTS namespaces (
            id       INTEGER PRIMARY KEY AUTOINCREMENT,
            name     TEXT    UNIQUE NOT NULL,
            language TEXT    NOT NULL
        );

        CREATE TABLE IF NOT EXISTS symbols (
            id           INTEGER PRIMARY KEY AUTOINCREMENT,
            namespace_id INTEGER REFERENCES namespaces(id) ON DELETE SET NULL,
            class_name   TEXT,
            symbol_type  TEXT NOT NULL,
            symbol_name  TEXT NOT NULL,
            language     TEXT NOT NULL DEFAULT 'csharp',
            file_path    TEXT NOT NULL,
            start_line   INTEGER NOT NULL,
            end_line     INTEGER NOT NULL,
            start_byte   INTEGER NOT NULL,
            end_byte     INTEGER NOT NULL,
            signature    TEXT,
            doc_comment  TEXT
        );

        CREATE TABLE IF NOT EXISTS symbol_references (
            id               INTEGER PRIMARY KEY AUTOINCREMENT,
            caller_symbol_id INTEGER REFERENCES symbols(id) ON DELETE CASCADE,
            target_symbol_id INTEGER REFERENCES symbols(id) ON DELETE CASCADE,
            reference_type   TEXT    NOT NULL,
            file_path        TEXT    NOT NULL,
            line_number      INTEGER NOT NULL
        );

        CREATE TABLE IF NOT EXISTS cross_stack_mappings (
            id                     INTEGER PRIMARY KEY AUTOINCREMENT,
            dto_name               TEXT    NOT NULL,
            cs_class_id            INTEGER REFERENCES symbols(id) ON DELETE SET NULL,
            ts_interface_id        INTEGER REFERENCES symbols(id) ON DELETE SET NULL,
            angular_component_path TEXT
        );
        """;

    private const string M002_Indices = """
        CREATE INDEX IF NOT EXISTS idx_symbols_name     ON symbols(symbol_name);
        CREATE INDEX IF NOT EXISTS idx_symbols_file     ON symbols(file_path);
        CREATE INDEX IF NOT EXISTS idx_symbols_type     ON symbols(symbol_type);
        CREATE INDEX IF NOT EXISTS idx_symbols_language ON symbols(language);
        CREATE INDEX IF NOT EXISTS idx_refs_caller      ON symbol_references(caller_symbol_id);
        CREATE INDEX IF NOT EXISTS idx_refs_target      ON symbol_references(target_symbol_id);
        """;

    private const string M003_CrossStackIndex = """
        CREATE UNIQUE INDEX IF NOT EXISTS idx_cross_stack_dto_name ON cross_stack_mappings(dto_name);
        """;

    private const string M004_ReferenceFilePathIndex = """
        CREATE INDEX IF NOT EXISTS idx_refs_file ON symbol_references(file_path);
        """;

    private const string M005_IsPartialColumn = """
        ALTER TABLE symbols ADD COLUMN is_partial INTEGER NOT NULL DEFAULT 0;
        """;

    private const string M006_IndexedParsers = """
        CREATE TABLE IF NOT EXISTS indexed_parsers (
            parser_id   TEXT PRIMARY KEY,
            language    TEXT NOT NULL,
            extensions  TEXT NOT NULL,
            version     TEXT NOT NULL,
            indexed_at  TEXT NOT NULL
        );
        """;
}

