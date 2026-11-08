using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using AttendanceApp.Features.Subjects;
using AttendanceApp.Features.Timetable;
using AttendanceApp.Infrastructure.Database;

namespace AttendanceApp.Views;

public partial class TimetableView : UserControl
{
    private readonly SubjectRepository _subjects = new(DatabaseLocation.GetPath());
    private readonly TimetableRepository _timetable = new(DatabaseLocation.GetPath());

    public TimetableView()
    {
        InitializeComponent();
        SubjectCombo.ItemsSource = _subjects.GetSubjects(includeArchived: false);
        DayCombo.ItemsSource = Enum.GetValues<DayOfWeek>();
        DayCombo.SelectedItem = DayOfWeek.Monday;
        EffectiveDatePicker.SelectedDate = DateTime.Today;
        LoadSessions();
    }

    public void RefreshData()
    {
        var selectedSubjectId = SubjectCombo.SelectedValue;
        var subjects = _subjects.GetSubjects(includeArchived: false);
        SubjectCombo.ItemsSource = subjects;
        if (selectedSubjectId is not null && subjects.Any(subject => subject.Id.Equals(selectedSubjectId)))
            SubjectCombo.SelectedValue = selectedSubjectId;
        LoadSessions();
    }

    private void LoadSessions()
    {
        if (EffectiveDatePicker.SelectedDate is not DateTime selectedDate)
        {
            SessionsList.ItemsSource = Array.Empty<TimetableSessionRow>();
            EmptyStateText.Visibility = Visibility.Visible;
            return;
        }

        var date = DateOnly.FromDateTime(selectedDate);
        var rows = _timetable.GetWeeklySessions(date)
            .Select(session => new TimetableSessionRow(
                session,
                Enum.IsDefined((DayOfWeek)session.DayOfWeek)
                    ? ((DayOfWeek)session.DayOfWeek).ToString()
                    : "Unknown",
                $"{session.StartTime}–{session.EndTime}",
                session.EffectiveTo is null
                    ? $"Effective {session.EffectiveFrom} onward"
                    : $"Effective {session.EffectiveFrom} to {session.EffectiveTo}"))
            .ToArray();

        SessionsList.ItemsSource = rows;
        EmptyStateText.Visibility = rows.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        ScheduleHeading.Text = $"Weekly sessions in effect on {selectedDate:dd MMM yyyy}";
    }

    private void SaveSession_Click(object sender, RoutedEventArgs e)
    {
        if (SubjectCombo.SelectedValue is not long subjectId ||
            DayCombo.SelectedItem is not DayOfWeek day ||
            EffectiveDatePicker.SelectedDate is not DateTime effectiveDate)
        {
            MessageBox.Show("Choose a subject, day, and effective date first.", "Details required",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (!TimeOnly.TryParseExact(StartTimeBox.Text.Trim(), "HH:mm", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var start) ||
            !TimeOnly.TryParseExact(EndTimeBox.Text.Trim(), "HH:mm", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var end))
        {
            MessageBox.Show("Enter class times in 24-hour format, such as 09:00.", "Check the time",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (end <= start)
        {
            MessageBox.Show("The end time must be later than the start time.", "Check the time",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        try
        {
            _timetable.SaveWeeklySession(subjectId, (int)day, start, end,
                DateOnly.FromDateTime(effectiveDate));
            LoadSessions();
        }
        catch (Exception ex) when (ex is Microsoft.Data.Sqlite.SqliteException or InvalidOperationException)
        {
            MessageBox.Show($"The timetable could not be saved. {ex.Message}", "Save failed",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void RemoveSession_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: TimetableSessionRow row } ||
            EffectiveDatePicker.SelectedDate is not DateTime effectiveDate)
        {
            return;
        }

        var result = MessageBox.Show(
            $"Remove {row.Session.SubjectName} on {row.DayName} from {effectiveDate:dd MMM yyyy} onward? Earlier timetable history will be preserved.",
            "Remove weekly session", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (result != MessageBoxResult.Yes)
            return;

        _timetable.RemoveWeeklySession(row.Session.SubjectId, row.Session.DayOfWeek,
            DateOnly.FromDateTime(effectiveDate));
        LoadSessions();
    }

    private void EffectiveDatePicker_SelectedDateChanged(object sender, SelectionChangedEventArgs e)
    {
        if (IsLoaded)
            LoadSessions();
    }

    private sealed record TimetableSessionRow(
        WeeklySessionSummary Session,
        string DayName,
        string TimeRange,
        string EffectiveDescription);
}
