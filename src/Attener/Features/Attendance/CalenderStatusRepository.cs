using System.Globalization;
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

    // Every saved attendance status for one subject, keyed by date.
    // Values are the status words stored in the database: "Present", "Absent", "Cancelled", "Not Marked", "No Class".
    public Dictionary<DateTime, string> GetStatusesForSubject(long subjectId)
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT AttendanceDate, Status
            FROM AttendanceRecords
            WHERE SubjectId = $subjectId;
            """;
        command.Parameters.AddWithValue("$subjectId", subjectId);

        var statuses = new Dictionary<DateTime, string>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            var date = DateTime.ParseExact(reader.GetString(0), "yyyy-MM-dd", CultureInfo.InvariantCulture);
            statuses[date] = reader.GetString(1);
        }
        return statuses;
    }
}