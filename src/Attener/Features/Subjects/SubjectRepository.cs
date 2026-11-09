using System.Globalization;
using Microsoft.Data.Sqlite;

namespace Attener.Features.Subjects;

public sealed class SubjectRepository
{
    private readonly string _connectionString;

    public SubjectRepository(string databasePath)
    {
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            ForeignKeys = true
        }.ToString();
    }

    public IReadOnlyList<SubjectSummary> GetSubjects(bool includeArchived)
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT Id, Name, IsArchived
            FROM Subjects
            WHERE IsArchived = $isArchived
            ORDER BY Name COLLATE NOCASE;
            """;
        command.Parameters.AddWithValue("$isArchived", includeArchived ? 1 : 0);

        using var reader = command.ExecuteReader();
        var subjects = new List<SubjectSummary>();
        while (reader.Read())
        {
            subjects.Add(new SubjectSummary(
                reader.GetInt64(0), reader.GetString(1), reader.GetInt64(2) == 1));
        }
        return subjects;
    }

    public bool AddSubject(string name)
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO Subjects (Name)
            SELECT $name
            WHERE NOT EXISTS (SELECT 1 FROM Subjects WHERE Name = $name COLLATE NOCASE);
            """;
        command.Parameters.AddWithValue("$name", name.Trim());
        return command.ExecuteNonQuery() == 1;
    }

    public bool RenameSubject(long subjectId, string name)
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE Subjects SET Name = $name WHERE Id = $id;";
        command.Parameters.AddWithValue("$name", name.Trim());
        command.Parameters.AddWithValue("$id", subjectId);
        return command.ExecuteNonQuery() == 1;
    }
    public bool ArchiveSubject(long subjectId, DateOnly archivedOn)
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();
        using var transaction = connection.BeginTransaction();

        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            UPDATE Subjects
            SET IsArchived = 1, ArchivedOn = $archivedOn
            WHERE Id = $id AND IsArchived = 0;
            """;
        command.Parameters.AddWithValue("$archivedOn", archivedOn.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$id", subjectId);
        var changed = command.ExecuteNonQuery() == 1;
        transaction.Commit();
        return changed;
    }

    // Permanently removes a subject together with its attendance records and timetable entries.
    // The child tables go first because they reference Subjects.
    public bool DeleteSubject(long subjectId)
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();
        using var transaction = connection.BeginTransaction();

        foreach (var table in new[] { "AttendanceRecords", "WeeklySessions" })
        {
            using var child = connection.CreateCommand();
            child.Transaction = transaction;
            child.CommandText = $"DELETE FROM {table} WHERE SubjectId = $id;";
            child.Parameters.AddWithValue("$id", subjectId);
            child.ExecuteNonQuery();
        }

        using var subject = connection.CreateCommand();
        subject.Transaction = transaction;
        subject.CommandText = "DELETE FROM Subjects WHERE Id = $id;";
        subject.Parameters.AddWithValue("$id", subjectId);
        var deleted = subject.ExecuteNonQuery() == 1;

        transaction.Commit();
        return deleted;
    }
}