using System.Globalization;
using Attener.Features.Subjects;
using Microsoft.Data.Sqlite;

namespace Attener.Features.Statistics;

public sealed record SubjectAttendanceSummary(
    long SubjectId,
    string SubjectName,
    bool IsArchived,
    int AttendedMinutes,
    int CountedMinutes);

public sealed record ClassTypeAttendanceSummary(
    SubjectType Type,
    string Venue,
    string Professor,
    int AttendedMinutes,
    int CountedMinutes);

public sealed class StatisticsRepository
{
    private readonly string _connectionString;

    public StatisticsRepository(string databasePath)
    {
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            ForeignKeys = true
        }.ToString();
    }

    public IReadOnlyList<SubjectAttendanceSummary> GetSubjectSummaries(DateOnly startDate, DateOnly endDate)
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT s.Id, s.Name, s.IsArchived,
                   SUM(CASE WHEN a.Status = 'Present'
                       THEN COALESCE(a.ActualDurationMinutes, a.ScheduledDurationMinutes) ELSE 0 END) AS AttendedMinutes,
                   SUM(CASE WHEN a.Status IN ('Present', 'Absent')
                       THEN COALESCE(a.ActualDurationMinutes, a.ScheduledDurationMinutes) ELSE 0 END) AS CountedMinutes
            FROM AttendanceRecords a
            JOIN Subjects s ON s.Id = a.SubjectId
            WHERE a.AttendanceDate >= $startDate AND a.AttendanceDate <= $endDate
            GROUP BY s.Id, s.Name, s.IsArchived
            HAVING SUM(CASE WHEN a.Status IN ('Present', 'Absent') THEN COALESCE(a.ActualDurationMinutes, a.ScheduledDurationMinutes) ELSE 0 END) > 0
            ORDER BY s.IsArchived, s.Name COLLATE NOCASE;
            """;
        command.Parameters.AddWithValue("$startDate", FormatDate(startDate));
        command.Parameters.AddWithValue("$endDate", FormatDate(endDate));

        using var reader = command.ExecuteReader();
        var summaries = new List<SubjectAttendanceSummary>();
        while (reader.Read())
        {
            summaries.Add(new SubjectAttendanceSummary(
                reader.GetInt64(0), reader.GetString(1), reader.GetInt64(2) == 1,
                reader.GetInt32(3), reader.GetInt32(4)));
        }
        return summaries;
    }

    public IReadOnlyList<ClassTypeAttendanceSummary> GetClassTypeSummaries(
        long subjectId, DateOnly startDate, DateOnly endDate)
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT ct.Type, ct.Venue, ct.Professor,
                   COALESCE(SUM(CASE WHEN a.Status = 'Present'
                       THEN COALESCE(a.ActualDurationMinutes, a.ScheduledDurationMinutes) ELSE 0 END), 0),
                   COALESCE(SUM(CASE WHEN a.Status IN ('Present', 'Absent')
                       THEN COALESCE(a.ActualDurationMinutes, a.ScheduledDurationMinutes) ELSE 0 END), 0)
            FROM SubjectClassTypes ct
            LEFT JOIN AttendanceRecords a
              ON a.SubjectId = ct.SubjectId AND a.ClassType = ct.Type
             AND a.AttendanceDate >= $startDate AND a.AttendanceDate <= $endDate
            WHERE ct.SubjectId = $subjectId
            GROUP BY ct.Type
            ORDER BY CASE ct.Type
                WHEN 'Lecture' THEN 0
                WHEN 'Tutorial' THEN 1
                ELSE 2
            END;
            """;
        command.Parameters.AddWithValue("$subjectId", subjectId);
        command.Parameters.AddWithValue("$startDate", FormatDate(startDate));
        command.Parameters.AddWithValue("$endDate", FormatDate(endDate));

        using var reader = command.ExecuteReader();
        var summaries = new List<ClassTypeAttendanceSummary>();
        while (reader.Read())
        {
            summaries.Add(new ClassTypeAttendanceSummary(
                Enum.Parse<SubjectType>(reader.GetString(0)), reader.GetString(1), reader.GetString(2),
                reader.GetInt32(3), reader.GetInt32(4)));
        }
        return summaries;
    }

    private static string FormatDate(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}





