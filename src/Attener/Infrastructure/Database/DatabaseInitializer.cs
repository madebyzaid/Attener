using System.IO;
using Microsoft.Data.Sqlite;

namespace Attener.Infrastructure.Database;

public sealed class DatabaseInitializer
{
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
                    UNIQUE (SubjectId, AttendanceDate),
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

        EnsureArchivedOnColumn(connection);
        using var versionCommand = connection.CreateCommand();
        versionCommand.CommandText = "PRAGMA user_version = 2;";
        versionCommand.ExecuteNonQuery();
    }

    private static void EnsureArchivedOnColumn(SqliteConnection connection)
    {
        using var columnsCommand = connection.CreateCommand();
        columnsCommand.CommandText = "PRAGMA table_info(Subjects);";
        using var reader = columnsCommand.ExecuteReader();
        var hasArchivedOn = false;
        while (reader.Read())
        {
            if (reader.GetString(1) == "ArchivedOn")
                hasArchivedOn = true;
        }
        reader.Close();

        if (hasArchivedOn)
            return;

        using var migration = connection.CreateCommand();
        migration.CommandText = "ALTER TABLE Subjects ADD COLUMN ArchivedOn TEXT;";
        migration.ExecuteNonQuery();
    }
}

