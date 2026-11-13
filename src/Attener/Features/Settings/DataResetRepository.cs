using Microsoft.Data.Sqlite;

namespace Attener.Features.Settings;

public sealed class DataResetRepository
{
    private readonly string _connectionString;

    public DataResetRepository(string databasePath)
    {
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            ForeignKeys = true
        }.ToString();
    }

    // Removes every row from every table. Child tables go first because they reference Subjects.
    // sqlite_sequence holds the AUTOINCREMENT counters; clearing it makes new ids start from 1 again.
    public void DeleteAllData()
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();
        using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            DELETE FROM AttendanceRecords;
            DELETE FROM WeeklySessions;
            DELETE FROM SubjectClassTypes;
            DELETE FROM Subjects;
            DELETE FROM AppSettings;
            DELETE FROM sqlite_sequence;
            """;
        command.ExecuteNonQuery();
        transaction.Commit();
    }
}