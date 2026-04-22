using Microsoft.EntityFrameworkCore;
using Microsoft.Data.Sqlite;
using System.Text.Json;

namespace VkPostman.Data;

/// <summary>
/// One-shot, idempotent startup migrations for the SQLite store. We don't use
/// EF Core Migrations proper because the app has always bootstrapped via
/// <c>EnsureCreated</c> — there's no migration history table on existing DBs
/// for EF tooling to latch onto. Each step below is written defensively so it
/// can run on any shape the database might be in (old JSON-schema, new
/// library-schema, or fresh) and leave it in the current-model shape.
/// </summary>
public static class SchemaMigration
{
    /// <summary>
    /// Run all pending migrations. Call after <c>EnsureCreated</c> so new
    /// databases skip everything here and existing ones upgrade cleanly.
    /// </summary>
    public static async Task MigrateAsync(VkPostmanDbContext db)
    {
        await MigrateToSharedPlaceholderLibraryAsync(db);
    }

    /// <summary>
    /// Moves <c>PostTemplate.PlaceholderSchema</c> (a JSON-serialized
    /// <c>List&lt;PlaceholderDefinition&gt;</c> column) into a first-class
    /// <c>PlaceholderDefinitions</c> table, deduplicating by <c>Key</c>
    /// (first-write-wins on type/display conflicts). Drops the old column.
    /// Idempotent: re-runs after the migration is already applied are no-ops.
    /// </summary>
    private static async Task MigrateToSharedPlaceholderLibraryAsync(VkPostmanDbContext db)
    {
        var conn = db.Database.GetDbConnection();
        if (conn.State != System.Data.ConnectionState.Open)
            await conn.OpenAsync();

        var hadOldColumn = await ColumnExistsAsync(conn, "PostTemplates", "PlaceholderSchema");
        if (!hadOldColumn)
            return; // Already on new schema (or brand-new DB).

        // 1. Ensure the library table exists. EnsureCreated has already made
        //    it if this is a model-first run; the IF NOT EXISTS makes us safe
        //    if it somehow hasn't.
        await ExecuteAsync(conn, """
            CREATE TABLE IF NOT EXISTS PlaceholderDefinitions (
                Id           INTEGER NOT NULL CONSTRAINT PK_PlaceholderDefinitions PRIMARY KEY AUTOINCREMENT,
                Key          TEXT    NOT NULL,
                DisplayName  TEXT    NOT NULL,
                Type         INTEGER NOT NULL,
                Description  TEXT    NULL,
                DefaultValue TEXT    NULL,
                CreatedAt    TEXT    NOT NULL,
                UpdatedAt    TEXT    NOT NULL
            );
            """);
        await ExecuteAsync(conn, """
            CREATE UNIQUE INDEX IF NOT EXISTS IX_PlaceholderDefinitions_Key
                ON PlaceholderDefinitions (Key);
            """);

        // 2. Pull existing JSON blobs and upsert each entry into the library.
        var now = DateTime.UtcNow.ToString("O");
        var rows = new List<(string Key, string DisplayName, int Type, string? Description, string? DefaultValue)>();
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT PlaceholderSchema FROM PostTemplates WHERE PlaceholderSchema IS NOT NULL AND PlaceholderSchema <> ''";
            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var json = reader.GetString(0);
                if (string.IsNullOrWhiteSpace(json)) continue;

                List<OldPlaceholder>? entries;
                try { entries = JsonSerializer.Deserialize<List<OldPlaceholder>>(json, JsonOpts); }
                catch { continue; }
                if (entries is null) continue;

                foreach (var entry in entries)
                {
                    if (string.IsNullOrWhiteSpace(entry.Key)) continue;
                    rows.Add((
                        entry.Key,
                        string.IsNullOrWhiteSpace(entry.DisplayName) ? entry.Key : entry.DisplayName,
                        entry.Type,
                        entry.Description,
                        entry.DefaultValue));
                }
            }
        }

        // First-write-wins on duplicate keys — the shared-library rule.
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var row in rows)
        {
            if (!seen.Add(row.Key)) continue;
            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                INSERT OR IGNORE INTO PlaceholderDefinitions
                    (Key, DisplayName, Type, Description, DefaultValue, CreatedAt, UpdatedAt)
                VALUES (@k, @d, @t, @desc, @dv, @now, @now);
                """;
            AddParam(cmd, "@k",   row.Key);
            AddParam(cmd, "@d",   row.DisplayName);
            AddParam(cmd, "@t",   row.Type);
            AddParam(cmd, "@desc",(object?)row.Description ?? DBNull.Value);
            AddParam(cmd, "@dv",  (object?)row.DefaultValue ?? DBNull.Value);
            AddParam(cmd, "@now", now);
            await cmd.ExecuteNonQueryAsync();
        }

        // 3. Drop PlaceholderSchema column. SQLite doesn't support DROP COLUMN
        //    cleanly before 3.35; the classic fallback is "rebuild the table".
        await ExecuteAsync(conn, "PRAGMA foreign_keys = OFF;");
        await ExecuteAsync(conn, """
            CREATE TABLE PostTemplates__new (
                Id               INTEGER NOT NULL CONSTRAINT PK_PostTemplates PRIMARY KEY AUTOINCREMENT,
                Name             TEXT    NOT NULL,
                Description      TEXT    NULL,
                BodyTemplate     TEXT    NOT NULL,
                DefaultThemeTags TEXT    NOT NULL,
                CreatedAt        TEXT    NOT NULL,
                UpdatedAt        TEXT    NOT NULL
            );
            """);
        await ExecuteAsync(conn, """
            INSERT INTO PostTemplates__new
                (Id, Name, Description, BodyTemplate, DefaultThemeTags, CreatedAt, UpdatedAt)
            SELECT Id, Name, Description, BodyTemplate, DefaultThemeTags, CreatedAt, UpdatedAt
            FROM PostTemplates;
            """);
        await ExecuteAsync(conn, "DROP TABLE PostTemplates;");
        await ExecuteAsync(conn, "ALTER TABLE PostTemplates__new RENAME TO PostTemplates;");
        await ExecuteAsync(conn, "PRAGMA foreign_keys = ON;");
    }

    // ---- helpers ------------------------------------------------------------

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private static async Task<bool> ColumnExistsAsync(System.Data.Common.DbConnection conn, string table, string column)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"PRAGMA table_info({table});";
        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            // Row layout: cid, name, type, notnull, dflt_value, pk
            if (string.Equals(reader.GetString(1), column, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    private static async Task ExecuteAsync(System.Data.Common.DbConnection conn, string sql)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        await cmd.ExecuteNonQueryAsync();
    }

    private static void AddParam(SqliteCommand cmd, string name, object value)
    {
        var p = cmd.CreateParameter();
        p.ParameterName = name;
        p.Value = value;
        cmd.Parameters.Add(p);
    }

    private static void AddParam(System.Data.Common.DbCommand cmd, string name, object value)
    {
        var p = cmd.CreateParameter();
        p.ParameterName = name;
        p.Value = value;
        cmd.Parameters.Add(p);
    }

    /// <summary>Matches the old JSON-serialized PlaceholderDefinition shape (had an extra IsRequired bool).</summary>
    private sealed class OldPlaceholder
    {
        public string Key { get; set; } = "";
        public string DisplayName { get; set; } = "";
        public int Type { get; set; }
        public string? Description { get; set; }
        public string? DefaultValue { get; set; }
        public string? ValidationPattern { get; set; }
        public bool IsRequired { get; set; }
    }
}
