using System.Globalization;
using Microsoft.Data.Sqlite;

namespace AttendanceApp.Features.Timetable;

public sealed class TimetableRepository
{
    private readonly string _connectionString;

    public TimetableRepository(string databasePath)
    {
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            ForeignKeys = true
        }.ToString();
    }

    public IReadOnlyList<WeeklySessionSummary> GetWeeklySessions(DateOnly effectiveOn)
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT ws.Id, ws.SubjectId, s.Name, ws.DayOfWeek, ws.StartTime, ws.EndTime,
                   ws.EffectiveFrom, ws.EffectiveTo
            FROM WeeklySessions ws
            JOIN Subjects s ON s.Id = ws.SubjectId
            WHERE (s.IsArchived = 0 OR s.ArchivedOn > $effectiveOn)
              AND ws.EffectiveFrom <= $effectiveOn
              AND (ws.EffectiveTo IS NULL OR ws.EffectiveTo > $effectiveOn)
            ORDER BY ws.DayOfWeek, ws.StartTime, s.Name COLLATE NOCASE;
            """;
        command.Parameters.AddWithValue("$effectiveOn", FormatDate(effectiveOn));

        using var reader = command.ExecuteReader();
        var sessions = new List<WeeklySessionSummary>();
        while (reader.Read())
        {
            sessions.Add(new WeeklySessionSummary(
                reader.GetInt64(0), reader.GetInt64(1), reader.GetString(2), reader.GetInt32(3),
                reader.GetString(4), reader.GetString(5), reader.GetString(6),
                reader.IsDBNull(7) ? null : reader.GetString(7)));
        }
        return sessions;
    }

    public void SaveWeeklySession(long subjectId, int dayOfWeek, TimeOnly startTime,
        TimeOnly endTime, DateOnly effectiveFrom)
    {
        if (dayOfWeek is < 0 or > 6)
            throw new ArgumentOutOfRangeException(nameof(dayOfWeek));
        if (endTime <= startTime)
            throw new ArgumentException("The end time must be later than the start time.");

        var effectiveDate = FormatDate(effectiveFrom);
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();
        using var transaction = connection.BeginTransaction();

        long? currentId = null;
        string? currentFrom = null;
        string? currentTo = null;
        using (var current = connection.CreateCommand())
        {
            current.Transaction = transaction;
            current.CommandText = """
                SELECT Id, EffectiveFrom, EffectiveTo
                FROM WeeklySessions
                WHERE SubjectId = $subjectId AND DayOfWeek = $dayOfWeek
                  AND EffectiveFrom <= $date
                  AND (EffectiveTo IS NULL OR EffectiveTo > $date)
                LIMIT 1;
                """;
            current.Parameters.AddWithValue("$subjectId", subjectId);
            current.Parameters.AddWithValue("$dayOfWeek", dayOfWeek);
            current.Parameters.AddWithValue("$date", effectiveDate);
            using var reader = current.ExecuteReader();
            if (reader.Read())
            {
                currentId = reader.GetInt64(0);
                currentFrom = reader.GetString(1);
                currentTo = reader.IsDBNull(2) ? null : reader.GetString(2);
            }
        }

        if (currentId.HasValue && currentFrom == effectiveDate)
        {
            using var update = connection.CreateCommand();
            update.Transaction = transaction;
            update.CommandText = "UPDATE WeeklySessions SET StartTime = $start, EndTime = $end WHERE Id = $id;";
            update.Parameters.AddWithValue("$start", FormatTime(startTime));
            update.Parameters.AddWithValue("$end", FormatTime(endTime));
            update.Parameters.AddWithValue("$id", currentId.Value);
            update.ExecuteNonQuery();
        }
        else
        {
            if (currentId.HasValue)
            {
                using var close = connection.CreateCommand();
                close.Transaction = transaction;
                close.CommandText = "UPDATE WeeklySessions SET EffectiveTo = $date WHERE Id = $id;";
                close.Parameters.AddWithValue("$date", effectiveDate);
                close.Parameters.AddWithValue("$id", currentId.Value);
                close.ExecuteNonQuery();
            }

            var nextEffectiveFrom = currentTo ?? FindNextStart(connection, transaction,
                subjectId, dayOfWeek, effectiveDate);
            using var insert = connection.CreateCommand();
            insert.Transaction = transaction;
            insert.CommandText = """
                INSERT INTO WeeklySessions
                    (SubjectId, DayOfWeek, StartTime, EndTime, EffectiveFrom, EffectiveTo)
                VALUES ($subjectId, $dayOfWeek, $start, $end, $from, $to);
                """;
            insert.Parameters.AddWithValue("$subjectId", subjectId);
            insert.Parameters.AddWithValue("$dayOfWeek", dayOfWeek);
            insert.Parameters.AddWithValue("$start", FormatTime(startTime));
            insert.Parameters.AddWithValue("$end", FormatTime(endTime));
            insert.Parameters.AddWithValue("$from", effectiveDate);
            insert.Parameters.AddWithValue("$to", (object?)nextEffectiveFrom ?? DBNull.Value);
            insert.ExecuteNonQuery();
        }

        transaction.Commit();
    }

    public bool RemoveWeeklySession(long subjectId, int dayOfWeek, DateOnly effectiveFrom)
    {
        var effectiveDate = FormatDate(effectiveFrom);
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();
        using var transaction = connection.BeginTransaction();

        long? currentId = null;
        string? currentStart = null;
        using (var current = connection.CreateCommand())
        {
            current.Transaction = transaction;
            current.CommandText = """
                SELECT Id, EffectiveFrom
                FROM WeeklySessions
                WHERE SubjectId = $subjectId AND DayOfWeek = $dayOfWeek
                  AND EffectiveFrom <= $date
                  AND (EffectiveTo IS NULL OR EffectiveTo > $date)
                LIMIT 1;
                """;
            current.Parameters.AddWithValue("$subjectId", subjectId);
            current.Parameters.AddWithValue("$dayOfWeek", dayOfWeek);
            current.Parameters.AddWithValue("$date", effectiveDate);
            using var reader = current.ExecuteReader();
            if (reader.Read())
            {
                currentId = reader.GetInt64(0);
                currentStart = reader.GetString(1);
            }
        }

        if (!currentId.HasValue)
        {
            transaction.Rollback();
            return false;
        }

        using var change = connection.CreateCommand();
        change.Transaction = transaction;
        if (currentStart == effectiveDate)
        {
            change.CommandText = "DELETE FROM WeeklySessions WHERE Id = $id;";
            change.Parameters.AddWithValue("$id", currentId.Value);
        }
        else
        {
            change.CommandText = "UPDATE WeeklySessions SET EffectiveTo = $date WHERE Id = $id;";
            change.Parameters.AddWithValue("$date", effectiveDate);
            change.Parameters.AddWithValue("$id", currentId.Value);
        }

        var changed = change.ExecuteNonQuery() == 1;
        transaction.Commit();
        return changed;
    }

    private static string? FindNextStart(SqliteConnection connection, SqliteTransaction transaction,
        long subjectId, int dayOfWeek, string effectiveDate)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT MIN(EffectiveFrom)
            FROM WeeklySessions
            WHERE SubjectId = $subjectId AND DayOfWeek = $dayOfWeek
              AND EffectiveFrom > $date;
            """;
        command.Parameters.AddWithValue("$subjectId", subjectId);
        command.Parameters.AddWithValue("$dayOfWeek", dayOfWeek);
        command.Parameters.AddWithValue("$date", effectiveDate);
        return command.ExecuteScalar() as string;
    }

    private static string FormatDate(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    private static string FormatTime(TimeOnly time) => time.ToString("HH:mm", CultureInfo.InvariantCulture);
}


