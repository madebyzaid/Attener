using System.Globalization;
using Microsoft.Data.Sqlite;

namespace AttendanceApp.Features.Statistics;

public sealed record SubjectAttendanceSummary(
    string SubjectName,
    bool IsArchived,
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
            SELECT s.Name, s.IsArchived,
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
                reader.GetString(0), reader.GetInt64(1) == 1,
                reader.GetInt32(2), reader.GetInt32(3)));
        }
        return summaries;
    }

    private static string FormatDate(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}




