using System.Globalization;
using Microsoft.Data.Sqlite;

namespace Attener.Features.Timetable;

public sealed class TimetableSettingsRepository
{
    private const string LegacyStartHourKey = "TimetableStartHour";
    private const string LegacyEndHourKey = "TimetableEndHour";
    private const string StartHourKeyPrefix = "TimetableStartHour:";
    private const string EndHourKeyPrefix = "TimetableEndHour:";
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

    // Each timetable period has its own visible range. The legacy range is used only for the current period.
    public (int StartHour, int EndHour) GetHourRange(DateOnly timetableStart, bool isCurrent)
    {
        var dateKey = timetableStart.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var range = ReadHourRange($"{StartHourKeyPrefix}{dateKey}", $"{EndHourKeyPrefix}{dateKey}");
        if (range is not null)
            return range.Value;

        if (isCurrent)
        {
            range = ReadHourRange(LegacyStartHourKey, LegacyEndHourKey);
            if (range is not null)
                return range.Value;
        }

        return (DefaultStartHour, DefaultEndHour);
    }

    public void SaveHourRange(DateOnly timetableStart, int startHour, int endHour)
    {
        if (!IsValidRange(startHour, endHour))
            throw new ArgumentException("The start hour must be earlier than the end hour, between 0 and 23.");

        var dateKey = timetableStart.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();
        using var transaction = connection.BeginTransaction();
        WriteHour(connection, transaction, $"{StartHourKeyPrefix}{dateKey}", startHour);
        WriteHour(connection, transaction, $"{EndHourKeyPrefix}{dateKey}", endHour);
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

    private (int StartHour, int EndHour)? ReadHourRange(string startKey, string endKey)
    {
        var start = ReadHour(startKey);
        var end = ReadHour(endKey);
        return start is not null && end is not null && IsValidRange(start.Value, end.Value)
            ? (start.Value, end.Value)
            : null;
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