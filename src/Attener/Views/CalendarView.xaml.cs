using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using Attener.Domain;
using Attener.Features.Attendance;
using Attener.Infrastructure.Dialogs;
using Attener.Features.Subjects;
using Attener.Features.Timetable;
using Attener.Infrastructure.Database;

namespace Attener.Views;

public partial class CalendarView : UserControl
{
    private readonly AttendanceRepository _attendance = new(DatabaseLocation.GetPath());
    private readonly CalendarStatusRepository _calendarStatuses = new(DatabaseLocation.GetPath());
    private readonly SemesterSettingsRepository _settings = new(DatabaseLocation.GetPath());
    private readonly SubjectRepository _subjects = new(DatabaseLocation.GetPath());

    public CalendarView()
    {
        InitializeComponent();
        LoadSubjectFilters(null);
        LoadDayColors();
        MonthCalendar.SelectedDate = DateTime.Today;
        LoadSelectedDate();
    }

    public void RefreshData()
    {
        var selectedSubjectId = (SubjectFilter.SelectedItem as CalendarSubjectFilter)?.SubjectId;
        LoadSubjectFilters(selectedSubjectId);
        LoadDayColors();
        LoadSelectedDate();
    }

    private void LoadSubjectFilters(long? selectedSubjectId)
    {
        var filters = new List<CalendarSubjectFilter> { new(null, "All subjects") };
        filters.AddRange(_subjects.GetSubjects(includeArchived: false)
            .Select(subject => new CalendarSubjectFilter(subject.Id, subject.Name)));
        filters.AddRange(_subjects.GetSubjects(includeArchived: true)
            .Select(subject => new CalendarSubjectFilter(subject.Id, $"{subject.Name} · Archived")));
        SubjectFilter.ItemsSource = filters;
        SubjectFilter.SelectedValue = selectedSubjectId;
        if (selectedSubjectId is null)
            SubjectFilter.SelectedIndex = 0;
    }

    // When one subject is chosen, give the calendar a date -> status map for it. Each day cell
    // reads that map (through the calendar's Tag) and colours itself. "All subjects" clears it.
    private void LoadDayColors()
    {
        var subjectId = (SubjectFilter.SelectedItem as CalendarSubjectFilter)?.SubjectId;
        MonthCalendar.Tag = subjectId is null
            ? null
            : _calendarStatuses.GetStatusesForSubject(subjectId.Value);
        LegendPanel.Visibility = subjectId is null ? Visibility.Collapsed : Visibility.Visible;
    }

    private void MonthCalendar_SelectedDatesChanged(object sender, SelectionChangedEventArgs e)
    {
        LoadSelectedDate();
    }

    private void SubjectFilter_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (IsLoaded)
        {
            LoadDayColors();
            LoadSelectedDate();
        }
    }

    private void LoadSelectedDate()
    {
        if (MonthCalendar.SelectedDate is not DateTime selectedDate)
            return;

        SelectedDateHeading.Text = selectedDate.ToString("dddd, d MMMM yyyy");
        var date = DateOnly.FromDateTime(selectedDate);
        var today = DateOnly.FromDateTime(DateTime.Today);
        var semesterStart = _settings.GetSemesterStartDate();
        if (date <= today && semesterStart is not null && date >= semesterStart.Value)
            _attendance.GenerateMissingBetween(date, date);

        var selectedSubject = (SubjectFilter.SelectedItem as CalendarSubjectFilter)?.SubjectId;

        var rows = _attendance.GetAttendanceForDate(date, allowUnmarked: true)
            .Where(entry => selectedSubject is null || entry.SubjectId == selectedSubject)
            .Select(entry => new CalendarSessionRow(entry,
                entry.IsManual
                    ? $"Manual · {(entry.ScheduledDurationMinutes / 60m).ToString("0.##", CultureInfo.InvariantCulture)} h"
                    : entry.ScheduledStart is null || entry.ScheduledEnd is null
                        ? "Time unavailable"
                        : $"{entry.ScheduledStart}–{entry.ScheduledEnd}",
                entry.IsManual || date > today || (semesterStart is not null && date >= semesterStart.Value)))
            .OrderBy(row => row.TimeRange).ToArray();

        SessionsList.ItemsSource = rows;
        EmptyStateText.Visibility = rows.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void Present_Click(object sender, RoutedEventArgs e) => SetStatus(sender, AttendanceStatus.Present);
    private void Absent_Click(object sender, RoutedEventArgs e) => SetStatus(sender, AttendanceStatus.Absent);
    private void Cancelled_Click(object sender, RoutedEventArgs e) => SetStatus(sender, AttendanceStatus.Cancelled);

    private void SetStatus(object sender, AttendanceStatus clicked)
    {
        if (sender is not FrameworkElement { DataContext: CalendarSessionRow row }) return;
        _attendance.SaveAttendance(row.Entry,
            row.SelectedStatus == clicked ? AttendanceStatus.NotMarked : clicked);
        RefreshAttendanceViews();
    }

    private void DeleteManual_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: CalendarSessionRow row } || !row.Entry.IsManual) return;
        var answer = InAppDialogService.Show(this, $"Delete this {row.Entry.SubjectName} entry for {row.Entry.AttendanceDate:dd MMM yyyy}? This cannot be undone.",
            "Delete entry", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No);
        if (answer != MessageBoxResult.Yes) return;
        _attendance.DeleteManualEntry(row.Entry.Id);
        RefreshAttendanceViews();
    }

    private void AddManual_Click(object sender, RoutedEventArgs e)
    {
        if (MonthCalendar.SelectedDate is not DateTime date) return;
        if (Application.Current.MainWindow is MainWindow window)
            window.ShowAddAttendanceEntry(date, lockDate: true);
        RefreshAttendanceViews();
    }

    private void RefreshAttendanceViews()
    {
        LoadSelectedDate();
        LoadDayColors();
    }

    private sealed record CalendarSubjectFilter(long? SubjectId, string Label) { public override string ToString() => Label; }
    private sealed class CalendarSessionRow
    {
        public AttendanceEntry Entry { get; }
        public string SubjectName => Entry.SubjectName;
        public string Professor => Entry.Professor;
        public string Venue => Entry.Venue;
        public bool HasProfessor => Entry.HasProfessor;
        public bool HasVenue => Entry.HasVenue;
        public string TimeRange { get; }
        public bool CanEdit { get; }
        public AttendanceStatus SelectedStatus => Entry.Status ?? AttendanceStatus.NotMarked;
        public bool IsPresent => SelectedStatus == AttendanceStatus.Present;
        public bool IsAbsent => SelectedStatus == AttendanceStatus.Absent;
        public bool IsCancelled => SelectedStatus == AttendanceStatus.Cancelled;
        public Visibility ManualVisibility => Entry.IsManual ? Visibility.Visible : Visibility.Collapsed;
        public Visibility DeleteVisibility => Entry.IsManual ? Visibility.Visible : Visibility.Collapsed;
        public CalendarSessionRow(AttendanceEntry entry, string timeRange, bool canEdit)
        { Entry = entry; TimeRange = timeRange; CanEdit = canEdit; }
    }
}

// Used by the Calendar page's day cells (see StatusDayButtonStyle in CalendarView.xaml).
// Input 1: the day's date. Input 2: the calendar's Tag, a date -> status map (or null).
// Output: that day's status word ("Present", "Absent", ...), or "" when there is none.
public sealed class DayStatusConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values.Length == 2
            && values[0] is DateTime date
            && values[1] is IReadOnlyDictionary<DateTime, string> statuses
            && statuses.TryGetValue(date.Date, out var status))
        {
            return status;
        }

        return "";
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
