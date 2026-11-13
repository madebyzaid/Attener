using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
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
        PageHeading.Text = "Statistics";
        BackButton.Visibility = Visibility.Collapsed;
        SubjectStatsList.Visibility = Visibility.Visible;
        ClassTypeStatsList.Visibility = Visibility.Collapsed;
        ClassTypeStatsList.ItemsSource = Array.Empty<SubjectStatRow>();

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
        var target = _target.GetTarget();
        var rows = _statistics.GetSubjectSummaries(semesterStart.Value, today)
            .Select(summary => CreateRow(summary.SubjectId,
                summary.IsArchived ? $"{summary.SubjectName} · Archived" : summary.SubjectName,
                "", "", null, summary.AttendedMinutes,
                summary.CountedMinutes, target, isClickable: true))
            .ToArray();

        SubjectStatsList.ItemsSource = rows;
        EmptyStateText.Text = "No attendance hours have been marked yet.";
        EmptyStateText.Visibility = rows.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void SubjectStat_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Border { DataContext: SubjectStatRow { IsClickable: true } row })
            return;

        var semesterStart = _settings.GetSemesterStartDate();
        if (semesterStart is null)
            return;

        PageHeading.Text = row.SubjectName;
        BackButton.Visibility = Visibility.Visible;
        SubjectStatsList.Visibility = Visibility.Collapsed;
        ClassTypeStatsList.Visibility = Visibility.Visible;

        var target = _target.GetTarget();
        var today = DateOnly.FromDateTime(DateTime.Today);
        var typeRows = _statistics.GetClassTypeSummaries(row.SubjectId, semesterStart.Value, today)
            .Select(summary => CreateRow(row.SubjectId, row.SubjectName, summary.Venue, summary.Professor,
                summary.Type.ToString(), summary.AttendedMinutes, summary.CountedMinutes, target,
                isClickable: false))
            .ToArray();

        ClassTypeStatsList.ItemsSource = typeRows;
        EmptyStateText.Text = "No enabled class types.";
        EmptyStateText.Visibility = typeRows.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void BackToSubjects_Click(object sender, RoutedEventArgs e)
    {
        PageHeading.Text = "Statistics";
        BackButton.Visibility = Visibility.Collapsed;
        SubjectStatsList.Visibility = Visibility.Visible;
        ClassTypeStatsList.Visibility = Visibility.Collapsed;
        EmptyStateText.Visibility = SubjectStatsList.Items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private SubjectStatRow CreateRow(long subjectId, string subjectName, string venue, string professor,
        string? typeName, int attendedMinutes, int countedMinutes, int target, bool isClickable)
    {
        var percentage = countedMinutes == 0 ? 0m : attendedMinutes * 100m / countedMinutes;
        return new SubjectStatRow(subjectId, subjectName, typeName, venue, professor,
            !string.IsNullOrWhiteSpace(professor), !string.IsNullOrWhiteSpace(venue),
            BuildTargetNote(attendedMinutes, countedMinutes, target), PickTargetBrush(attendedMinutes, countedMinutes, target),
            new GridLength(target, GridUnitType.Star), new GridLength(100 - target, GridUnitType.Star),
            $"{FormatHours(attendedMinutes)} attended of {FormatHours(countedMinutes)} counted",
            $"{percentage:0.0}%", (double)percentage, isClickable);
    }

    private static string FormatHours(int minutes) =>
        $"{(minutes / 60m).ToString("0.##", CultureInfo.InvariantCulture)} h";

    // Whole-minute maths, so there is no rounding drift:
    //   at/above target: hours you can still miss = floor(attended / target - counted)
    //   below target:    hours to attend in a row = ceil((target * counted - attended) / (1 - target))
    private static string BuildTargetNote(int attendedMinutes, int countedMinutes, int target)
    {
        long attended = attendedMinutes;
        long counted = countedMinutes;
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

    private System.Windows.Media.Brush PickTargetBrush(int attendedMinutes, int countedMinutes, int target) =>
        countedMinutes == 0 || attendedMinutes * 100L >= target * (long)countedMinutes
            ? (System.Windows.Media.Brush)FindResource("SuccessBrush")
            : (System.Windows.Media.Brush)FindResource("DangerBrush");

    private sealed record SubjectStatRow(
        long SubjectId,
        string SubjectName,
        string? TypeName,
        string Venue,
        string Professor,
        bool HasProfessor,
        bool HasVenue,
        string TargetNote,
        System.Windows.Media.Brush TargetBrush,
        GridLength TargetLeft,
        GridLength TargetRight,
        string HoursSummary,
        string PercentageText,
        double PercentageValue,
        bool IsClickable)
    {
        public Visibility TypeVisibility => TypeName is null ? Visibility.Collapsed : Visibility.Visible;
        public Cursor Cursor => IsClickable ? Cursors.Hand : Cursors.Arrow;
    }
}
