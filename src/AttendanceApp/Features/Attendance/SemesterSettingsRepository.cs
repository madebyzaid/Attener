using System.Globalization;
using Microsoft.Data.Sqlite;

namespace AttendanceApp.Features.Attendance;

public sealed class SemesterSettingsRepository
{
    private const string SemesterStartKey = "SemesterStartDate";
    private readonly string _connectionString;

    public SemesterSettingsRepository(string databasePath)
    {
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            ForeignKeys = true
        }.ToString();
    }

    public DateOnly? GetSemesterStartDate()
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT Value FROM AppSettings WHERE Key = $key;";
        command.Parameters.AddWithValue("$key", SemesterStartKey);
        var value = command.ExecuteScalar() as string;
        return DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture,
            DateTimeStyles.None, out var date) ? date : null;
    }

    public void SaveSemesterStartDate(DateOnly date)
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO AppSettings (Key, Value) VALUES ($key, $value)
            ON CONFLICT (Key) DO UPDATE SET Value = excluded.Value;
            """;
        command.Parameters.AddWithValue("$key", SemesterStartKey);
        command.Parameters.AddWithValue("$value", date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        command.ExecuteNonQuery();
    }
}
