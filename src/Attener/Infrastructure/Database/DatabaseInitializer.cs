using System.IO;
using Microsoft.Data.Sqlite;

namespace Attener.Infrastructure.Database;

public sealed class DatabaseInitializer
{
    private const int SchemaVersion = 4;
    private readonly string _databasePath;

    public DatabaseInitializer(string databasePath)
    {
        _databasePath = databasePath;
    }

    public void Initialize()
    {
        var directory = Path.GetDirectoryName(_databasePath)
            ?? throw new InvalidOperationException("The database path must include a directory.");
        Directory.CreateDirectory(directory);

        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = _databasePath,
            ForeignKeys = true
        }.ToString();

        using var connection = new SqliteConnection(connectionString);
        connection.Open();

        using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                PRAGMA journal_mode = WAL;

                CREATE TABLE IF NOT EXISTS Subjects (
                    Id              INTEGER PRIMARY KEY AUTOINCREMENT,
                    Name            TEXT NOT NULL COLLATE NOCASE UNIQUE,
                    IsArchived      INTEGER NOT NULL DEFAULT 0 CHECK (IsArchived IN (0, 1)),
                    ArchivedOn      TEXT,
                    CreatedAtUtc    TEXT NOT NULL DEFAULT (strftime('%Y-%m-%dT%H:%M:%fZ', 'now'))
                );

                CREATE TABLE IF NOT EXISTS WeeklySessions (
                    Id              INTEGER PRIMARY KEY AUTOINCREMENT,
                    SubjectId       INTEGER NOT NULL REFERENCES Subjects(Id),
                    DayOfWeek       INTEGER NOT NULL CHECK (DayOfWeek BETWEEN 0 AND 6),
                    StartTime       TEXT NOT NULL,
                    EndTime         TEXT NOT NULL,
                    EffectiveFrom   TEXT NOT NULL,
                    EffectiveTo     TEXT,
                    EndedByEndDate  INTEGER NOT NULL DEFAULT 0,
                    CHECK (EndTime > StartTime),
                    CHECK (EffectiveTo IS NULL OR EffectiveTo > EffectiveFrom)
                );

                CREATE INDEX IF NOT EXISTS IX_WeeklySessions_EffectiveDate
                    ON WeeklySessions (DayOfWeek, EffectiveFrom, EffectiveTo);

                CREATE TABLE IF NOT EXISTS AttendanceRecords (
                    Id                      INTEGER PRIMARY KEY AUTOINCREMENT,
                    SubjectId               INTEGER NOT NULL REFERENCES Subjects(Id),
                    AttendanceDate          TEXT NOT NULL,
                    Status                  TEXT NOT NULL CHECK (Status IN (
                                                'Present', 'Absent', 'Cancelled', 'Not Marked', 'No Class'
                                            )),
                    ScheduledStart          TEXT,
                    ScheduledEnd            TEXT,
                    ScheduledDurationMinutes INTEGER NOT NULL DEFAULT 0 CHECK (ScheduledDurationMinutes >= 0),
                    ActualDurationMinutes   INTEGER CHECK (
                                                ActualDurationMinutes IS NULL OR ActualDurationMinutes >= 0
                                            ),
                    UNIQUE (SubjectId, AttendanceDate, ScheduledStart),
                    CHECK (ScheduledEnd IS NULL OR ScheduledStart IS NOT NULL)
                );

                CREATE INDEX IF NOT EXISTS IX_AttendanceRecords_Date
                    ON AttendanceRecords (AttendanceDate);

                CREATE TABLE IF NOT EXISTS AppSettings (
                    Key     TEXT PRIMARY KEY,
                    Value   TEXT NOT NULL
                );
                """;
            command.ExecuteNonQuery();
        }

        // Older databases were created before these columns existed.
        EnsureColumn(connection, "Subjects", "ArchivedOn", "TEXT");
        EnsureColumn(connection, "WeeklySessions", "EndedByEndDate", "INTEGER NOT NULL DEFAULT 0");

        MigrateAttendanceUniqueness(connection);

        using var versionCommand = connection.CreateCommand();
        versionCommand.CommandText = $"PRAGMA user_version = {SchemaVersion};";
        versionCommand.ExecuteNonQuery();
    }

    // Version 4: a subject can now have several classes on the same day, so an attendance record is
    // unique per subject + date + start time (it used to be per subject + date only).
    // SQLite cannot change a UNIQUE rule in place, so the table is rebuilt and every row is copied
    // across unchanged. It all happens in one transaction: either it fully works or nothing changes.
    private static void MigrateAttendanceUniqueness(SqliteConnection connection)
    {
        using var versionCommand = connection.CreateCommand();
        versionCommand.CommandText = "PRAGMA user_version;";
        var version = Convert.ToInt32(versionCommand.ExecuteScalar());
        if (version >= 4)
            return;

        using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            CREATE TABLE AttendanceRecords_New (
                Id                      INTEGER PRIMARY KEY AUTOINCREMENT,
                SubjectId               INTEGER NOT NULL REFERENCES Subjects(Id),
                AttendanceDate          TEXT NOT NULL,
                Status                  TEXT NOT NULL CHECK (Status IN (
                                            'Present', 'Absent', 'Cancelled', 'Not Marked', 'No Class'
                                        )),
                ScheduledStart          TEXT,
                ScheduledEnd            TEXT,
                ScheduledDurationMinutes INTEGER NOT NULL DEFAULT 0 CHECK (ScheduledDurationMinutes >= 0),
                ActualDurationMinutes   INTEGER CHECK (
                                            ActualDurationMinutes IS NULL OR ActualDurationMinutes >= 0
                                        ),
                UNIQUE (SubjectId, AttendanceDate, ScheduledStart),
                CHECK (ScheduledEnd IS NULL OR ScheduledStart IS NOT NULL)
            );

            INSERT INTO AttendanceRecords_New
                (Id, SubjectId, AttendanceDate, Status, ScheduledStart, ScheduledEnd,
                 ScheduledDurationMinutes, ActualDurationMinutes)
            SELECT Id, SubjectId, AttendanceDate, Status, ScheduledStart, ScheduledEnd,
                   ScheduledDurationMinutes, ActualDurationMinutes
            FROM AttendanceRecords;

            DROP TABLE AttendanceRecords;
            ALTER TABLE AttendanceRecords_New RENAME TO AttendanceRecords;

            CREATE INDEX IF NOT EXISTS IX_AttendanceRecords_Date
                ON AttendanceRecords (AttendanceDate);
            """;
        command.ExecuteNonQuery();
        transaction.Commit();
    }

    private static void EnsureColumn(SqliteConnection connection, string table, string column, string definition)
    {
        using var columnsCommand = connection.CreateCommand();
        columnsCommand.CommandText = $"PRAGMA table_info({table});";
        using var reader = columnsCommand.ExecuteReader();
        var exists = false;
        while (reader.Read())
        {
            if (reader.GetString(1) == column)
                exists = true;
        }
        reader.Close();

        if (exists)
            return;

        using var migration = connection.CreateCommand();
        migration.CommandText = $"ALTER TABLE {table} ADD COLUMN {column} {definition};";
        migration.ExecuteNonQuery();
    }
}