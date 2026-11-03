using Microsoft.Data.Sqlite;

namespace HeySip.Core.Features.Hydration;

public sealed record IntakeEntry(long Id, DateTimeOffset At, int Milliliters);

/// <summary>The drink log, kept in a local SQLite file.</summary>
public sealed class IntakeStore(string databasePath)
{
    // No connection pool: HeySip touches the database a few times a minute at most, and a pooled connection would keep the
    // file open after every call, which delays deleting or moving the data folder.
    private readonly string _connectionString = new SqliteConnectionStringBuilder { DataSource = databasePath, Pooling = false }.ToString();

    /// <summary>
    /// The layout version stored in the file (<c>PRAGMA user_version</c>). 1 is the layout every earlier copy of HeySip already
    /// has. A later change to the tables adds a step to <see cref="Upgrade"/> and raises this number.
    /// </summary>
    public const int CurrentVersion = 1;

    public void Initialize()
    {
        using var connection = Open();
        using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                CREATE TABLE IF NOT EXISTS intake (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    at_ms INTEGER NOT NULL,
                    milliliters INTEGER NOT NULL
                );
                CREATE INDEX IF NOT EXISTS ix_intake_at ON intake(at_ms);
                """;
            command.ExecuteNonQuery();
        }

        Upgrade(connection);
    }

    /// <summary>The layout version recorded in the file; 0 before this was tracked.</summary>
    public int SchemaVersion()
    {
        using var connection = Open();
        return ReadVersion(connection);
    }

    private static int ReadVersion(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA user_version;";
        return Convert.ToInt32(command.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Brings an older file up to <see cref="CurrentVersion"/>, one step at a time. A file from a newer HeySip is left exactly as it is:
    /// this copy only reads and writes the columns it knows, and never downgrades the number.
    /// </summary>
    private static void Upgrade(SqliteConnection connection)
    {
        var version = ReadVersion(connection);
        if (version >= CurrentVersion)
        {
            return;
        }

        // Version 0 -> 1: no change to the tables, the number is only recorded. Later steps go here, each in its own transaction:
        //   if (version < 2) { ...ALTER TABLE...; version = 2; }
        using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA user_version = {CurrentVersion};";
        command.ExecuteNonQuery();
    }

    public IntakeEntry Add(DateTimeOffset at, int milliliters)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "INSERT INTO intake (at_ms, milliliters) VALUES ($at, $ml); SELECT last_insert_rowid();";
        command.Parameters.AddWithValue("$at", at.ToUnixTimeMilliseconds());
        command.Parameters.AddWithValue("$ml", milliliters);
        var id = (long)command.ExecuteScalar()!;
        return new IntakeEntry(id, at, milliliters);
    }

    public IntakeEntry? Find(long id)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT id, at_ms, milliliters FROM intake WHERE id = $id";
        command.Parameters.AddWithValue("$id", id);
        using var reader = command.ExecuteReader();
        return reader.Read()
            ? new IntakeEntry(reader.GetInt64(0), DateTimeOffset.FromUnixTimeMilliseconds(reader.GetInt64(1)), reader.GetInt32(2))
            : null;
    }

    public bool Delete(long id)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM intake WHERE id = $id";
        command.Parameters.AddWithValue("$id", id);
        return command.ExecuteNonQuery() > 0;
    }

    /// <summary>Entries with <paramref name="from"/> &lt;= time &lt; <paramref name="to"/>, oldest first.</summary>
    public IReadOnlyList<IntakeEntry> Between(DateTimeOffset from, DateTimeOffset to)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT id, at_ms, milliliters FROM intake WHERE at_ms >= $from AND at_ms < $to ORDER BY at_ms, id";
        command.Parameters.AddWithValue("$from", from.ToUnixTimeMilliseconds());
        command.Parameters.AddWithValue("$to", to.ToUnixTimeMilliseconds());
        var entries = new List<IntakeEntry>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            entries.Add(new IntakeEntry(reader.GetInt64(0), DateTimeOffset.FromUnixTimeMilliseconds(reader.GetInt64(1)), reader.GetInt32(2)));
        }

        return entries;
    }

    /// <summary>The newest <paramref name="limit"/> entries before <paramref name="before"/>, newest first.</summary>
    public IReadOnlyList<IntakeEntry> Latest(DateTimeOffset before, int limit)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT id, at_ms, milliliters FROM intake WHERE at_ms < $before ORDER BY at_ms DESC, id DESC LIMIT $limit";
        command.Parameters.AddWithValue("$before", before.ToUnixTimeMilliseconds());
        command.Parameters.AddWithValue("$limit", limit);
        var entries = new List<IntakeEntry>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            entries.Add(new IntakeEntry(reader.GetInt64(0), DateTimeOffset.FromUnixTimeMilliseconds(reader.GetInt64(1)), reader.GetInt32(2)));
        }

        return entries;
    }

    public void Clear()
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM intake";
        command.ExecuteNonQuery();
    }

    private SqliteConnection Open()
    {
        var connection = new SqliteConnection(_connectionString);
        connection.Open();
        return connection;
    }
}
