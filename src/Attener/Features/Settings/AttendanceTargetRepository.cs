using System.Globalization;
using Microsoft.Data.Sqlite;

namespace Attener.Features.Settings;

public sealed class AttendanceTargetRepository
{
    private const string TargetKey = "AttendanceTargetPercent";
    public const int DefaultTarget = 75;
    public const int MinTarget = 1;
    public const int MaxTarget = 99;

    private readonly string _connectionString;

    public AttendanceTargetRepository(string databasePath)
    {
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            ForeignKeys = true
        }.ToString();
    }

    public int GetTarget()
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT Value FROM AppSettings WHERE Key = $key;";
        command.Parameters.AddWithValue("$key", TargetKey);
        var value = command.ExecuteScalar() as string;
        return int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var percent)
               && percent is >= MinTarget and <= MaxTarget
            ? percent
            : DefaultTarget;
    }

    public void SaveTarget(int percent)
    {
        if (percent is < MinTarget or > MaxTarget)
            throw new ArgumentException($"The target must be between {MinTarget} and {MaxTarget}.");

        using var connection = new SqliteConnection(_connectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO AppSettings (Key, Value) VALUES ($key, $value)
            ON CONFLICT (Key) DO UPDATE SET Value = excluded.Value;
            """;
        command.Parameters.AddWithValue("$key", TargetKey);
        command.Parameters.AddWithValue("$value", percent.ToString(CultureInfo.InvariantCulture));
        command.ExecuteNonQuery();
    }
}
