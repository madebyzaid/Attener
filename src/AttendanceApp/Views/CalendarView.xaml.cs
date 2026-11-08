using System.Windows;
using System.Windows.Controls;
using AttendanceApp.Domain;
using AttendanceApp.Features.Attendance;
using AttendanceApp.Features.Subjects;
using AttendanceApp.Features.Timetable;
using AttendanceApp.Infrastructure.Database;

namespace AttendanceApp.Views;

public partial class CalendarView : UserControl
{
    private readonly TimetableRepository _timetable = new(DatabaseLocation.GetPath());
    private readonly AttendanceRepository _attendance = new(DatabaseLocation.GetPath());
    private readonly SemesterSettingsRepository _settings = new(DatabaseLocation.GetPath());
    private readonly SubjectRepository _subjects = new(DatabaseLocation.GetPath());

    public CalendarView()
    {
        InitializeComponent();
        var filters = new List<CalendarSubjectFilter> { new(null, "All subjects") };
        filters.AddRange(_subjects.GetSubjects(includeArchived: false)
            .Select(subject => new CalendarSubjectFilter(subject.Id, subject.Name)));
        filters.AddRange(_subjects.GetSubjects(includeArchived: true)
            .Select(subject => new CalendarSubjectFilter(subject.Id, $"{subject.Name} · Archived")));
        SubjectFilter.ItemsSource = filters;
        SubjectFilter.SelectedIndex = 0;
        MonthCalendar.SelectedDate = DateTime.Today;
        LoadSelectedDate();
    }

    public void RefreshData() => LoadSelectedDate();

    private void MonthCalendar_SelectedDatesChanged(object sender, SelectionChangedEventArgs e)
    {
        LoadSelectedDate();
    }

    private void SubjectFilter_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (IsLoaded)
            LoadSelectedDate();
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
        var stored = _attendance.GetAttendanceForDate(date, allowUnmarked: true);
        var storedBySubject = stored.ToDictionary(entry => entry.SubjectId);
        var rows = new List<CalendarSessionRow>();
        var dayOfWeek = (int)selectedDate.DayOfWeek;

        foreach (var session in _timetable.GetWeeklySessions(date)
                     .Where(item => item.DayOfWeek == dayOfWeek &&
                                    (selectedSubject is null || item.SubjectId == selectedSubject)))
        {
            storedBySubject.TryGetValue(session.SubjectId, out var record);
            rows.Add(new CalendarSessionRow(
                session.SubjectName,
                $"{session.StartTime}–{session.EndTime}",
                record is null ? "Scheduled class" : StatusLabel(record.Status),
                record is null ? "Scheduled" : StatusLabel(record.Status)));
            storedBySubject.Remove(session.SubjectId);
        }

        foreach (var record in storedBySubject.Values.Where(record =>
                     selectedSubject is null || record.SubjectId == selectedSubject))
        {
            rows.Add(new CalendarSessionRow(
                record.SubjectName,
                record.ScheduledStart is null || record.ScheduledEnd is null
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

    private sealed record CalendarSubjectFilter(long? SubjectId, string Label);
    private sealed record CalendarSessionRow(
        string SubjectName, string TimeRange, string StatusText, string StatusLabel);
}
