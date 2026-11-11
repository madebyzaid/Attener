using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Attener.Features.Attendance;
using Attener.Features.Settings;
using Attener.Infrastructure.Database;
using Microsoft.Data.Sqlite;

namespace Attener.Views;

public partial class SettingsView : UserControl
{
    private readonly SemesterSettingsRepository _settings = new(DatabaseLocation.GetPath());
    private readonly AttendanceRepository _attendance = new(DatabaseLocation.GetPath());
    private readonly DataResetRepository _reset = new(DatabaseLocation.GetPath());
    private readonly string _databasePath = DatabaseLocation.GetPath();

    public SettingsView()
    {
        InitializeComponent();
        RefreshData();
    }

    public void RefreshData()
    {
        var startDate = _settings.GetSemesterStartDate();
        SemesterStartPicker.SelectedDate = startDate?.ToDateTime(TimeOnly.MinValue);
        GenerationMessage.Text = "";
    }

    private void GenerateRecords_Click(object sender, RoutedEventArgs e)
    {
        if (SemesterStartPicker.SelectedDate is not DateTime selectedDate)
        {
            MessageBox.Show("Choose the semester start date first.", "Date required",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var startDate = DateOnly.FromDateTime(selectedDate);
        var today = DateOnly.FromDateTime(DateTime.Today);

        // Moving the start later removes everything before it, so ask first.
        var (total, marked) = _attendance.CountRecordsBefore(startDate);
        var deleted = 0;
        if (total > 0)
        {
            var answer = MessageBox.Show(
                $"Moving the semester start to {startDate:dd MMM yyyy} will permanently delete {total} attendance record(s) before that date" +
                (marked > 0 ? $", including {marked} you already marked" : "") +
                ".\n\nThis cannot be undone. Continue?",
                "Delete earlier records?", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);
            if (answer != MessageBoxResult.Yes)
            {
                RefreshData();
                return;
            }
            deleted = _attendance.DeleteRecordsBefore(startDate);
        }

        _settings.SaveSemesterStartDate(startDate);
        var created = _attendance.GenerateMissingBetween(startDate, today);

        var deletedNote = deleted > 0 ? $" {deleted} earlier record(s) were deleted." : "";
        GenerationMessage.Text = startDate > today
            ? "The semester start is in the future. Historical records will be generated when that date arrives." + deletedNote
            : $"Semester start saved. {created} pending class record(s) were created; existing attendance was preserved.{deletedNote}";
    }

    private void SeedTestData_Click(object sender, RoutedEventArgs e)
    {
        var result = MessageBox.Show(
            "This clears all current data and inserts fake attendance/test data. Continue?",
            "Seed test data",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No);

        if (result != MessageBoxResult.Yes)
            return;

        try
        {
            SeedTestData();
            RefreshData();
            MessageBox.Show("Test data was seeded successfully.", "Done",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not seed test data. {ex.Message}", "Seed failed",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void DeleteAllData_Click(object sender, RoutedEventArgs e)
    {
        if (!ConfirmDeleteAll())
            return;

        try
        {
            _reset.DeleteAllData();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not delete the data. {ex.Message}", "Delete failed",
                MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        RefreshData();
        MessageBox.Show("All data has been deleted.", "Done",
            MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void SeedTestData()
    {
        _reset.DeleteAllData();

        var semesterStart = new DateOnly(2026, 8, 1);
        var yesterday = new DateOnly(2026, 11, 10);

        var subjectNames = new[]
        {
            "EC101P",
            "ME105L",
            "AM101L",
            "CS101P",
            "EC101L",
            "ME102L"
        };

        var archivedSubject = "ME102L";
        var subjectIdByName = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = _databasePath,
            ForeignKeys = true
        }.ToString();

        using var connection = new SqliteConnection(connectionString);
        connection.Open();
        using var transaction = connection.BeginTransaction();

        using (var setting = connection.CreateCommand())
        {
            setting.Transaction = transaction;
            setting.CommandText = "INSERT INTO AppSettings (Key, Value) VALUES ('SemesterStartDate', $value);";
            setting.Parameters.AddWithValue("$value", semesterStart.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
            setting.ExecuteNonQuery();
        }

        foreach (var name in subjectNames)
        {
            var isArchived = string.Equals(name, archivedSubject, StringComparison.OrdinalIgnoreCase) ? 1 : 0;
            var archivedOn = isArchived == 1 ? "2026-09-01" : null;

            using var insertSubject = connection.CreateCommand();
            insertSubject.Transaction = transaction;
            insertSubject.CommandText = "INSERT INTO Subjects (Name, IsArchived, ArchivedOn) VALUES ($name, $isArchived, $archivedOn);";
            insertSubject.Parameters.AddWithValue("$name", name);
            insertSubject.Parameters.AddWithValue("$isArchived", isArchived);
            insertSubject.Parameters.AddWithValue("$archivedOn", archivedOn ?? (object)DBNull.Value);
            insertSubject.ExecuteNonQuery();

            using var getId = connection.CreateCommand();
            getId.Transaction = transaction;
            getId.CommandText = "SELECT last_insert_rowid();";
            var lastId = getId.ExecuteScalar();
            subjectIdByName[name] = Convert.ToInt64(lastId);
        }

        var weeklySessions = new[]
        {
            new SeederSession("EC101P", 1, "09:00", "11:00", "2026-08-01", "2026-09-01"),
            new SeederSession("EC101P", 3, "11:00", "13:00", "2026-08-01", "2026-09-01"),
            new SeederSession("ME105L", 2, "14:00", "16:00", "2026-08-01", "2026-09-01"),
            new SeederSession("ME105L", 4, "10:00", "12:00", "2026-08-01", "2026-09-01"),
            new SeederSession("AM101L", 4, "13:00", "15:00", "2026-08-01", "2026-09-01"),
            new SeederSession("CS101P", 5, "09:00", "11:00", "2026-08-01", "2026-09-01"),
            new SeederSession("ME102L", 6, "07:00", "08:00", "2026-08-01", "2026-09-01"),

            new SeederSession("CS101P", 1, "10:00", "12:00", "2026-09-01", "2026-10-01"),
            new SeederSession("EC101L", 2, "09:00", "11:00", "2026-09-01", "2026-10-01"),
            new SeederSession("ME105L", 2, "13:00", "15:00", "2026-09-01", "2026-10-01"),
            new SeederSession("AM101L", 3, "14:00", "16:00", "2026-09-01", "2026-10-01"),
            new SeederSession("EC101P", 5, "11:00", "13:00", "2026-09-01", "2026-10-01"),

            new SeederSession("ME105L", 0, "15:00", "17:00", "2026-10-01", "2026-11-01"),
            new SeederSession("CS101P", 1, "08:00", "10:00", "2026-10-01", "2026-11-01"),
            new SeederSession("EC101L", 3, "12:00", "14:00", "2026-10-01", "2026-11-01"),
            new SeederSession("EC101P", 4, "16:00", "18:00", "2026-10-01", "2026-11-01"),
            new SeederSession("AM101L", 5, "10:00", "12:00", "2026-10-01", "2026-11-01"),

            new SeederSession("AM101L", 1, "13:00", "15:00", "2026-11-01", null),
            new SeederSession("ME105L", 2, "09:00", "11:00", "2026-11-01", null),
            new SeederSession("EC101P", 3, "15:00", "17:00", "2026-11-01", null),
            new SeederSession("CS101P", 4, "11:00", "13:00", "2026-11-01", null),
            new SeederSession("EC101L", 5, "08:00", "10:00", "2026-11-01", null)
        };

        foreach (var session in weeklySessions)
        {
            using var insertSession = connection.CreateCommand();
            insertSession.Transaction = transaction;
            insertSession.CommandText = "INSERT INTO WeeklySessions (SubjectId, DayOfWeek, StartTime, EndTime, EffectiveFrom, EffectiveTo, EndedByEndDate) VALUES ($subjectId, $dayOfWeek, $startTime, $endTime, $effectiveFrom, $effectiveTo, 0);";
            insertSession.Parameters.AddWithValue("$subjectId", subjectIdByName[session.Subject]);
            insertSession.Parameters.AddWithValue("$dayOfWeek", session.DayOfWeek);
            insertSession.Parameters.AddWithValue("$startTime", session.Start);
            insertSession.Parameters.AddWithValue("$endTime", session.End);
            insertSession.Parameters.AddWithValue("$effectiveFrom", session.EffectiveFrom);
            insertSession.Parameters.AddWithValue("$effectiveTo", session.EffectiveTo is null ? DBNull.Value : session.EffectiveTo);
            insertSession.ExecuteNonQuery();
        }

        var occurrences = new List<(SeederSession Session, DateOnly Date)>();

        foreach (var session in weeklySessions)
        {
            var effectiveFrom = DateOnly.ParseExact(session.EffectiveFrom, "yyyy-MM-dd", CultureInfo.InvariantCulture);
            var effectiveTo = session.EffectiveTo is null
                ? yesterday.AddDays(1)
                : DateOnly.ParseExact(session.EffectiveTo, "yyyy-MM-dd", CultureInfo.InvariantCulture);

            for (var date = effectiveFrom; date < effectiveTo && date <= yesterday; date = date.AddDays(1))
            {
                if ((int)date.DayOfWeek == session.DayOfWeek)
                    occurrences.Add((session, date));
            }
        }

        var random = new Random(42);
        var statusRates = new[]
        {
            (Status: "Present", Rate: 0.75),
            (Status: "Absent", Rate: 0.15),
            (Status: "Cancelled", Rate: 0.05),
            (Status: "Not Marked", Rate: 0.05)
        };
        var statusCounts = statusRates
            .Select(item => (item.Status, Count: (int)Math.Floor(occurrences.Count * item.Rate),
                Remainder: occurrences.Count * item.Rate % 1))
            .ToArray();
        var remainingStatuses = occurrences.Count - statusCounts.Sum(item => item.Count);
        var statusOrder = statusCounts
            .Select((item, index) => (item.Remainder, Index: index))
            .OrderByDescending(item => item.Remainder)
            .Select(item => item.Index)
            .ToArray();
        var statusByName = statusCounts.Select(item => item.Status).ToArray();
        var statusAllocations = statusCounts.Select(item => item.Count).ToArray();
        for (var i = 0; i < remainingStatuses; i++)
            statusAllocations[statusOrder[i]]++;

        var statuses = statusByName
            .SelectMany((status, index) => Enumerable.Repeat(status, statusAllocations[index]))
            .ToArray();
        for (var i = statuses.Length - 1; i > 0; i--)
        {
            var j = random.Next(i + 1);
            (statuses[i], statuses[j]) = (statuses[j], statuses[i]);
        }

        for (var i = 0; i < occurrences.Count; i++)
        {
            var (session, date) = occurrences[i];
            using var insertRecord = connection.CreateCommand();
            insertRecord.Transaction = transaction;
            insertRecord.CommandText = "INSERT INTO AttendanceRecords (SubjectId, AttendanceDate, Status, ScheduledStart, ScheduledEnd, ScheduledDurationMinutes, ActualDurationMinutes, IsManual) VALUES ($subjectId, $attendanceDate, $status, $scheduledStart, $scheduledEnd, $scheduledDurationMinutes, NULL, 0);";
            insertRecord.Parameters.AddWithValue("$subjectId", subjectIdByName[session.Subject]);
            insertRecord.Parameters.AddWithValue("$attendanceDate", date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
            insertRecord.Parameters.AddWithValue("$status", statuses[i]);
            insertRecord.Parameters.AddWithValue("$scheduledStart", session.Start);
            insertRecord.Parameters.AddWithValue("$scheduledEnd", session.End);
            insertRecord.Parameters.AddWithValue("$scheduledDurationMinutes", GetMinutes(session.End) - GetMinutes(session.Start));
            insertRecord.ExecuteNonQuery();
        }

        var manualEntries = new[]
        {
            (Subject: "EC101P", Date: new DateOnly(2026, 8, 9), Status: "Present", Duration: 45),
            (Subject: "AM101L", Date: new DateOnly(2026, 9, 20), Status: "Absent", Duration: 60),
            (Subject: "CS101P", Date: new DateOnly(2026, 10, 25), Status: "Cancelled", Duration: 90),
            (Subject: "ME105L", Date: new DateOnly(2026, 11, 8), Status: "Not Marked", Duration: 75)
        };

        foreach (var entry in manualEntries)
        {
            using var insertManual = connection.CreateCommand();
            insertManual.Transaction = transaction;
            insertManual.CommandText = "INSERT INTO AttendanceRecords (SubjectId, AttendanceDate, Status, ScheduledStart, ScheduledEnd, ScheduledDurationMinutes, ActualDurationMinutes, IsManual) VALUES ($subjectId, $attendanceDate, $status, NULL, NULL, $scheduledDurationMinutes, NULL, 1);";
            insertManual.Parameters.AddWithValue("$subjectId", subjectIdByName[entry.Subject]);
            insertManual.Parameters.AddWithValue("$attendanceDate", entry.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
            insertManual.Parameters.AddWithValue("$status", entry.Status);
            insertManual.Parameters.AddWithValue("$scheduledDurationMinutes", entry.Duration);
            insertManual.ExecuteNonQuery();
        }

        transaction.Commit();
    }

    private sealed record SeederSession(
        string Subject,
        int DayOfWeek,
        string Start,
        string End,
        string EffectiveFrom,
        string? EffectiveTo);

    private static int GetMinutes(string time)
    {
        var parts = time.Split(':');
        var hours = int.Parse(parts[0], CultureInfo.InvariantCulture);
        var minutes = int.Parse(parts[1], CultureInfo.InvariantCulture);
        return hours * 60 + minutes;
    }

    // Small pop-up: the red button only turns on after the user types DELETE exactly.
    private bool ConfirmDeleteAll()
    {
        var input = new TextBox { Width = 300, Margin = new Thickness(0, 12, 0, 16), HorizontalAlignment = HorizontalAlignment.Left };

        var confirm = new Button
        {
            Content = "Delete everything",
            Padding = new Thickness(14, 7, 14, 7),
            IsEnabled = false,
            Background = new SolidColorBrush(Color.FromRgb(0xD6, 0x45, 0x50)),
            Foreground = Brushes.White,
            BorderThickness = new Thickness(0),
            Cursor = System.Windows.Input.Cursors.Hand
        };
        var cancel = new Button
        {
            Content = "Cancel",
            Padding = new Thickness(14, 7, 14, 7),
            Margin = new Thickness(0, 0, 8, 0),
            IsCancel = true,
            BorderThickness = new Thickness(0),
            Cursor = System.Windows.Input.Cursors.Hand
        };

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        buttons.Children.Add(cancel);
        buttons.Children.Add(confirm);

        var panel = new StackPanel { Margin = new Thickness(20) };
        panel.Children.Add(new TextBlock
        {
            Text = "This permanently deletes all subjects, timetable entries, attendance records and settings.",
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 340
        });
        panel.Children.Add(new TextBlock
        {
            Text = "Type DELETE to confirm.",
            Foreground = (Brush)FindResource("SecondaryTextBrush"),
            Margin = new Thickness(0, 12, 0, 0)
        });
        panel.Children.Add(input);
        panel.Children.Add(buttons);

        var dialog = new Window
        {
            Title = "Delete all data",
            Content = panel,
            SizeToContent = SizeToContent.WidthAndHeight,
            ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = Window.GetWindow(this),
            Background = (Brush)FindResource("SurfaceBackgroundBrush")
        };

        input.TextChanged += (_, _) => confirm.IsEnabled = string.Equals(input.Text.Trim(), "DELETE", StringComparison.Ordinal);
        confirm.Click += (_, _) => dialog.DialogResult = true;

        return dialog.ShowDialog() == true;
    }
}