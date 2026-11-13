using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using Attener.Features.Attendance;
using Attener.Features.Settings;
using Attener.Features.Statistics;
using Attener.Infrastructure.Database;

namespace Attener.Views;

public partial class StatisticsView : UserControl
{
    private readonly AttendanceTargetRepository _target = new(DatabaseLocation.GetPath());
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
            SubjectStatsList.ItemsSource = Array.Empty<SubjectStatRow>();
            EmptyStateText.Text = "Choose a semester start date in Settings to view statistics.";
            EmptyStateText.Visibility = Visibility.Visible;
            return;
        }

        var today = DateOnly.FromDateTime(DateTime.Today);
        DateRangeText.Text = $"{semesterStart:dd MMM yyyy} to {today:dd MMM yyyy}";
        var summaries = _statistics.GetSubjectSummaries(semesterStart.Value, today);

        var target = _target.GetTarget();
        var rows = summaries.Select(summary =>
        {
            var percentage = summary.CountedMinutes == 0
                ? 0m
                : summary.AttendedMinutes * 100m / summary.CountedMinutes;
            var subjectName = summary.IsArchived ? $"{summary.SubjectName} · Archived" : summary.SubjectName;
            return new SubjectStatRow(BuildTargetNote(summary, target), PickTargetBrush(summary, target),
                new GridLength(target, GridUnitType.Star), new GridLength(100 - target, GridUnitType.Star),
                subjectName,
                $"{FormatHours(summary.AttendedMinutes)} attended of {FormatHours(summary.CountedMinutes)} counted",
                $"{percentage:0.0}%", (double)percentage);
        }).ToArray();

        SubjectStatsList.ItemsSource = rows;
        EmptyStateText.Text = "No attendance hours have been marked yet.";
        EmptyStateText.Visibility = rows.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private static string FormatHours(int minutes) =>
        $"{(minutes / 60m).ToString("0.##", CultureInfo.InvariantCulture)} h";

    private static readonly System.Windows.Media.Brush OnTrackBrush =
        new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x5B, 0xD6, 0x8A));
    private static readonly System.Windows.Media.Brush ShortageBrush =
        new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xF2, 0x6D, 0x78));

    // Whole-minute maths, so there is no rounding drift:
    //   at/above target: hours you can still miss = floor(attended / target - counted)
    //   below target:    hours to attend in a row = ceil((target * counted - attended) / (1 - target))
    private static string BuildTargetNote(SubjectAttendanceSummary summary, int target)
    {
        long attended = summary.AttendedMinutes;
        long counted = summary.CountedMinutes;
        if (counted == 0)
            return "";

        if (attended * 100 >= target * counted)
        {
            var canMiss = attended * 100 / target - counted;
            return canMiss <= 0
                ? $"At {target}% right now: you can't miss any more hours"
                : $"Can safely miss {FormatHours((int)canMiss)}";
        }

        var needed = (target * counted - attended * 100 + (100 - target) - 1) / (100 - target);
        return $"Attend the next {FormatHours((int)needed)} to reach {target}%";
    }

    private static System.Windows.Media.Brush PickTargetBrush(SubjectAttendanceSummary summary, int target) =>
        summary.CountedMinutes == 0 || summary.AttendedMinutes * 100L >= target * (long)summary.CountedMinutes
            ? OnTrackBrush
            : ShortageBrush;

    private sealed record SubjectStatRow(
        string TargetNote, System.Windows.Media.Brush TargetBrush, GridLength TargetLeft, GridLength TargetRight,
        string SubjectName, string HoursSummary, string PercentageText, double PercentageValue);
}

