using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using Attener.Domain;
using Attener.Features.Attendance;
using Attener.Infrastructure.Database;

namespace Attener.Views;

public partial class AttendanceView : UserControl
{
    private readonly AttendanceRepository _attendance = new(DatabaseLocation.GetPath());
    private readonly SemesterSettingsRepository _settings = new(DatabaseLocation.GetPath());
    private bool _showPending;

    public IReadOnlyList<StatusOption> StatusOptions { get; } =
    [
        new(AttendanceStatus.Present, "Present"),
        new(AttendanceStatus.Absent, "Absent"),
        new(AttendanceStatus.Cancelled, "Cancelled")
    ];

    public AttendanceView()
    {
        InitializeComponent();
        DataContext = this;
        AttendanceDatePicker.SelectedDate = DateTime.Today;
        LoadAttendance();
    }

    public void RefreshData() => LoadAttendance();

    private void LoadAttendance()
    {
        if (AttendanceDatePicker.SelectedDate is not DateTime selectedDate)
            return;

        var date = DateOnly.FromDateTime(selectedDate);
        var semesterStart = _settings.GetSemesterStartDate();
        var canGenerate = semesterStart is not null && date >= semesterStart.Value;
        var canEdit = date <= DateOnly.FromDateTime(DateTime.Today);

        if (canGenerate && canEdit)
            _attendance.GenerateMissingBetween(date, date);

        var entries = _showPending
            ? _attendance.GetPendingAttendance(DateOnly.FromDateTime(DateTime.Today))
            : canEdit
                ? _attendance.GetAttendanceForDate(date, allowUnmarked: true)
                : Array.Empty<AttendanceEntry>();

        var rows = entries.Select(entry => new AttendanceRow(
            entry,
            _showPending
                ? $"{entry.AttendanceDate:dd MMM yyyy} · {entry.ScheduledStart}–{entry.ScheduledEnd}"
                : entry.ScheduledStart is null || entry.ScheduledEnd is null
                    ? "No scheduled time"
                    : $"{entry.ScheduledStart}–{entry.ScheduledEnd}",
            entry.Status ?? AttendanceStatus.NotMarked,
            (entry.ActualDurationMinutes ?? entry.ScheduledDurationMinutes) / 60m,
            _showPending || (canEdit && canGenerate))).ToArray();

        DateControls.Visibility = _showPending ? Visibility.Collapsed : Visibility.Visible;
        PendingHeader.Visibility = _showPending ? Visibility.Visible : Visibility.Collapsed;
        ByDateButton.Background = _showPending ? System.Windows.Media.Brushes.Transparent : (System.Windows.Media.Brush)FindResource("SurfaceBackgroundBrush");
        PendingButton.Background = _showPending ? (System.Windows.Media.Brush)FindResource("SurfaceBackgroundBrush") : System.Windows.Media.Brushes.Transparent;
        AttendanceList.ItemsSource = rows;
        EmptyStateText.Visibility = rows.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        if (rows.Length == 0)
        {
            EmptyStateText.Text = _showPending
                ? semesterStart is null
                    ? "Choose a semester start date on Overview to generate attendance records."
                    : "No unmarked classes remain for today or earlier."
                : !canEdit
                    ? "Future classes appear in Timetable and Calendar; they are not pending attendance."
                    : semesterStart is null
                        ? "Choose a semester start date on Overview to generate attendance records."
                        : date < semesterStart.Value
                            ? $"This date is before the selected semester start ({semesterStart:dd MMM yyyy})."
                            : "No classes are scheduled on this date.";
        }
    }

    private void SaveAttendance_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: AttendanceRow row })
            return;

        int? actualMinutes = null;
        if (!string.IsNullOrWhiteSpace(row.ActualDurationHours))
        {
            if (!decimal.TryParse(row.ActualDurationHours, NumberStyles.AllowDecimalPoint,
                    CultureInfo.InvariantCulture, out var hours) || hours < 0)
            {
                MessageBox.Show("Enter a non-negative number of hours, such as 1 or 0.75.",
                    "Check the duration", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            actualMinutes = (int)Math.Round(hours * 60m, MidpointRounding.AwayFromZero);
        }

        _attendance.SaveAttendance(row.Entry, row.SelectedStatus, actualMinutes);
        LoadAttendance();
    }

    private void ShowByDate_Click(object sender, RoutedEventArgs e)
    {
        _showPending = false;
        LoadAttendance();
    }

    private void ShowPending_Click(object sender, RoutedEventArgs e)
    {
        _showPending = true;
        LoadAttendance();
    }

    private void AttendanceDatePicker_SelectedDateChanged(object sender, SelectionChangedEventArgs e)
    {
        if (IsLoaded)
            LoadAttendance();
    }

    public sealed record StatusOption(AttendanceStatus Status, string Label);

    private sealed class AttendanceRow
    {
        public AttendanceEntry Entry { get; }
        public string ScheduledTime { get; }
        public AttendanceStatus SelectedStatus { get; set; }
        public string ActualDurationHours { get; set; }
        public bool CanEdit { get; }

        public AttendanceRow(AttendanceEntry entry, string scheduledTime,
            AttendanceStatus selectedStatus, decimal durationHours, bool canEdit)
        {
            Entry = entry;
            ScheduledTime = scheduledTime;
            SelectedStatus = selectedStatus;
            ActualDurationHours = durationHours.ToString("0.##", CultureInfo.InvariantCulture);
            CanEdit = canEdit;
        }
    }
}



