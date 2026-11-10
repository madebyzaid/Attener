using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Attener.Domain;
using Attener.Features.Attendance;
using Attener.Features.Subjects;
using Attener.Infrastructure.Database;

namespace Attener.Views;

public partial class AttendanceView : UserControl
{
    private readonly AttendanceRepository _attendance = new(DatabaseLocation.GetPath());
    private readonly SemesterSettingsRepository _settings = new(DatabaseLocation.GetPath());
    private readonly SubjectRepository _subjects = new(DatabaseLocation.GetPath());
    private bool _showPending;

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
            : _attendance.GetAttendanceForDate(date, allowUnmarked: true);

        var rows = entries.Select(entry => new AttendanceRow(
            entry,
            entry.IsManual
                ? $"Added manually · {FormatHours(entry.ScheduledDurationMinutes)}"
                : _showPending
                    ? $"{entry.AttendanceDate:dd MMM yyyy} · {entry.ScheduledStart}–{entry.ScheduledEnd}"
                    : entry.ScheduledStart is null || entry.ScheduledEnd is null
                        ? "No scheduled time"
                        : $"{entry.ScheduledStart}–{entry.ScheduledEnd}",
            entry.Status ?? AttendanceStatus.NotMarked,
            entry.IsManual || _showPending || (canEdit && canGenerate))).ToArray();

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
                    ? "Choose a semester start date in Settings to generate attendance records."
                    : "No unmarked classes remain for today or earlier."
                : !canEdit
                    ? "Future classes appear in Timetable and Calendar; they are not pending attendance."
                    : semesterStart is null
                        ? "Choose a semester start date in Settings to generate attendance records."
                        : date < semesterStart.Value
                            ? $"This date is before the selected semester start ({semesterStart:dd MMM yyyy})."
                            : "No classes are scheduled on this date.";
        }
    }

    private void Present_Click(object sender, RoutedEventArgs e) => SetStatus(sender, AttendanceStatus.Present);

    private void Absent_Click(object sender, RoutedEventArgs e) => SetStatus(sender, AttendanceStatus.Absent);

    private void Cancelled_Click(object sender, RoutedEventArgs e) => SetStatus(sender, AttendanceStatus.Cancelled);

    // Clicking the status that is already selected un-marks the class (back to Not Marked).
    // Attendance always counts the full scheduled duration, so no duration is saved.
    private void SetStatus(object sender, AttendanceStatus clicked)
    {
        if (sender is not FrameworkElement { DataContext: AttendanceRow row })
            return;

        // A hand-entered record is always Present, Absent or Cancelled, so clicking its current
        // status does nothing (use Delete to remove it).
        var newStatus = row.SelectedStatus == clicked && !row.Entry.IsManual ? AttendanceStatus.NotMarked : clicked;
        _attendance.SaveAttendance(row.Entry, newStatus);
        LoadAttendance();
    }

    private void DeleteManual_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: AttendanceRow row } || !row.Entry.IsManual)
            return;

        var answer = MessageBox.Show(
            $"Delete this {row.Entry.SubjectName} entry for {row.Entry.AttendanceDate:dd MMM yyyy}? This cannot be undone.",
            "Delete entry", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No);
        if (answer != MessageBoxResult.Yes)
            return;

        _attendance.DeleteManualEntry(row.Entry.Id);
        LoadAttendance();
    }

    // Pop-up for adding attendance by hand: an existing subject, any date, a status and a duration.
    private void AddManual_Click(object sender, RoutedEventArgs e)
    {
        var choices = _subjects.GetSubjects(includeArchived: false)
            .Select(subject => new SubjectChoice(subject.Id, subject.Name))
            .Concat(_subjects.GetSubjects(includeArchived: true)
                .Select(subject => new SubjectChoice(subject.Id, $"{subject.Name} · Archived")))
            .ToList();
        if (choices.Count == 0)
        {
            MessageBox.Show("Add a subject in Subjects first.", "No subjects yet",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var secondary = (Brush)FindResource("SecondaryTextBrush");
        var subjectBox = new ComboBox { Height = 38, ItemsSource = choices, SelectedIndex = 0, Margin = new Thickness(0, 6, 0, 14) };
        var dateBox = new DatePicker
        {
            Height = 38,
            SelectedDate = AttendanceDatePicker.SelectedDate ?? DateTime.Today,
            Margin = new Thickness(0, 6, 0, 14)
        };
        var statusBox = new ComboBox
        {
            Height = 38,
            ItemsSource = new[]
            {
                new StatusChoice(AttendanceStatus.Present, "Present"),
                new StatusChoice(AttendanceStatus.Absent, "Absent"),
                new StatusChoice(AttendanceStatus.Cancelled, "Cancelled")
            },
            SelectedIndex = 0,
            Margin = new Thickness(0, 6, 0, 14)
        };
        var durationBox = new TextBox { Height = 38, Text = "1", VerticalContentAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 6, 0, 18) };

        var save = new Button
        {
            Content = "Save entry",
            Padding = new Thickness(18, 7, 18, 7),
            Background = (Brush)FindResource("AccentBrush"),
            Foreground = new SolidColorBrush(Color.FromRgb(0x11, 0x13, 0x18)),
            FontWeight = FontWeights.SemiBold,
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
        buttons.Children.Add(save);

        TextBlock MakeLabel(string text) => new() { Text = text, Foreground = secondary, FontSize = 12 };

        var panel = new StackPanel { Margin = new Thickness(22), Width = 320 };
        panel.Children.Add(new TextBlock { Text = "Add attendance entry", FontSize = 18, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 16) });
        panel.Children.Add(MakeLabel("Subject"));
        panel.Children.Add(subjectBox);
        panel.Children.Add(MakeLabel("Date"));
        panel.Children.Add(dateBox);
        panel.Children.Add(MakeLabel("Status"));
        panel.Children.Add(statusBox);
        panel.Children.Add(MakeLabel("Duration (hours, for example 1 or 1.5)"));
        panel.Children.Add(durationBox);
        panel.Children.Add(buttons);

        var dialog = new Window
        {
            Title = "Add entry",
            Content = panel,
            SizeToContent = SizeToContent.WidthAndHeight,
            ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = Window.GetWindow(this),
            Background = (Brush)FindResource("SurfaceBackgroundBrush")
        };

        var minutes = 0;
        save.Click += (_, _) =>
        {
            if (dateBox.SelectedDate is null)
            {
                MessageBox.Show("Choose a date.", "Date required", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            if (!decimal.TryParse(durationBox.Text.Trim().Replace(',', '.'), NumberStyles.AllowDecimalPoint,
                    CultureInfo.InvariantCulture, out var hours) || hours <= 0 || hours > 24)
            {
                MessageBox.Show("Enter a duration between 0 and 24 hours, such as 1 or 0.75.",
                    "Check the duration", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            minutes = (int)Math.Round(hours * 60m, MidpointRounding.AwayFromZero);
            if (minutes < 1)
            {
                MessageBox.Show("That duration is too short. Try at least 0.02 hours (about a minute).",
                    "Check the duration", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            dialog.DialogResult = true;
        };

        if (dialog.ShowDialog() != true)
            return;

        var chosenSubject = (SubjectChoice)subjectBox.SelectedItem;
        var status = (StatusChoice)statusBox.SelectedItem;
        var date = dateBox.SelectedDate!.Value;
        _attendance.AddManualEntry(chosenSubject.Id, DateOnly.FromDateTime(date), status.Status, minutes);

        // Jump to the day that was just edited so the new entry is visible straight away.
        _showPending = false;
        AttendanceDatePicker.SelectedDate = date;
        LoadAttendance();
    }

    private static string FormatHours(int minutes) =>
        $"{(minutes / 60m).ToString("0.##", CultureInfo.InvariantCulture)} h";

    private sealed record SubjectChoice(long Id, string Label) { public override string ToString() => Label; }
    private sealed record StatusChoice(AttendanceStatus Status, string Label) { public override string ToString() => Label; }

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

    private sealed class AttendanceRow
    {
        public AttendanceEntry Entry { get; }
        public string ScheduledTime { get; }
        public AttendanceStatus SelectedStatus { get; }
        public bool CanEdit { get; }

        public bool IsPresent => SelectedStatus == AttendanceStatus.Present;
        public bool IsAbsent => SelectedStatus == AttendanceStatus.Absent;
        public bool IsCancelled => SelectedStatus == AttendanceStatus.Cancelled;

        // The Delete button only appears on records that were added by hand.
        public Visibility DeleteVisibility => Entry.IsManual ? Visibility.Visible : Visibility.Collapsed;

        public AttendanceRow(AttendanceEntry entry, string scheduledTime,
            AttendanceStatus selectedStatus, bool canEdit)
        {
            Entry = entry;
            ScheduledTime = scheduledTime;
            SelectedStatus = selectedStatus;
            CanEdit = canEdit;
        }
    }
}