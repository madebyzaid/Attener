using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using Attener.Domain;
using Attener.Features.Attendance;
using Attener.Features.Subjects;
using Attener.Features.Timetable;
using Attener.Infrastructure.Database;

namespace Attener.Views;

public partial class CalendarView : UserControl
{
    private readonly TimetableRepository _timetable = new(DatabaseLocation.GetPath());
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

        // A subject can have several classes on one day, so saved records are matched to
        // timetable classes by subject + start time (not by subject alone).
        var storedByClass = new Dictionary<(long SubjectId, string? Start), AttendanceEntry>();
        foreach (var entry in _attendance.GetAttendanceForDate(date, allowUnmarked: true))
            storedByClass[(entry.SubjectId, entry.IsManual ? $"manual-{entry.Id}" : entry.ScheduledStart)] = entry;

        var rows = new List<CalendarSessionRow>();
        var dayOfWeek = (int)selectedDate.DayOfWeek;

        foreach (var session in _timetable.GetWeeklySessions(date)
                     .Where(item => item.DayOfWeek == dayOfWeek &&
                                    (selectedSubject is null || item.SubjectId == selectedSubject)))
        {
            var key = (session.SubjectId, (string?)session.StartTime);
            storedByClass.TryGetValue(key, out var record);
            rows.Add(new CalendarSessionRow(
                session.SubjectName,
                $"{session.StartTime}–{session.EndTime}",
                record is null ? "Scheduled class" : StatusLabel(record.Status),
                record is null ? "Scheduled" : StatusLabel(record.Status)));
            storedByClass.Remove(key);
        }

        foreach (var record in storedByClass.Values.Where(record =>
                     selectedSubject is null || record.SubjectId == selectedSubject))
        {
            rows.Add(new CalendarSessionRow(
                record.SubjectName,
                record.IsManual
                    ? $"Manual · {(record.ScheduledDurationMinutes / 60m).ToString("0.##", CultureInfo.InvariantCulture)} h"
                    : record.ScheduledStart is null || record.ScheduledEnd is null
                        ? "Time unavailable"
                        : $"{record.ScheduledStart}–{record.ScheduledEnd}",
                StatusLabel(record.Status), StatusLabel(record.Status)));
        }

        SessionsList.ItemsSource = rows.OrderBy(row => row.TimeRange).ToArray();
        EmptyStateText.Visibility = rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private static string StatusLabel(AttendanceStatus? status) => status switch
    {
        AttendanceStatus.Present => "Present",
        AttendanceStatus.Absent => "Absent",
        AttendanceStatus.Cancelled => "Cancelled",
        AttendanceStatus.NotMarked => "Not Marked",
        AttendanceStatus.NoClass => "No Class",
        _ => "Scheduled"
    };

    private sealed record CalendarSubjectFilter(long? SubjectId, string Label) { public override string ToString() => Label; }
    private sealed record CalendarSessionRow(
        string SubjectName, string TimeRange, string StatusText, string StatusLabel);
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