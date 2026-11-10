using System.Globalization;
using Microsoft.Data.Sqlite;

namespace Attener.Features.Timetable;

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

    // The last day the timetable that is in force on this date will run, or null if it has no end date.
    public DateOnly? GetEndDate(DateOnly effectiveOn)
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();
        var boundary = ReadEndBoundary(connection, null, FormatDate(effectiveOn));
        return boundary is null ? null : ParseDate(boundary).AddDays(-1);
    }

    // Ends every class that runs from effectiveOn onward on lastClassDay (inclusive).
    // If the timetable already had an end date, it is replaced. Classes that start after
    // lastClassDay are left alone. Returns how many weekly sessions were ended.
    public int SetEndDate(DateOnly effectiveOn, DateOnly lastClassDay)
    {
        if (lastClassDay < effectiveOn)
            throw new ArgumentException("The end date cannot be before the date the changes take effect.");

        var date = FormatDate(effectiveOn);
        var endExclusive = FormatDate(lastClassDay.AddDays(1));

        using var connection = new SqliteConnection(_connectionString);
        connection.Open();
        using var transaction = connection.BeginTransaction();

        var oldBoundary = ReadEndBoundary(connection, transaction, date);
        if (oldBoundary is not null)
            ReopenEndedSessions(connection, transaction, oldBoundary);

        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            UPDATE WeeklySessions
            SET EffectiveTo = $to, EndedByEndDate = 1
            WHERE EffectiveFrom < $to
              AND (EffectiveTo IS NULL OR EffectiveTo > $to);
            """;
        command.Parameters.AddWithValue("$to", endExclusive);
        var changed = command.ExecuteNonQuery();

        transaction.Commit();
        return changed;
    }

    // Removes the end date of the timetable in force on this date. Only classes that were ended by an
    // end date are re-opened; classes you removed or replaced in the timetable stay as they are.
    public int ClearEndDate(DateOnly effectiveOn)
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();
        using var transaction = connection.BeginTransaction();

        var boundary = ReadEndBoundary(connection, transaction, FormatDate(effectiveOn));
        var changed = boundary is null ? 0 : ReopenEndedSessions(connection, transaction, boundary);

        transaction.Commit();
        return changed;
    }

    public void SaveWeeklySession(long subjectId, int dayOfWeek, TimeOnly startTime,
        TimeOnly endTime, DateOnly effectiveFrom, DateOnly? lastClassDay = null)
    {
        if (dayOfWeek is < 0 or > 6)
            throw new ArgumentOutOfRangeException(nameof(dayOfWeek));
        if (endTime <= startTime)
            throw new ArgumentException("The end time must be later than the start time.");

        var effectiveDate = FormatDate(effectiveFrom);
        var endBoundary = lastClassDay is null ? null : FormatDate(lastClassDay.Value.AddDays(1));
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();
        using var transaction = connection.BeginTransaction();

        long? currentId = null;
        string? currentFrom = null;
        string? currentTo = null;
        var currentEnded = false;
        using (var current = connection.CreateCommand())
        {
            current.Transaction = transaction;
            // A subject can have several classes on one day, so a class is identified by
            // subject + weekday + start time. (Without the start time, saving a second class of the
            // same subject would overwrite the first one.)
            current.CommandText = """
                SELECT Id, EffectiveFrom, EffectiveTo, EndedByEndDate
                FROM WeeklySessions
                WHERE SubjectId = $subjectId AND DayOfWeek = $dayOfWeek
                  AND StartTime = $start
                  AND EffectiveFrom <= $date
                  AND (EffectiveTo IS NULL OR EffectiveTo > $date)
                LIMIT 1;
                """;
            current.Parameters.AddWithValue("$subjectId", subjectId);
            current.Parameters.AddWithValue("$dayOfWeek", dayOfWeek);
            current.Parameters.AddWithValue("$start", FormatTime(startTime));
            current.Parameters.AddWithValue("$date", effectiveDate);
            using var reader = current.ExecuteReader();
            if (reader.Read())
            {
                currentId = reader.GetInt64(0);
                currentFrom = reader.GetString(1);
                currentTo = reader.IsDBNull(2) ? null : reader.GetString(2);
                currentEnded = reader.GetInt64(3) == 1;
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
                close.CommandText = "UPDATE WeeklySessions SET EffectiveTo = $date, EndedByEndDate = 0 WHERE Id = $id;";
                close.Parameters.AddWithValue("$date", effectiveDate);
                close.Parameters.AddWithValue("$id", currentId.Value);
                close.ExecuteNonQuery();
            }

            var nextEffectiveFrom = currentTo ?? FindNextStart(connection, transaction,
                subjectId, dayOfWeek, effectiveDate);
            var endedByEndDate = currentId.HasValue && currentEnded;

            // Keep the timetable's end date: the new class stops at the end date unless
            // another version of it already starts sooner.
            if (endBoundary is not null
                && string.CompareOrdinal(endBoundary, effectiveDate) > 0
                && (nextEffectiveFrom is null || string.CompareOrdinal(endBoundary, nextEffectiveFrom) <= 0))
            {
                nextEffectiveFrom = endBoundary;
                endedByEndDate = true;
            }

            using var insert = connection.CreateCommand();
            insert.Transaction = transaction;
            insert.CommandText = """
                INSERT INTO WeeklySessions
                    (SubjectId, DayOfWeek, StartTime, EndTime, EffectiveFrom, EffectiveTo, EndedByEndDate)
                VALUES ($subjectId, $dayOfWeek, $start, $end, $from, $to, $ended);
                """;
            insert.Parameters.AddWithValue("$subjectId", subjectId);
            insert.Parameters.AddWithValue("$dayOfWeek", dayOfWeek);
            insert.Parameters.AddWithValue("$start", FormatTime(startTime));
            insert.Parameters.AddWithValue("$end", FormatTime(endTime));
            insert.Parameters.AddWithValue("$from", effectiveDate);
            insert.Parameters.AddWithValue("$to", (object?)nextEffectiveFrom ?? DBNull.Value);
            insert.Parameters.AddWithValue("$ended", endedByEndDate ? 1 : 0);
            insert.ExecuteNonQuery();
        }

        transaction.Commit();
    }

    // Removes ALL of this subject's classes on this weekday from effectiveFrom onward (a subject can
    // have several classes in one day). Classes that start exactly on effectiveFrom are deleted;
    // classes that started earlier are closed so they stop on effectiveFrom.
    // Returns true if anything changed.
    public bool RemoveWeeklySession(long subjectId, int dayOfWeek, DateOnly effectiveFrom)
    {
        var effectiveDate = FormatDate(effectiveFrom);
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();
        using var transaction = connection.BeginTransaction();

        using var delete = connection.CreateCommand();
        delete.Transaction = transaction;
        delete.CommandText = """
            DELETE FROM WeeklySessions
            WHERE SubjectId = $subjectId AND DayOfWeek = $dayOfWeek
              AND EffectiveFrom = $date;
            """;
        delete.Parameters.AddWithValue("$subjectId", subjectId);
        delete.Parameters.AddWithValue("$dayOfWeek", dayOfWeek);
        delete.Parameters.AddWithValue("$date", effectiveDate);
        var deleted = delete.ExecuteNonQuery();

        using var close = connection.CreateCommand();
        close.Transaction = transaction;
        close.CommandText = """
            UPDATE WeeklySessions
            SET EffectiveTo = $date, EndedByEndDate = 0
            WHERE SubjectId = $subjectId AND DayOfWeek = $dayOfWeek
              AND EffectiveFrom < $date
              AND (EffectiveTo IS NULL OR EffectiveTo > $date);
            """;
        close.Parameters.AddWithValue("$subjectId", subjectId);
        close.Parameters.AddWithValue("$dayOfWeek", dayOfWeek);
        close.Parameters.AddWithValue("$date", effectiveDate);
        var closed = close.ExecuteNonQuery();

        transaction.Commit();
        return deleted + closed > 0;
    }

    // How many weekly classes, from effectiveOn onward, do not fit inside startHour..endHour.
    public int CountOutsideHours(DateOnly effectiveOn, int startHour, int endHour)
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();
        return ReadOutsideHours(connection, null, FormatDate(effectiveOn), startHour * 60, endHour * 60).Count;
    }

    // Crops every weekly class that runs on or after effectiveOn so it fits inside startHour..endHour:
    // a class that crosses a boundary is shortened, a class completely outside is removed.
    // Nothing before effectiveOn changes. A class that started earlier is closed on effectiveOn and
    // carries on from that day with its cropped times, exactly like an ordinary timetable edit.
    // Returns how many classes were cropped or removed.
    public int CropToHours(DateOnly effectiveOn, int startHour, int endHour)
    {
        if (startHour < 0 || endHour > 23 || startHour >= endHour)
            throw new ArgumentException("The start hour must be earlier than the end hour, between 0 and 23.");

        var date = FormatDate(effectiveOn);
        var fromMinutes = startHour * 60;
        var toMinutes = endHour * 60;

        using var connection = new SqliteConnection(_connectionString);
        connection.Open();
        using var transaction = connection.BeginTransaction();

        var rows = ReadOutsideHours(connection, transaction, date, fromMinutes, toMinutes);
        foreach (var row in rows)
        {
            var newStart = Math.Max(row.StartMinutes, fromMinutes);
            var newEnd = Math.Min(row.EndMinutes, toMinutes);
            var stillHasHours = newStart < newEnd;

            if (string.CompareOrdinal(row.EffectiveFrom, date) >= 0)
            {
                // This version starts on or after the chosen date, so it can be changed in place.
                using var change = connection.CreateCommand();
                change.Transaction = transaction;
                if (stillHasHours)
                {
                    change.CommandText = "UPDATE WeeklySessions SET StartTime = $start, EndTime = $end WHERE Id = $id;";
                    change.Parameters.AddWithValue("$start", FormatMinutes(newStart));
                    change.Parameters.AddWithValue("$end", FormatMinutes(newEnd));
                }
                else
                {
                    change.CommandText = "DELETE FROM WeeklySessions WHERE Id = $id;";
                }
                change.Parameters.AddWithValue("$id", row.Id);
                change.ExecuteNonQuery();
                continue;
            }

            // This version started before the chosen date: keep its earlier days exactly as they were.
            using (var close = connection.CreateCommand())
            {
                close.Transaction = transaction;
                close.CommandText = "UPDATE WeeklySessions SET EffectiveTo = $date, EndedByEndDate = 0 WHERE Id = $id;";
                close.Parameters.AddWithValue("$date", date);
                close.Parameters.AddWithValue("$id", row.Id);
                close.ExecuteNonQuery();
            }

            if (!stillHasHours) continue;

            using var insert = connection.CreateCommand();
            insert.Transaction = transaction;
            insert.CommandText = """
                INSERT INTO WeeklySessions
                    (SubjectId, DayOfWeek, StartTime, EndTime, EffectiveFrom, EffectiveTo, EndedByEndDate)
                VALUES ($subjectId, $dayOfWeek, $start, $end, $from, $to, $ended);
                """;
            insert.Parameters.AddWithValue("$subjectId", row.SubjectId);
            insert.Parameters.AddWithValue("$dayOfWeek", row.DayOfWeek);
            insert.Parameters.AddWithValue("$start", FormatMinutes(newStart));
            insert.Parameters.AddWithValue("$end", FormatMinutes(newEnd));
            insert.Parameters.AddWithValue("$from", date);
            insert.Parameters.AddWithValue("$to", (object?)row.EffectiveTo ?? DBNull.Value);
            insert.Parameters.AddWithValue("$ended", row.EndedByEndDate);
            insert.ExecuteNonQuery();
        }

        transaction.Commit();
        return rows.Count;
    }

    private sealed record OutsideHoursRow(long Id, long SubjectId, int DayOfWeek, int StartMinutes, int EndMinutes,
        string EffectiveFrom, string? EffectiveTo, int EndedByEndDate);

    // The classes that run on or after this date (the same subjects the timetable shows) and reach
    // outside fromMinutes..toMinutes.
    private static List<OutsideHoursRow> ReadOutsideHours(SqliteConnection connection, SqliteTransaction? transaction,
        string date, int fromMinutes, int toMinutes)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT ws.Id, ws.SubjectId, ws.DayOfWeek, ws.StartTime, ws.EndTime,
                   ws.EffectiveFrom, ws.EffectiveTo, ws.EndedByEndDate
            FROM WeeklySessions ws
            JOIN Subjects s ON s.Id = ws.SubjectId
            WHERE (s.IsArchived = 0 OR s.ArchivedOn > $date)
              AND (ws.EffectiveTo IS NULL OR ws.EffectiveTo > $date);
            """;
        command.Parameters.AddWithValue("$date", date);

        var rows = new List<OutsideHoursRow>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            var start = ParseMinutes(reader.GetString(3));
            var end = ParseMinutes(reader.GetString(4));
            if (start >= fromMinutes && end <= toMinutes) continue;

            rows.Add(new OutsideHoursRow(
                reader.GetInt64(0), reader.GetInt64(1), reader.GetInt32(2), start, end,
                reader.GetString(5), reader.IsDBNull(6) ? null : reader.GetString(6), reader.GetInt32(7)));
        }
        return rows;
    }

    // The earliest "ended by an end date" stop among the classes running on this date (exclusive end,
    // so it is the day AFTER the last class day). Null when the timetable has no end date.
    private static string? ReadEndBoundary(SqliteConnection connection, SqliteTransaction? transaction, string date)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT MIN(ws.EffectiveTo)
            FROM WeeklySessions ws
            JOIN Subjects s ON s.Id = ws.SubjectId
            WHERE ws.EndedByEndDate = 1
              AND ws.EffectiveFrom <= $date
              AND ws.EffectiveTo > $date
              AND (s.IsArchived = 0 OR s.ArchivedOn > $date);
            """;
        command.Parameters.AddWithValue("$date", date);
        return command.ExecuteScalar() as string;
    }

    // Puts sessions that were ended at this boundary back to how they were: they run until the next
    // version of the same class starts, or with no end if there is none.
    private static int ReopenEndedSessions(SqliteConnection connection, SqliteTransaction transaction, string boundary)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            UPDATE WeeklySessions
            SET EndedByEndDate = 0,
                EffectiveTo = (SELECT MIN(n.EffectiveFrom)
                               FROM WeeklySessions n
                               WHERE n.SubjectId = WeeklySessions.SubjectId
                                 AND n.DayOfWeek = WeeklySessions.DayOfWeek
                                 AND n.EffectiveFrom > WeeklySessions.EffectiveFrom)
            WHERE EndedByEndDate = 1 AND EffectiveTo = $boundary;
            """;
        command.Parameters.AddWithValue("$boundary", boundary);
        return command.ExecuteNonQuery();
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
    private static DateOnly ParseDate(string value) => DateOnly.ParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture);
    private static string FormatTime(TimeOnly time) => time.ToString("HH:mm", CultureInfo.InvariantCulture);
    private static string FormatMinutes(int minutes) => FormatTime(new TimeOnly(minutes / 60, minutes % 60));
    private static int ParseMinutes(string value)
    {
        var time = TimeOnly.ParseExact(value, "HH:mm", CultureInfo.InvariantCulture);
        return time.Hour * 60 + time.Minute;
    }
}