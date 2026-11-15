using System.Globalization;
using Attener.Features.Subjects;
using Microsoft.Data.Sqlite;

namespace Attener.Features.Attendance;

public sealed class CalendarStatusRepository
{
    private readonly string _connectionString;

    public CalendarStatusRepository(string databasePath)
    {
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            ForeignKeys = true
        }.ToString();
    }

    // One status per date for one subject, used to colour the calendar day.
    // Values are the status words stored in the database: "Present", "Absent", "Cancelled", "Not Marked", "No Class".
    // If the subject has several classes on one day, the most important status wins (see Rank).
    public Dictionary<DateTime, string> GetStatusesForSubject(long subjectId, SubjectType? classType = null)
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT AttendanceDate, Status
            FROM AttendanceRecords
            WHERE SubjectId = $subjectId
              AND ($classType IS NULL OR ClassType = $classType);
            """;
        command.Parameters.AddWithValue("$subjectId", subjectId);
        command.Parameters.AddWithValue("$classType", classType is null ? DBNull.Value : classType.Value.ToString());

        var statuses = new Dictionary<DateTime, string>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            var date = DateTime.ParseExact(reader.GetString(0), "yyyy-MM-dd", CultureInfo.InvariantCulture);
            var status = reader.GetString(1);
            if (!statuses.TryGetValue(date, out var existing) || Rank(status) < Rank(existing))
                statuses[date] = status;
        }
        return statuses;
    }

    // Lower number = wins the day's colour.
    private static int Rank(string status) => status switch
    {
        "Absent" => 0,
        "Not Marked" => 1,
        "Present" => 2,
        "Cancelled" => 3,
        _ => 4
    };
}