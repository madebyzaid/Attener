using System.Globalization;
using Microsoft.Data.Sqlite;

namespace Attener.Features.Timetable;

public sealed class TimetableSettingsRepository
{
    private const string StartHourKey = "TimetableStartHour";
    private const string EndHourKey = "TimetableEndHour";
    public const int DefaultStartHour = 8;
    public const int DefaultEndHour = 20;
    public const int LatestEndHour = 23;

    private readonly string _connectionString;

    public TimetableSettingsRepository(string databasePath)
    {
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            ForeignKeys = true
        }.ToString();
    }

    // The hours shown in the timetable grid. Falls back to 8-20 if nothing valid is saved.
    public (int StartHour, int EndHour) GetHourRange()
    {
        var start = ReadHour(StartHourKey);
        var end = ReadHour(EndHourKey);
        return start is not null && end is not null && IsValidRange(start.Value, end.Value)
            ? (start.Value, end.Value)
            : (DefaultStartHour, DefaultEndHour);
    }

    public void SaveHourRange(int startHour, int endHour)
    {
        if (!IsValidRange(startHour, endHour))
            throw new ArgumentException("The start hour must be earlier than the end hour, between 0 and 23.");

        using var connection = new SqliteConnection(_connectionString);
        connection.Open();
        using var transaction = connection.BeginTransaction();
        WriteHour(connection, transaction, StartHourKey, startHour);
        WriteHour(connection, transaction, EndHourKey, endHour);
        transaction.Commit();
    }

    private static bool IsValidRange(int startHour, int endHour) =>
        startHour >= 0 && endHour <= LatestEndHour && startHour < endHour;

    private int? ReadHour(string key)
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT Value FROM AppSettings WHERE Key = $key;";
        command.Parameters.AddWithValue("$key", key);
        var value = command.ExecuteScalar() as string;
        return int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var hour) ? hour : null;
    }

    private static void WriteHour(SqliteConnection connection, SqliteTransaction transaction, string key, int hour)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO AppSettings (Key, Value) VALUES ($key, $value)
            ON CONFLICT (Key) DO UPDATE SET Value = excluded.Value;
            """;
        command.Parameters.AddWithValue("$key", key);
        command.Parameters.AddWithValue("$value", hour.ToString(CultureInfo.InvariantCulture));
        command.ExecuteNonQuery();
    }
}