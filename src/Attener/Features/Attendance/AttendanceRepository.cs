using System.Globalization;
using Attener.Domain;
using Microsoft.Data.Sqlite;

namespace Attener.Features.Attendance;

public sealed record AttendanceEntry(
    long SubjectId,
    string SubjectName,
    DateOnly AttendanceDate,
    string? ScheduledStart,
    string? ScheduledEnd,
    int ScheduledDurationMinutes,
    AttendanceStatus? Status,
    int? ActualDurationMinutes,
    bool IsSaved);

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
                SubjectId, AttendanceDate, Status, ScheduledStart, ScheduledEnd,
                ScheduledDurationMinutes, ActualDurationMinutes
            )
            SELECT ws.SubjectId, dates.AttendanceDate, 'Not Marked', ws.StartTime, ws.EndTime,
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
                SELECT a.SubjectId, s.Name, a.AttendanceDate, a.ScheduledStart, a.ScheduledEnd,
                       a.ScheduledDurationMinutes, a.Status, a.ActualDurationMinutes, 1
                FROM AttendanceRecords a
                JOIN Subjects s ON s.Id = a.SubjectId
                WHERE a.AttendanceDate = $date
                ORDER BY a.ScheduledStart, s.Name COLLATE NOCASE;
                """;
        }
        else
        {
            command.CommandText = """
                SELECT ws.SubjectId, s.Name, $date, ws.StartTime, ws.EndTime,
                       (CAST(substr(ws.EndTime, 1, 2) AS INTEGER) * 60 + CAST(substr(ws.EndTime, 4, 2) AS INTEGER))
                       - (CAST(substr(ws.StartTime, 1, 2) AS INTEGER) * 60 + CAST(substr(ws.StartTime, 4, 2) AS INTEGER)),
                       NULL, NULL, 0
                FROM WeeklySessions ws
                JOIN Subjects s ON s.Id = ws.SubjectId
                WHERE ws.DayOfWeek = $dayOfWeek
                  AND ws.EffectiveFrom <= $date
                  AND (ws.EffectiveTo IS NULL OR ws.EffectiveTo > $date)
                  AND (s.IsArchived = 0 OR (s.ArchivedOn IS NOT NULL AND s.ArchivedOn > $date))
                ORDER BY ws.StartTime, s.Name COLLATE NOCASE;
                """;
            command.Parameters.AddWithValue("$dayOfWeek", (int)date.DayOfWeek);
        }
        command.Parameters.AddWithValue("$date", FormatDate(date));

        using var reader = command.ExecuteReader();
        var entries = new List<AttendanceEntry>();
        while (reader.Read())
        {
            entries.Add(new AttendanceEntry(
                reader.GetInt64(0), reader.GetString(1), date,
                reader.IsDBNull(3) ? null : reader.GetString(3),
                reader.IsDBNull(4) ? null : reader.GetString(4),
                reader.GetInt32(5),
                reader.IsDBNull(6) ? null : ParseStatus(reader.GetString(6)),
                reader.IsDBNull(7) ? null : reader.GetInt32(7),
                reader.GetInt64(8) == 1));
        }
        return entries;
    }


    public IReadOnlyList<AttendanceEntry> GetPendingAttendance(DateOnly throughDate)
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT a.SubjectId, s.Name, a.AttendanceDate, a.ScheduledStart, a.ScheduledEnd,
                   a.ScheduledDurationMinutes, a.Status, a.ActualDurationMinutes, 1
            FROM AttendanceRecords a
            JOIN Subjects s ON s.Id = a.SubjectId
            WHERE a.Status = 'Not Marked' AND a.AttendanceDate <= $throughDate
            ORDER BY a.AttendanceDate DESC, a.ScheduledStart, s.Name COLLATE NOCASE;
            """;
        command.Parameters.AddWithValue("$throughDate", FormatDate(throughDate));

        using var reader = command.ExecuteReader();
        var entries = new List<AttendanceEntry>();
        while (reader.Read())
        {
            entries.Add(new AttendanceEntry(
                reader.GetInt64(0), reader.GetString(1), DateOnly.ParseExact(reader.GetString(2), "yyyy-MM-dd", CultureInfo.InvariantCulture),
                reader.IsDBNull(3) ? null : reader.GetString(3),
                reader.IsDBNull(4) ? null : reader.GetString(4),
                reader.GetInt32(5), ParseStatus(reader.GetString(6)),
                reader.IsDBNull(7) ? null : reader.GetInt32(7), true));
        }
        return entries;
    }
    public void SaveAttendance(AttendanceEntry entry, AttendanceStatus status, int? actualDurationMinutes)
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO AttendanceRecords (
                SubjectId, AttendanceDate, Status, ScheduledStart, ScheduledEnd,
                ScheduledDurationMinutes, ActualDurationMinutes
            )
            VALUES ($subjectId, $date, $status, $start, $end, $scheduledMinutes, $actualMinutes)
            ON CONFLICT (SubjectId, AttendanceDate, ScheduledStart) DO UPDATE SET
                Status = excluded.Status,
                ScheduledStart = excluded.ScheduledStart,
                ScheduledEnd = excluded.ScheduledEnd,
                ScheduledDurationMinutes = excluded.ScheduledDurationMinutes,
                ActualDurationMinutes = excluded.ActualDurationMinutes;
            """;
        command.Parameters.AddWithValue("$subjectId", entry.SubjectId);
        command.Parameters.AddWithValue("$date", FormatDate(entry.AttendanceDate));
        command.Parameters.AddWithValue("$status", FormatStatus(status));
        command.Parameters.AddWithValue("$start", (object?)entry.ScheduledStart ?? DBNull.Value);
        command.Parameters.AddWithValue("$end", (object?)entry.ScheduledEnd ?? DBNull.Value);
        command.Parameters.AddWithValue("$scheduledMinutes", entry.ScheduledDurationMinutes);
        command.Parameters.AddWithValue("$actualMinutes", (object?)actualDurationMinutes ?? DBNull.Value);
        command.ExecuteNonQuery();
    }

    private static string FormatDate(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

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


