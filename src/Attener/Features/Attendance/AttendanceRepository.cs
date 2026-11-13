using System.Globalization;
using Attener.Domain;
using Attener.Features.Subjects;
using Microsoft.Data.Sqlite;

namespace Attener.Features.Attendance;

public sealed record AttendanceEntry(
    long SubjectId,
    string SubjectName,
    SubjectType Type,
    DateOnly AttendanceDate,
    string? ScheduledStart,
    string? ScheduledEnd,
    int ScheduledDurationMinutes,
    AttendanceStatus? Status,
    int? ActualDurationMinutes,
    bool IsSaved,
    long Id = 0,
    bool IsManual = false,
    string Venue = "",
    string Professor = "")
{
    public bool HasProfessor => !string.IsNullOrWhiteSpace(Professor);
    public bool HasVenue => !string.IsNullOrWhiteSpace(Venue);
}

public sealed class AttendanceRepository
{
    private readonly string _connectionString;

    public AttendanceRepository(string databasePath)
    {
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            ForeignKeys = true
        }.ToString();
    }

    public int GenerateMissingBetween(DateOnly startDate, DateOnly endDate)
    {
        if (startDate > endDate || startDate > DateOnly.FromDateTime(DateTime.Today))
            return 0;

        var end = endDate > DateOnly.FromDateTime(DateTime.Today)
            ? DateOnly.FromDateTime(DateTime.Today)
            : endDate;

        using var connection = new SqliteConnection(_connectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            WITH RECURSIVE dates(AttendanceDate) AS (
                SELECT $startDate WHERE $startDate <= $endDate
                UNION ALL
                SELECT date(AttendanceDate, '+1 day')
                FROM dates WHERE AttendanceDate < $endDate
            )
            INSERT OR IGNORE INTO AttendanceRecords (
                SubjectId, ClassType, AttendanceDate, Status, ScheduledStart, ScheduledEnd,
                ScheduledDurationMinutes, ActualDurationMinutes
            )
            SELECT ws.SubjectId, ws.ClassType, dates.AttendanceDate, 'Not Marked', ws.StartTime, ws.EndTime,
                   (CAST(substr(ws.EndTime, 1, 2) AS INTEGER) * 60 + CAST(substr(ws.EndTime, 4, 2) AS INTEGER))
                   - (CAST(substr(ws.StartTime, 1, 2) AS INTEGER) * 60 + CAST(substr(ws.StartTime, 4, 2) AS INTEGER)),
                   NULL
            FROM dates
            JOIN WeeklySessions ws
              ON ws.DayOfWeek = CAST(strftime('%w', dates.AttendanceDate) AS INTEGER)
             AND ws.EffectiveFrom <= dates.AttendanceDate
             AND (ws.EffectiveTo IS NULL OR ws.EffectiveTo > dates.AttendanceDate)
            JOIN Subjects s ON s.Id = ws.SubjectId
            WHERE s.IsArchived = 0 OR (s.ArchivedOn IS NOT NULL AND s.ArchivedOn > dates.AttendanceDate);
            """;
        command.Parameters.AddWithValue("$startDate", FormatDate(startDate));
        command.Parameters.AddWithValue("$endDate", FormatDate(end));
        return command.ExecuteNonQuery();
    }

    public IReadOnlyList<AttendanceEntry> GetAttendanceForDate(DateOnly date, bool allowUnmarked)
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();
        using var command = connection.CreateCommand();

        if (allowUnmarked)
        {
            command.CommandText = """
                SELECT a.SubjectId, s.Name, a.ClassType, a.AttendanceDate, a.ScheduledStart, a.ScheduledEnd,
                       a.ScheduledDurationMinutes, a.Status, a.ActualDurationMinutes, 1, a.Id, a.IsManual,
                       ct.Venue, ct.Professor
                FROM AttendanceRecords a
                JOIN Subjects s ON s.Id = a.SubjectId
                JOIN SubjectClassTypes ct ON ct.SubjectId = a.SubjectId AND ct.Type = a.ClassType
                WHERE a.AttendanceDate = $date
                UNION ALL
                SELECT ws.SubjectId, s.Name, ws.ClassType, $date, ws.StartTime, ws.EndTime,
                       (CAST(substr(ws.EndTime, 1, 2) AS INTEGER) * 60 + CAST(substr(ws.EndTime, 4, 2) AS INTEGER))
                       - (CAST(substr(ws.StartTime, 1, 2) AS INTEGER) * 60 + CAST(substr(ws.StartTime, 4, 2) AS INTEGER)),
                       NULL, NULL, 0, 0, 0, ct.Venue, ct.Professor
                FROM WeeklySessions ws
                JOIN Subjects s ON s.Id = ws.SubjectId
                JOIN SubjectClassTypes ct ON ct.SubjectId = ws.SubjectId AND ct.Type = ws.ClassType
                WHERE ws.DayOfWeek = $dayOfWeek
                  AND ws.EffectiveFrom <= $date
                  AND (ws.EffectiveTo IS NULL OR ws.EffectiveTo > $date)
                  AND (s.IsArchived = 0 OR (s.ArchivedOn IS NOT NULL AND s.ArchivedOn > $date))
                  AND NOT EXISTS (
                      SELECT 1 FROM AttendanceRecords a
                      WHERE a.SubjectId = ws.SubjectId AND a.AttendanceDate = $date
                        AND a.ClassType = ws.ClassType AND a.ScheduledStart = ws.StartTime AND a.IsManual = 0
                  )
                ORDER BY 5, 2, 3;
                """;
            command.Parameters.AddWithValue("$dayOfWeek", (int)date.DayOfWeek);
        }
        else
        {
            command.CommandText = """
                SELECT ws.SubjectId, s.Name, ws.ClassType, $date, ws.StartTime, ws.EndTime,
                       (CAST(substr(ws.EndTime, 1, 2) AS INTEGER) * 60 + CAST(substr(ws.EndTime, 4, 2) AS INTEGER))
                       - (CAST(substr(ws.StartTime, 1, 2) AS INTEGER) * 60 + CAST(substr(ws.StartTime, 4, 2) AS INTEGER)),
                       NULL, NULL, 0, 0, 0, ct.Venue, ct.Professor
                FROM WeeklySessions ws
                JOIN Subjects s ON s.Id = ws.SubjectId
                JOIN SubjectClassTypes ct ON ct.SubjectId = ws.SubjectId AND ct.Type = ws.ClassType
                WHERE ws.DayOfWeek = $dayOfWeek
                  AND ws.EffectiveFrom <= $date
                  AND (ws.EffectiveTo IS NULL OR ws.EffectiveTo > $date)
                  AND (s.IsArchived = 0 OR (s.ArchivedOn IS NOT NULL AND s.ArchivedOn > $date))
                ORDER BY ws.StartTime, s.Name COLLATE NOCASE, ws.ClassType;
                """;
            command.Parameters.AddWithValue("$dayOfWeek", (int)date.DayOfWeek);
        }
        command.Parameters.AddWithValue("$date", FormatDate(date));

        using var reader = command.ExecuteReader();
        var entries = new List<AttendanceEntry>();
        while (reader.Read())
        {
            entries.Add(new AttendanceEntry(
                reader.GetInt64(0), reader.GetString(1), Enum.Parse<SubjectType>(reader.GetString(2)), date,
                reader.IsDBNull(4) ? null : reader.GetString(4),
                reader.IsDBNull(5) ? null : reader.GetString(5),
                reader.GetInt32(6),
                reader.IsDBNull(7) ? null : ParseStatus(reader.GetString(7)),
                reader.IsDBNull(8) ? null : reader.GetInt32(8),
                reader.GetInt64(9) == 1,
                reader.GetInt64(10), reader.GetInt64(11) == 1,
                reader.GetString(12), reader.GetString(13)));
        }
        return entries;
    }


    public IReadOnlyList<AttendanceEntry> GetPendingAttendance(DateOnly throughDate)
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT a.SubjectId, s.Name, a.ClassType, a.AttendanceDate, a.ScheduledStart, a.ScheduledEnd,
                   a.ScheduledDurationMinutes, a.Status, a.ActualDurationMinutes, 1, a.Id, a.IsManual,
                   ct.Venue, ct.Professor
            FROM AttendanceRecords a
            JOIN Subjects s ON s.Id = a.SubjectId
            JOIN SubjectClassTypes ct ON ct.SubjectId = a.SubjectId AND ct.Type = a.ClassType
            WHERE a.Status = 'Not Marked' AND a.AttendanceDate <= $throughDate
            ORDER BY a.AttendanceDate DESC, a.ScheduledStart, s.Name COLLATE NOCASE;
            """;
        command.Parameters.AddWithValue("$throughDate", FormatDate(throughDate));

        using var reader = command.ExecuteReader();
        var entries = new List<AttendanceEntry>();
        while (reader.Read())
        {
            entries.Add(new AttendanceEntry(
                reader.GetInt64(0), reader.GetString(1), Enum.Parse<SubjectType>(reader.GetString(2)),
                DateOnly.ParseExact(reader.GetString(3), "yyyy-MM-dd", CultureInfo.InvariantCulture),
                reader.IsDBNull(4) ? null : reader.GetString(4),
                reader.IsDBNull(5) ? null : reader.GetString(5),
                reader.GetInt32(6), ParseStatus(reader.GetString(7)),
                reader.IsDBNull(8) ? null : reader.GetInt32(8), true,
                reader.GetInt64(10), reader.GetInt64(11) == 1,
                reader.GetString(12), reader.GetString(13)));
        }
        return entries;
    }
    public void SaveAttendance(AttendanceEntry entry, AttendanceStatus status)
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();
        EnsureEnabledType(connection, entry.SubjectId, entry.Type);
        using var command = connection.CreateCommand();

        // A hand-entered record has no start time, so the "same class" rule below cannot find it.
        // It is changed by its own id instead.
        if (entry.IsManual)
        {
            if (status == AttendanceStatus.NotMarked)
                command.CommandText = "DELETE FROM AttendanceRecords WHERE Id = $id AND IsManual = 1;";
            else
            {
                command.CommandText = "UPDATE AttendanceRecords SET Status = $status WHERE Id = $id AND IsManual = 1;";
                command.Parameters.AddWithValue("$status", FormatStatus(status));
            }
            command.Parameters.AddWithValue("$id", entry.Id);
            command.ExecuteNonQuery();
            return;
        }

        command.CommandText = """
            INSERT INTO AttendanceRecords (
                SubjectId, ClassType, AttendanceDate, Status, ScheduledStart, ScheduledEnd,
                ScheduledDurationMinutes, ActualDurationMinutes
            )
            VALUES ($subjectId, $classType, $date, $status, $start, $end, $scheduledMinutes, $actualMinutes)
            ON CONFLICT (SubjectId, ClassType, AttendanceDate, ScheduledStart) DO UPDATE SET
                Status = excluded.Status,
                ScheduledStart = excluded.ScheduledStart,
                ScheduledEnd = excluded.ScheduledEnd,
                ScheduledDurationMinutes = excluded.ScheduledDurationMinutes,
                ActualDurationMinutes = excluded.ActualDurationMinutes;
            """;
        command.Parameters.AddWithValue("$subjectId", entry.SubjectId);
        command.Parameters.AddWithValue("$classType", entry.Type.ToString());
        command.Parameters.AddWithValue("$date", FormatDate(entry.AttendanceDate));
        command.Parameters.AddWithValue("$status", FormatStatus(status));
        command.Parameters.AddWithValue("$start", (object?)entry.ScheduledStart ?? DBNull.Value);
        command.Parameters.AddWithValue("$end", (object?)entry.ScheduledEnd ?? DBNull.Value);
        command.Parameters.AddWithValue("$scheduledMinutes", entry.ScheduledDurationMinutes);
        // Actual duration is no longer edited anywhere: a saved record always counts its full scheduled time.
        command.Parameters.AddWithValue("$actualMinutes", DBNull.Value);
        command.ExecuteNonQuery();
    }

    // Adds attendance the person typed in by hand: any date (past, today or future) for an existing
    // subject, with the number of minutes they chose. It has no start or end time and is not linked to
    // the timetable. The same subject can have several of these on one day.
    public void AddManualEntry(long subjectId, SubjectType type, DateOnly date,
        AttendanceStatus status, int durationMinutes)
    {
        if (durationMinutes <= 0)
            throw new ArgumentException("The duration must be more than zero.");
        if (status == AttendanceStatus.NotMarked || status == AttendanceStatus.NoClass)
            throw new ArgumentException("Choose Present, Absent or Cancelled.");

        using var connection = new SqliteConnection(_connectionString);
        connection.Open();
        EnsureEnabledType(connection, subjectId, type);
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO AttendanceRecords (
                SubjectId, ClassType, AttendanceDate, Status, ScheduledStart, ScheduledEnd,
                ScheduledDurationMinutes, ActualDurationMinutes, IsManual
            )
            VALUES ($subjectId, $classType, $date, $status, NULL, NULL, $minutes, NULL, 1);
            """;
        command.Parameters.AddWithValue("$subjectId", subjectId);
        command.Parameters.AddWithValue("$classType", type.ToString());
        command.Parameters.AddWithValue("$date", FormatDate(date));
        command.Parameters.AddWithValue("$status", FormatStatus(status));
        command.Parameters.AddWithValue("$minutes", durationMinutes);
        command.ExecuteNonQuery();
    }

    // Deletes one hand-entered record. Timetable records cannot be deleted this way.
    public bool DeleteManualEntry(long id)
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM AttendanceRecords WHERE Id = $id AND IsManual = 1;";
        command.Parameters.AddWithValue("$id", id);
        return command.ExecuteNonQuery() == 1;
    }

    // How many records are dated before this day, and how many of them already have a mark
    // (anything except Not Marked). Used to warn before the semester start moves later.
    public (int Total, int Marked) CountRecordsBefore(DateOnly date)
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT COUNT(*), COALESCE(SUM(CASE WHEN Status <> 'Not Marked' THEN 1 ELSE 0 END), 0)
            FROM AttendanceRecords
            WHERE AttendanceDate < $date;
            """;
        command.Parameters.AddWithValue("$date", FormatDate(date));
        using var reader = command.ExecuteReader();
        reader.Read();
        return (reader.GetInt32(0), reader.GetInt32(1));
    }

    // Permanently deletes every attendance record dated before this day. Returns how many were deleted.
    public int DeleteRecordsBefore(DateOnly date)
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM AttendanceRecords WHERE AttendanceDate < $date;";
        command.Parameters.AddWithValue("$date", FormatDate(date));
        return command.ExecuteNonQuery();
    }

    // Deletes Not Marked records dated on or after this day whose class is no longer in the timetable
    // (same subject, weekday, start and end time). Marked records are never deleted.
    public int DeleteStaleUnmarkedFrom(DateOnly fromDate, DateOnly? untilExclusive = null)
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            DELETE FROM AttendanceRecords
            WHERE Status = 'Not Marked'
              AND IsManual = 0
              AND AttendanceDate >= $fromDate
              AND ($untilExclusive IS NULL OR AttendanceDate < $untilExclusive)
              AND NOT EXISTS (
                  SELECT 1 FROM WeeklySessions ws
                  WHERE ws.SubjectId = AttendanceRecords.SubjectId
                    AND ws.ClassType = AttendanceRecords.ClassType
                    AND ws.DayOfWeek = CAST(strftime('%w', AttendanceRecords.AttendanceDate) AS INTEGER)
                    AND ws.StartTime = AttendanceRecords.ScheduledStart
                    AND ws.EndTime = AttendanceRecords.ScheduledEnd
                    AND ws.EffectiveFrom <= AttendanceRecords.AttendanceDate
                    AND (ws.EffectiveTo IS NULL OR ws.EffectiveTo > AttendanceRecords.AttendanceDate)
              );
            """;
        command.Parameters.AddWithValue("$fromDate", FormatDate(fromDate));
        command.Parameters.AddWithValue("$untilExclusive",
            untilExclusive is null ? DBNull.Value : FormatDate(untilExclusive.Value));
        return command.ExecuteNonQuery();
    }

    private static string FormatDate(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static void EnsureEnabledType(SqliteConnection connection, long subjectId, SubjectType type)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT 1 FROM SubjectClassTypes
            WHERE SubjectId = $subjectId AND Type = $type;
            """;
        command.Parameters.AddWithValue("$subjectId", subjectId);
        command.Parameters.AddWithValue("$type", type.ToString());
        if (command.ExecuteScalar() is null)
            throw new InvalidOperationException($"{type} is not enabled for this subject.");
    }

    private static string FormatStatus(AttendanceStatus status) => status switch
    {
        AttendanceStatus.Present => "Present",
        AttendanceStatus.Absent => "Absent",
        AttendanceStatus.Cancelled => "Cancelled",
        AttendanceStatus.NotMarked => "Not Marked",
        AttendanceStatus.NoClass => "No Class",
        _ => throw new ArgumentOutOfRangeException(nameof(status))
    };

    private static AttendanceStatus ParseStatus(string status) => status switch
    {
        "Present" => AttendanceStatus.Present,
        "Absent" => AttendanceStatus.Absent,
        "Cancelled" => AttendanceStatus.Cancelled,
        "Not Marked" => AttendanceStatus.NotMarked,
        "No Class" => AttendanceStatus.NoClass,
        _ => throw new InvalidOperationException($"Unknown attendance status: {status}")
    };
}


