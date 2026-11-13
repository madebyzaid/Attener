using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Attener.Features.Attendance;
using Attener.Features.Settings;
using Attener.Features.Subjects;
using Attener.Infrastructure.Database;
using Microsoft.Data.Sqlite;

namespace Attener.Views;

public partial class SettingsView : UserControl
{
    private readonly SemesterSettingsRepository _settings = new(DatabaseLocation.GetPath());
    private readonly AttendanceRepository _attendance = new(DatabaseLocation.GetPath());
    private readonly DataResetRepository _reset = new(DatabaseLocation.GetPath());
    private readonly AttendanceTargetRepository _target = new(DatabaseLocation.GetPath());
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
        TargetBox.Text = _target.GetTarget().ToString(CultureInfo.InvariantCulture);
        TargetMessage.Text = "";
    }

    private void SaveTarget_Click(object sender, RoutedEventArgs e)
    {
        if (!int.TryParse(TargetBox.Text.Trim().TrimEnd('%'), NumberStyles.None, CultureInfo.InvariantCulture, out var percent)
            || percent < AttendanceTargetRepository.MinTarget || percent > AttendanceTargetRepository.MaxTarget)
        {
            MessageBox.Show(
                $"Enter a whole number between {AttendanceTargetRepository.MinTarget} and {AttendanceTargetRepository.MaxTarget}.",
                "Check the target", MessageBoxButton.OK, MessageBoxImage.Information);
            TargetBox.Text = _target.GetTarget().ToString(CultureInfo.InvariantCulture);
            return;
        }

        _target.SaveTarget(percent);
        TargetBox.Text = percent.ToString(CultureInfo.InvariantCulture);
        TargetMessage.Text = $"Attendance target saved: {percent}%.";
    }

    private void TargetBox_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == System.Windows.Input.Key.Enter)
            SaveTarget_Click(sender, e);
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

        // Semester starts Monday 3 Aug 2026. Records are created up to today, but never past 18 Nov 2026;
        // classes after today stay as scheduled classes only, exactly as the real app treats them.
        var semesterStart = new DateOnly(2026, 8, 3);
        var lastDay = DateOnly.FromDateTime(DateTime.Today);
        if (lastDay > new DateOnly(2026, 11, 18))
            lastDay = new DateOnly(2026, 11, 18);

        var subjects = new[]
        {
            (Name: "AM101", Types: new[] { SubjectType.Lecture, SubjectType.Tutorial }),
            (Name: "ME105", Types: new[] { SubjectType.Lecture, SubjectType.Practical }),
            (Name: "CO101", Types: new[] { SubjectType.Lecture, SubjectType.Practical }),
            (Name: "EC101", Types: new[] { SubjectType.Lecture, SubjectType.Practical }),
            (Name: "CS103", Types: new[] { SubjectType.Lecture, SubjectType.Practical }),
            (Name: "AEC18", Types: new[] { SubjectType.Lecture })
        };

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

        foreach (var subjectInfo in subjects)
        {
            // All subjects are active in this data set.
            var isArchived = 0;
            string? archivedOn = null;

            using var insertSubject = connection.CreateCommand();
            insertSubject.Transaction = transaction;
            insertSubject.CommandText = "INSERT INTO Subjects (Name, IsArchived, ArchivedOn) VALUES ($name, $isArchived, $archivedOn);";
            insertSubject.Parameters.AddWithValue("$name", subjectInfo.Name);
            insertSubject.Parameters.AddWithValue("$isArchived", isArchived);
            insertSubject.Parameters.AddWithValue("$archivedOn", archivedOn ?? (object)DBNull.Value);
            insertSubject.ExecuteNonQuery();

            using var getId = connection.CreateCommand();
            getId.Transaction = transaction;
            getId.CommandText = "SELECT last_insert_rowid();";
            var lastId = getId.ExecuteScalar();
            var subjectId = Convert.ToInt64(lastId);
            subjectIdByName[subjectInfo.Name] = subjectId;

            foreach (var type in subjectInfo.Types)
            {
                using var insertType = connection.CreateCommand();
                insertType.Transaction = transaction;
                insertType.CommandText = "INSERT INTO SubjectClassTypes (SubjectId, Type) VALUES ($subjectId, $type);";
                insertType.Parameters.AddWithValue("$subjectId", subjectId);
                insertType.Parameters.AddWithValue("$type", type.ToString());
                insertType.ExecuteNonQuery();
            }
        }

        // Four different weekly timetables, one per month. DayOfWeek: 0 = Sunday, 1 = Monday ... 6 = Saturday.
        // Every subject appears in every month; every class falls between 08:00 and 18:00.
        // EffectiveTo is the first day of the next timetable (exclusive); the last one has no end.
        var weeklySessions = new[]
        {
            // ---- August (3 Aug - 31 Aug) ----
            new SeederSession("AM101L", 1, "09:00", "11:00", "2026-08-03", "2026-09-01"),
            new SeederSession("CO101P", 1, "14:00", "16:00", "2026-08-03", "2026-09-01"),
            new SeederSession("AEC18",  2, "08:00", "09:00", "2026-08-03", "2026-09-01"),
            new SeederSession("ME105L", 2, "10:00", "12:00", "2026-08-03", "2026-09-01"),
            new SeederSession("EC101L", 2, "14:00", "16:00", "2026-08-03", "2026-09-01"),
            new SeederSession("CS103L", 3, "09:00", "11:00", "2026-08-03", "2026-09-01"),
            new SeederSession("AM101T", 3, "11:00", "12:00", "2026-08-03", "2026-09-01"),
            new SeederSession("ME105P", 3, "13:00", "15:00", "2026-08-03", "2026-09-01"),
            new SeederSession("CO101L", 4, "10:00", "12:00", "2026-08-03", "2026-09-01"),
            new SeederSession("EC101P", 4, "14:00", "16:00", "2026-08-03", "2026-09-01"),
            new SeederSession("CS103P", 5, "09:00", "11:00", "2026-08-03", "2026-09-01"),
            new SeederSession("AEC18",  5, "11:00", "12:00", "2026-08-03", "2026-09-01"),

            // ---- September ----
            new SeederSession("CO101L", 1, "08:00", "10:00", "2026-09-01", "2026-10-01"),
            new SeederSession("EC101P", 1, "11:00", "13:00", "2026-09-01", "2026-10-01"),
            new SeederSession("AM101T", 1, "15:00", "16:00", "2026-09-01", "2026-10-01"),
            new SeederSession("CS103L", 2, "09:00", "11:00", "2026-09-01", "2026-10-01"),
            new SeederSession("AM101L", 2, "13:00", "15:00", "2026-09-01", "2026-10-01"),
            new SeederSession("ME105L", 3, "10:00", "12:00", "2026-09-01", "2026-10-01"),
            new SeederSession("CS103P", 3, "14:00", "16:00", "2026-09-01", "2026-10-01"),
            new SeederSession("AEC18",  3, "16:00", "17:00", "2026-09-01", "2026-10-01"),
            new SeederSession("EC101L", 4, "09:00", "11:00", "2026-09-01", "2026-10-01"),
            new SeederSession("CO101P", 4, "11:00", "13:00", "2026-09-01", "2026-10-01"),
            new SeederSession("ME105P", 4, "15:00", "17:00", "2026-09-01", "2026-10-01"),
            new SeederSession("AEC18",  5, "08:00", "09:00", "2026-09-01", "2026-10-01"),
            new SeederSession("AM101T", 5, "12:00", "13:00", "2026-09-01", "2026-10-01"),

            // ---- October ----
            new SeederSession("ME105P", 1, "10:00", "12:00", "2026-10-01", "2026-11-01"),
            new SeederSession("AEC18",  1, "13:00", "14:00", "2026-10-01", "2026-11-01"),
            new SeederSession("CS103L", 1, "15:00", "17:00", "2026-10-01", "2026-11-01"),
            new SeederSession("AM101L", 2, "08:00", "10:00", "2026-10-01", "2026-11-01"),
            new SeederSession("CO101P", 2, "11:00", "13:00", "2026-10-01", "2026-11-01"),
            new SeederSession("EC101L", 2, "14:00", "16:00", "2026-10-01", "2026-11-01"),
            new SeederSession("AM101T", 3, "09:00", "10:00", "2026-10-01", "2026-11-01"),
            new SeederSession("CO101L", 3, "11:00", "13:00", "2026-10-01", "2026-11-01"),
            new SeederSession("ME105L", 3, "14:00", "16:00", "2026-10-01", "2026-11-01"),
            new SeederSession("CS103P", 4, "10:00", "12:00", "2026-10-01", "2026-11-01"),
            new SeederSession("EC101P", 4, "13:00", "15:00", "2026-10-01", "2026-11-01"),
            new SeederSession("AEC18",  5, "09:00", "10:00", "2026-10-01", "2026-11-01"),
            new SeederSession("AM101T", 5, "14:00", "15:00", "2026-10-01", "2026-11-01"),

            // ---- November onward ----
            new SeederSession("CS103P", 1, "09:00", "11:00", "2026-11-01", null),
            new SeederSession("AM101T", 1, "11:00", "12:00", "2026-11-01", null),
            new SeederSession("EC101L", 1, "14:00", "16:00", "2026-11-01", null),
            new SeederSession("ME105L", 2, "08:00", "10:00", "2026-11-01", null),
            new SeederSession("CO101P", 2, "10:00", "12:00", "2026-11-01", null),
            new SeederSession("AEC18",  2, "15:00", "16:00", "2026-11-01", null),
            new SeederSession("AM101L", 3, "10:00", "12:00", "2026-11-01", null),
            new SeederSession("EC101P", 3, "13:00", "15:00", "2026-11-01", null),
            new SeederSession("CS103L", 3, "15:00", "17:00", "2026-11-01", null),
            new SeederSession("CO101L", 4, "09:00", "11:00", "2026-11-01", null),
            new SeederSession("ME105P", 4, "11:00", "13:00", "2026-11-01", null),
            new SeederSession("AM101T", 4, "16:00", "17:00", "2026-11-01", null),
            new SeederSession("AEC18",  5, "08:00", "09:00", "2026-11-01", null),
            new SeederSession("CO101P", 5, "14:00", "16:00", "2026-11-01", null)
        };

        foreach (var session in weeklySessions)
        {
            var classType = ResolveSeedClassType(session.Subject);
            using var insertSession = connection.CreateCommand();
            insertSession.Transaction = transaction;
            insertSession.CommandText = "INSERT INTO WeeklySessions (SubjectId, ClassType, DayOfWeek, StartTime, EndTime, EffectiveFrom, EffectiveTo, EndedByEndDate) VALUES ($subjectId, $classType, $dayOfWeek, $startTime, $endTime, $effectiveFrom, $effectiveTo, 0);";
            insertSession.Parameters.AddWithValue("$subjectId", subjectIdByName[classType.SubjectName]);
            insertSession.Parameters.AddWithValue("$classType", classType.Type.ToString());
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
                ? lastDay.AddDays(1)
                : DateOnly.ParseExact(session.EffectiveTo, "yyyy-MM-dd", CultureInfo.InvariantCulture);

            for (var date = effectiveFrom; date < effectiveTo && date <= lastDay; date = date.AddDays(1))
            {
                if ((int)date.DayOfWeek == session.DayOfWeek)
                    occurrences.Add((session, date));
            }
        }

        // Repeatable mix (fixed seed): about 78% Present, 12% Absent, 5% Cancelled, 5% Not Marked.
        // The last four days are always Not Marked, so the Pending tab has something to show.
        var random = new Random(42);
        var statuses = occurrences.Select(item =>
        {
            var roll = random.NextDouble();
            if (item.Date > lastDay.AddDays(-4)) return "Not Marked";
            return roll < 0.78 ? "Present"
                : roll < 0.90 ? "Absent"
                : roll < 0.95 ? "Cancelled"
                : "Not Marked";
        }).ToArray();

        for (var i = 0; i < occurrences.Count; i++)
        {
            var (session, date) = occurrences[i];
            var classType = ResolveSeedClassType(session.Subject);
            using var insertRecord = connection.CreateCommand();
            insertRecord.Transaction = transaction;
            insertRecord.CommandText = "INSERT INTO AttendanceRecords (SubjectId, ClassType, AttendanceDate, Status, ScheduledStart, ScheduledEnd, ScheduledDurationMinutes, ActualDurationMinutes, IsManual) VALUES ($subjectId, $classType, $attendanceDate, $status, $scheduledStart, $scheduledEnd, $scheduledDurationMinutes, NULL, 0);";
            insertRecord.Parameters.AddWithValue("$subjectId", subjectIdByName[classType.SubjectName]);
            insertRecord.Parameters.AddWithValue("$classType", classType.Type.ToString());
            insertRecord.Parameters.AddWithValue("$attendanceDate", date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
            insertRecord.Parameters.AddWithValue("$status", statuses[i]);
            insertRecord.Parameters.AddWithValue("$scheduledStart", session.Start);
            insertRecord.Parameters.AddWithValue("$scheduledEnd", session.End);
            insertRecord.Parameters.AddWithValue("$scheduledDurationMinutes", GetMinutes(session.End) - GetMinutes(session.Start));
            insertRecord.ExecuteNonQuery();
        }

        // Hand-added entries are always Present, Absent or Cancelled, as in the real "Add entry" dialog.
        var manualEntries = new[]
        {
            (Subject: "CS103L", Date: new DateOnly(2026, 8, 16), Status: "Present", Duration: 45),
            (Subject: "AM101L", Date: new DateOnly(2026, 9, 20), Status: "Absent", Duration: 60),
            (Subject: "CO101P", Date: new DateOnly(2026, 10, 25), Status: "Cancelled", Duration: 90),
            (Subject: "ME105L", Date: new DateOnly(2026, 11, 8), Status: "Present", Duration: 75)
        };

        foreach (var entry in manualEntries)
        {
            var classType = ResolveSeedClassType(entry.Subject);
            using var insertManual = connection.CreateCommand();
            insertManual.Transaction = transaction;
            insertManual.CommandText = "INSERT INTO AttendanceRecords (SubjectId, ClassType, AttendanceDate, Status, ScheduledStart, ScheduledEnd, ScheduledDurationMinutes, ActualDurationMinutes, IsManual) VALUES ($subjectId, $classType, $attendanceDate, $status, NULL, NULL, $scheduledDurationMinutes, NULL, 1);";
            insertManual.Parameters.AddWithValue("$subjectId", subjectIdByName[classType.SubjectName]);
            insertManual.Parameters.AddWithValue("$classType", classType.Type.ToString());
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

    private static (string SubjectName, SubjectType Type) ResolveSeedClassType(string key)
    {
        if (key == "AEC18")
            return (key, SubjectType.Lecture);

        var type = key[^1] switch
        {
            'L' => SubjectType.Lecture,
            'T' => SubjectType.Tutorial,
            'P' => SubjectType.Practical,
            _ => throw new InvalidOperationException($"Unknown seeded class type in {key}.")
        };
        return (key[..^1], type);
    }

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
            Background = (Brush)FindResource("DangerBrush"),
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