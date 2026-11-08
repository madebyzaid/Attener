using System.Windows;
using System.Windows.Controls;
using AttendanceApp.Features.Attendance;
using AttendanceApp.Features.Statistics;
using AttendanceApp.Features.Subjects;
using AttendanceApp.Infrastructure.Database;

namespace AttendanceApp.Views;

public partial class OverviewView : UserControl
{
    private readonly SemesterSettingsRepository _settings = new(DatabaseLocation.GetPath());
    private readonly AttendanceRepository _attendance = new(DatabaseLocation.GetPath());
    private readonly StatisticsRepository _statistics = new(DatabaseLocation.GetPath());
    private readonly SubjectRepository _subjects = new(DatabaseLocation.GetPath());

    public OverviewView() { InitializeComponent(); var startDate = _settings.GetSemesterStartDate(); if (startDate is not null) SemesterStartPicker.SelectedDate = startDate.Value.ToDateTime(TimeOnly.MinValue); RefreshData(); }
    public void RefreshData()
    {
        SubjectCountText.Text = _subjects.GetSubjects(false).Count.ToString();
        var start = _settings.GetSemesterStartDate();
        if (start is null) { AttendanceSummaryText.Text = "Set semester start date"; return; }
        var summaries = _statistics.GetSubjectSummaries(start.Value, DateOnly.FromDateTime(DateTime.Today));
        var attended = summaries.Sum(x => x.AttendedMinutes); var counted = summaries.Sum(x => x.CountedMinutes);
        AttendanceSummaryText.Text = counted == 0 ? "No marked classes yet" : $"{(double)attended / counted:P0} · {attended / 60.0:0.#}/{counted / 60.0:0.#} hrs";
    }
    private void GenerateRecords_Click(object sender, RoutedEventArgs e)
    {
        if (SemesterStartPicker.SelectedDate is not DateTime selectedDate) { MessageBox.Show("Choose the semester start date first.", "Date required", MessageBoxButton.OK, MessageBoxImage.Information); return; }
        var startDate = DateOnly.FromDateTime(selectedDate); _settings.SaveSemesterStartDate(startDate); var today = DateOnly.FromDateTime(DateTime.Today); var created = _attendance.GenerateMissingBetween(startDate, today);
        GenerationMessage.Text = startDate > today ? "The semester start is in the future. Historical records will be generated when that date arrives." : $"Semester start saved. {created} pending class record(s) were created; existing attendance was preserved."; RefreshData();
    }
}
