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
            SELECT s.Id, s.Name, s.IsArchived, ct.Type, ct.Professor, ct.Venue
            FROM Subjects s
            LEFT JOIN SubjectClassTypes ct ON ct.SubjectId = s.Id
            WHERE s.IsArchived = $isArchived
            ORDER BY s.Name COLLATE NOCASE,
                CASE ct.Type WHEN 'Lecture' THEN 0 WHEN 'Tutorial' THEN 1 ELSE 2 END;
            """;
        command.Parameters.AddWithValue("$isArchived", includeArchived ? 1 : 0);

        var records = new List<(long Id, string Name, bool IsArchived, List<SubjectClassDetails> ClassDetails)>();
        var recordIndexes = new Dictionary<long, int>();
        using (var reader = command.ExecuteReader())
        {
            while (reader.Read())
            {
                var id = reader.GetInt64(0);
                if (!recordIndexes.TryGetValue(id, out var index))
                {
                    index = records.Count;
                    recordIndexes.Add(id, index);
                    records.Add((id, reader.GetString(1), reader.GetInt64(2) == 1, []));
                }

                if (!reader.IsDBNull(3))
                {
                    records[index].ClassDetails.Add(new SubjectClassDetails(
                        Enum.Parse<SubjectType>(reader.GetString(3)), reader.GetString(4), reader.GetString(5)));
                }
            }
        }

        return records.Select(record => new SubjectSummary(
            record.Id, record.Name, record.ClassDetails, record.IsArchived)).ToArray();
    }

    public bool AddSubject(string name, IReadOnlyCollection<SubjectClassDetails> classDetails)
    {
        ValidateClassDetails(classDetails);
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();
        using var transaction = connection.BeginTransaction();

        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO Subjects (Name)
            SELECT $name
            WHERE NOT EXISTS (SELECT 1 FROM Subjects WHERE Name = $name COLLATE NOCASE);
            """;
        command.Parameters.AddWithValue("$name", name.Trim());
        if (command.ExecuteNonQuery() != 1)
        {
            transaction.Rollback();
            return false;
        }

        using var idCommand = connection.CreateCommand();
        idCommand.Transaction = transaction;
        idCommand.CommandText = "SELECT last_insert_rowid();";
        var subjectId = Convert.ToInt64(idCommand.ExecuteScalar(), CultureInfo.InvariantCulture);
        foreach (var details in classDetails)
            SaveTypeDetails(connection, transaction, subjectId, details);

        transaction.Commit();
        return true;
    }

    public void UpdateSubject(long subjectId, string name, IReadOnlyCollection<SubjectClassDetails> classDetails)
    {
        ValidateClassDetails(classDetails);
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();
        using var transaction = connection.BeginTransaction();

        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            UPDATE Subjects
            SET Name = $name
            WHERE Id = $id;
            """;
        command.Parameters.AddWithValue("$name", name.Trim());
        command.Parameters.AddWithValue("$id", subjectId);
        if (command.ExecuteNonQuery() != 1)
        {
            transaction.Rollback();
            return;
        }

        foreach (var details in classDetails)
            SaveTypeDetails(connection, transaction, subjectId, details);

        using var remove = connection.CreateCommand();
        remove.Transaction = transaction;
        remove.CommandText = """
            SELECT Type FROM SubjectClassTypes
            WHERE SubjectId = $subjectId;
            """;
        remove.Parameters.AddWithValue("$subjectId", subjectId);
        var oldTypes = new List<SubjectType>();
        using (var reader = remove.ExecuteReader())
        {
            while (reader.Read())
                oldTypes.Add(Enum.Parse<SubjectType>(reader.GetString(0)));
        }

        var selectedTypes = classDetails.Select(details => details.Type).ToHashSet();
        foreach (var type in oldTypes.Except(selectedTypes))
        {
            using var deleteRecords = connection.CreateCommand();
            deleteRecords.Transaction = transaction;
            deleteRecords.CommandText = """
                DELETE FROM AttendanceRecords WHERE SubjectId = $subjectId AND ClassType = $type;
                DELETE FROM WeeklySessions WHERE SubjectId = $subjectId AND ClassType = $type;
                DELETE FROM SubjectClassTypes WHERE SubjectId = $subjectId AND Type = $type;
                """;
            deleteRecords.Parameters.AddWithValue("$subjectId", subjectId);
            deleteRecords.Parameters.AddWithValue("$type", type.ToString());
            deleteRecords.ExecuteNonQuery();
        }

        transaction.Commit();
    }

    private static void SaveTypeDetails(SqliteConnection connection, SqliteTransaction transaction,
        long subjectId, SubjectClassDetails details)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO SubjectClassTypes (SubjectId, Type, Professor, Venue)
            VALUES ($subjectId, $type, $professor, $venue)
            ON CONFLICT (SubjectId, Type) DO UPDATE SET
                Professor = excluded.Professor,
                Venue = excluded.Venue;
            """;
        command.Parameters.AddWithValue("$subjectId", subjectId);
        command.Parameters.AddWithValue("$type", details.Type.ToString());
        command.Parameters.AddWithValue("$professor", details.Professor.Trim());
        command.Parameters.AddWithValue("$venue", details.Venue.Trim());
        command.ExecuteNonQuery();
    }

    private static void ValidateClassDetails(IReadOnlyCollection<SubjectClassDetails> classDetails)
    {
        if (classDetails.Count == 0)
            throw new ArgumentException("A subject must have at least one class type.", nameof(classDetails));
        if (classDetails.Any(details => !Enum.IsDefined(details.Type)))
            throw new ArgumentOutOfRangeException(nameof(classDetails), "A class type is invalid.");
        if (classDetails.Select(details => details.Type).Distinct().Count() != classDetails.Count)
            throw new ArgumentException("Each class type can only be included once.", nameof(classDetails));
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

        foreach (var table in new[] { "AttendanceRecords", "WeeklySessions", "SubjectClassTypes" })
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