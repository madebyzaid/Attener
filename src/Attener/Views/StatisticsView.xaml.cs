using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using Attener.Features.Attendance;
using Attener.Features.Statistics;
using Attener.Infrastructure.Database;

namespace Attener.Views;

public partial class StatisticsView : UserControl
{
    private readonly SemesterSettingsRepository _settings = new(DatabaseLocation.GetPath());
    private readonly StatisticsRepository _statistics = new(DatabaseLocation.GetPath());

    public StatisticsView()
    {
        InitializeComponent();
        RefreshData();
    }

    public void RefreshData()
    {
        var semesterStart = _settings.GetSemesterStartDate();
        if (semesterStart is null)
        {
            DateRangeText.Text = "Choose a semester start date to begin.";
            OverallPercentage.Text = "—";
            OverallHours.Text = "No counted hours yet";
            OverallProgress.Value = 0;
            SubjectStatsList.ItemsSource = Array.Empty<SubjectStatRow>();
            EmptyStateText.Text = "Choose a semester start date in Settings to view statistics.";
            EmptyStateText.Visibility = Visibility.Visible;
            return;
        }

        var today = DateOnly.FromDateTime(DateTime.Today);
        DateRangeText.Text = $"{semesterStart:dd MMM yyyy} to {today:dd MMM yyyy}";
        var summaries = _statistics.GetSubjectSummaries(semesterStart.Value, today);
        var countedMinutes = summaries.Sum(summary => summary.CountedMinutes);
        var attendedMinutes = summaries.Sum(summary => summary.AttendedMinutes);
        var overall = countedMinutes == 0 ? 0m : attendedMinutes * 100m / countedMinutes;

        OverallPercentage.Text = countedMinutes == 0 ? "—" : $"{overall:0.0}%";
        OverallHours.Text = countedMinutes == 0
            ? "No counted hours yet"
            : $"{FormatHours(attendedMinutes)} attended · {FormatHours(countedMinutes)} counted";
        OverallProgress.Value = countedMinutes == 0 ? 0 : (double)overall;

        var rows = summaries.Select(summary =>
        {
            var percentage = summary.CountedMinutes == 0
                ? 0m
                : summary.AttendedMinutes * 100m / summary.CountedMinutes;
            var subjectName = summary.IsArchived ? $"{summary.SubjectName} · Archived" : summary.SubjectName;
            return new SubjectStatRow(subjectName,
                $"{FormatHours(summary.AttendedMinutes)} attended of {FormatHours(summary.CountedMinutes)} counted",
                $"{percentage:0.0}%", (double)percentage);
        }).ToArray();

        SubjectStatsList.ItemsSource = rows;
        EmptyStateText.Text = "No attendance hours have been marked yet.";
        EmptyStateText.Visibility = rows.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private static string FormatHours(int minutes) =>
        $"{(minutes / 60m).ToString("0.##", CultureInfo.InvariantCulture)} h";

    private sealed record SubjectStatRow(
        string SubjectName, string HoursSummary, string PercentageText, double PercentageValue);
}

