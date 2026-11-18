using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Attener.Domain;
using Attener.Controls;
using Attener.Features.Attendance;
using Attener.Features.Subjects;
using Attener.Infrastructure.Database;
using Attener.Infrastructure.Dialogs;
using System.Windows.Controls.Primitives;

namespace Attener.Views;

public partial class AttendanceView : UserControl
{
    private readonly AttendanceRepository _attendance = new(DatabaseLocation.GetPath());
    private readonly SemesterSettingsRepository _settings = new(DatabaseLocation.GetPath());
    private readonly SubjectRepository _subjects = new(DatabaseLocation.GetPath());

    public AttendanceView()
    {
        InitializeComponent();
        DataContext = this;
        LoadAttendance();
    }

    public void RefreshData()
    {
        LoadAttendance();
    }

    // The page shows one card per date: today first (all of today's classes, marked or not),
    // then every earlier date that still has unmarked classes, newest date first.
    private void LoadAttendance()
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        var semesterStart = _settings.GetSemesterStartDate();
        var scrollOffset = CardsScroll.VerticalOffset;
        var cards = new List<DayCard>();

        if (semesterStart is not null && semesterStart.Value <= today)
        {
            // There is no date picker any more, so make sure every class from the semester start
            // through today has a record. Records that already exist are skipped.
            _attendance.GenerateMissingBetween(semesterStart.Value, today);

            var todayRows = _attendance.GetAttendanceForDate(today, allowUnmarked: true)
                .OrderBy(entry => entry.ScheduledStart is null ? 1 : 0)
                .ThenBy(entry => entry.ScheduledStart, StringComparer.Ordinal)
                .Select(entry => new AttendanceRow(entry, DescribeTime(entry),
                    entry.Status ?? AttendanceStatus.NotMarked))
                .ToArray();
            cards.Add(DayCard.ForToday(today, todayRows));

            cards.AddRange(_attendance.GetPendingAttendance(today)
                .Where(entry => entry.AttendanceDate < today)
                .GroupBy(entry => entry.AttendanceDate)
                .OrderByDescending(group => group.Key)
                .Select(group => DayCard.ForEarlierDay(group.Key,
                    group.Select(entry => new AttendanceRow(entry, DescribeTime(entry),
                        AttendanceStatus.NotMarked)).ToArray())));
        }

        CardsList.ItemsSource = cards;

        var hasCards = cards.Count > 0;
        EmptyStateText.Visibility = hasCards ? Visibility.Collapsed : Visibility.Visible;
        if (!hasCards)
        {
            EmptyStateText.Text = semesterStart is null
                ? "Choose a semester start date in Settings to generate attendance records."
                : $"The semester starts on {semesterStart:dd MMM yyyy}. Classes will appear here from that day.";
        }
        NothingElseText.Visibility = cards.Count == 1 ? Visibility.Visible : Visibility.Collapsed;

        // Keep the scroll position, so marking a class far down the page does not jump back to the top.
        CardsScroll.UpdateLayout();
        CardsScroll.ScrollToVerticalOffset(scrollOffset);
    }

    private static string DescribeTime(AttendanceEntry entry) =>
        entry.IsManual
            ? $"Manual · {FormatHours(entry.ScheduledDurationMinutes)}"
            : entry.ScheduledStart is null || entry.ScheduledEnd is null
                ? "No scheduled time"
                : $"{entry.ScheduledStart}–{entry.ScheduledEnd}";

    private void Row_StatusClicked(object sender, AttendanceStatus clicked) => SetStatus(sender, clicked);

    // Clicking the status that is already selected un-marks the class (back to Not Marked).
    // Hand-added entries are un-marked the same way; only their Delete button removes them.
    // Attendance always counts the full scheduled duration, so no duration is saved.
    private void SetStatus(object sender, AttendanceStatus clicked)
    {
        if (sender is not FrameworkElement { DataContext: AttendanceRow row })
            return;

        var newStatus = row.SelectedStatus == clicked ? AttendanceStatus.NotMarked : clicked;
        _attendance.SaveAttendance(row.Entry, newStatus);
        LoadAttendance();
    }

    private void DeleteManual_Click(object sender, EventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: AttendanceRow row } || !row.Entry.IsManual)
            return;

        var answer = InAppDialogService.Show(this,
            $"Delete this {row.Entry.SubjectName} entry for {row.Entry.AttendanceDate:dd MMM yyyy}? This cannot be undone.",
            "Delete entry", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No);
        if (answer != MessageBoxResult.Yes)
            return;

        _attendance.DeleteManualEntry(row.Entry.Id);
        LoadAttendance();
    }

    // In-app form for adding attendance by hand.
    private void AddManual_Click(object sender, RoutedEventArgs e) => AddManualForDate();

    public void AddManualForDate(DateTime? initialDate = null, bool lockDate = false)
    {
        var semesterStart = _settings.GetSemesterStartDate();
        if (semesterStart is null)
        {
            InAppDialogService.Show(this, "Choose a semester start date in Settings first.",
                "Semester start required", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var semesterStartDate = semesterStart.Value.ToDateTime(TimeOnly.MinValue);
        var selectedDate = initialDate ?? DateTime.Today;
        if (selectedDate.Date < semesterStartDate)
            selectedDate = semesterStartDate;

        var subjects = _subjects.GetSubjects(includeArchived: false)
            .Concat(_subjects.GetSubjects(includeArchived: true))
            .Where(subject => subject.Types.Any())
            .Select(subject => (Id: subject.Id,
                Name: subject.Name + (subject.IsArchived ? " · Archived" : ""),
                Types: subject.Types.ToList()))
            .ToList();
        if (subjects.Count == 0)
        {
            InAppDialogService.Show(this, "Add a subject in Subjects first.", "No subjects yet",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var secondary = (Brush)FindResource("SecondaryTextBrush");

        // Subject + class type: a field that opens a popup of subject rows; only the L/T/P badges pick.
        var chosenId = subjects[0].Id;
        var chosenName = subjects[0].Name;
        var chosenType = subjects[0].Types[0];
        var pickText = new TextBlock { VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        var pickChevron = new TextBlock { Text = "▾", Foreground = secondary, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) };
        var pickContent = new Grid();
        pickContent.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        pickContent.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        pickContent.Children.Add(pickText);
        Grid.SetColumn(pickChevron, 1);
        pickContent.Children.Add(pickChevron);
        var subjectBox = new Button
        {
            Height = (double)FindResource("FieldHeight"),
            Style = (Style)FindResource("SecondaryButtonStyle"),
            Background = (Brush)FindResource("SurfaceBackgroundBrush"),
            Foreground = (Brush)FindResource("PrimaryTextBrush"),
            BorderBrush = (Brush)FindResource("BorderBrush"),
            BorderThickness = new Thickness(1),
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Content = pickContent,
            Margin = new Thickness(0, 6, 0, 14)
        };
        void ShowChoice() => pickText.Text = $"{chosenName} · {new SubjectTypeBadge(chosenType).Letter}";
        ShowChoice();
        subjectBox.Click += (_, _) =>
        {
            var popup = new Popup
            {
                PlacementTarget = subjectBox,
                Placement = PlacementMode.Bottom,
                VerticalOffset = 4,
                StaysOpen = false,
                AllowsTransparency = true,
                PopupAnimation = PopupAnimation.Fade
            };
            var list = new StackPanel();
            foreach (var subject in subjects)
            {
                var picker = new SubjectTypePicker
                {
                    Mode = SubjectTypePickerMode.Select,
                    SubjectName = subject.Name,
                    NameMaxWidth = 200,
                    FontSize = 13,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = (Brush)FindResource("PrimaryTextBrush"),
                    Margin = new Thickness(10, 2, 6, 2),
                    Badges = subject.Types.Select(type =>
                        new SubjectTypeBadge(type, subject.Id == chosenId && type == chosenType)).ToList()
                };
                picker.TypeClicked += (_, args) =>
                {
                    chosenId = subject.Id;
                    chosenName = subject.Name;
                    chosenType = args.Type!.Value;
                    popup.IsOpen = false;
                    ShowChoice();
                };
                list.Children.Add(picker);
            }
            popup.Child = new Border
            {
                Background = (Brush)FindResource("SurfaceBackgroundBrush"),
                BorderBrush = (Brush)FindResource("BorderBrush"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(4),
                MinWidth = Math.Max(240, subjectBox.ActualWidth),
                Child = new ScrollViewer
                {
                    MaxHeight = 320,
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                    Content = list
                }
            };
            popup.IsOpen = true;
        };

        var dateBox = new DatePicker
        {
            Height = (double)FindResource("FieldHeight"),
            DisplayDateStart = semesterStartDate,
            SelectedDate = selectedDate,
            IsEnabled = !lockDate,
            Margin = new Thickness(0, 6, 0, 14)
        };
        dateBox.AddHandler(UIElement.GotMouseCaptureEvent,
            new System.Windows.Input.MouseEventHandler(ReleaseCalendarCapture));
        var statusBox = new ComboBox
        {
            Height = (double)FindResource("FieldHeight"),
            ItemsSource = new[]
            {
                new StatusChoice(AttendanceStatus.Present, "Present"),
                new StatusChoice(AttendanceStatus.Absent, "Absent"),
                new StatusChoice(AttendanceStatus.Cancelled, "Cancelled"),
                new StatusChoice(AttendanceStatus.NotMarked, "Not Marked")
            },
            // From the Calendar the default is Not Marked; from the Attendance page it is Present.
            SelectedIndex = lockDate ? 3 : 0,
            Margin = new Thickness(0, 6, 0, 14)
        };
        var durationBox = new TextBox { Height = (double)FindResource("FieldHeight"), Text = "1", VerticalContentAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 6, 0, 18) };

        var save = new Button
        {
            Content = "Save entry",
            Padding = new Thickness(18, 7, 18, 7),
            Background = (Brush)FindResource("AccentBrush"),
            Foreground = (Brush)FindResource("PrimaryTextBrush"),
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
        // From the Calendar the date is already chosen (lockDate), so the field is hidden.
        // dateBox still exists and holds that date for the Save handler below.
        if (!lockDate)
        {
            panel.Children.Add(MakeLabel("Date"));
            panel.Children.Add(dateBox);
        }
        panel.Children.Add(MakeLabel("Status"));
        panel.Children.Add(statusBox);
        panel.Children.Add(MakeLabel("Duration (hours, for example 1 or 1.5)"));
        panel.Children.Add(durationBox);
        panel.Children.Add(buttons);

        var minutes = 0;
        cancel.Click += (_, _) => InAppDialogService.CloseCustom(this, false);
        save.Click += (_, _) =>
        {
            if (dateBox.SelectedDate is null)
            {
                InAppDialogService.Show(this, "Choose a date.", "Date required",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            if (dateBox.SelectedDate.Value.Date < semesterStartDate)
            {
                InAppDialogService.Show(this,
                    $"Choose a date on or after the semester start, {semesterStartDate:dd MMM yyyy}.",
                    "Date before semester start", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            if (!decimal.TryParse(durationBox.Text.Trim().Replace(',', '.'), NumberStyles.AllowDecimalPoint,
                    CultureInfo.InvariantCulture, out var hours) || hours <= 0 || hours > 24)
            {
                InAppDialogService.Show(this, "Enter a duration between 0 and 24 hours, such as 1 or 0.75.",
                    "Check the duration", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            minutes = (int)Math.Round(hours * 60m, MidpointRounding.AwayFromZero);
            if (minutes < 1)
            {
                InAppDialogService.Show(this, "That duration is too short. Try at least 0.02 hours (about a minute).",
                    "Check the duration", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            InAppDialogService.CloseCustom(this, true);
        };

        if (InAppDialogService.ShowCustom(this, panel, subjectBox) != true)
            return;

        var status = (StatusChoice)statusBox.SelectedItem;
        var date = dateBox.SelectedDate!.Value;
        _attendance.AddManualEntry(chosenId, chosenType,
            DateOnly.FromDateTime(date), status.Status, minutes);
        LoadAttendance();
    }

    private static void ReleaseCalendarCapture(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (e.OriginalSource is CalendarDayButton or CalendarItem)
            ((UIElement)e.OriginalSource).ReleaseMouseCapture();
    }

    private static string FormatHours(int minutes) =>
        $"{(minutes / 60m).ToString("0.##", CultureInfo.InvariantCulture)} h";

    private sealed record StatusChoice(AttendanceStatus Status, string Label) { public override string ToString() => Label; }

    // One card on the page: a date, a one-line summary, and that date's class rows.
    private sealed class DayCard
    {
        public string Title { get; }
        public string Summary { get; }
        public bool IsToday { get; }
        public IReadOnlyList<AttendanceRow> Rows { get; }

        // Today's title is larger and shows the TODAY badge; earlier dates are quieter.
        public double TitleFontSize => IsToday ? 22 : 16;
        public Visibility BadgeVisibility => IsToday ? Visibility.Visible : Visibility.Collapsed;

        private DayCard(string title, string summary, bool isToday, IReadOnlyList<AttendanceRow> rows)
        {
            Title = title;
            Summary = summary;
            IsToday = isToday;
            Rows = rows;
        }

        public static DayCard ForToday(DateOnly date, IReadOnlyList<AttendanceRow> rows)
        {
            var marked = rows.Count(row => row.SelectedStatus != AttendanceStatus.NotMarked);
            var summary = rows.Count == 0 ? "No classes today"
                : marked == rows.Count ? "All done for today"
                : $"{marked} of {rows.Count} marked";
            return new DayCard(FormatTitle(date), summary, true, rows);
        }

        public static DayCard ForEarlierDay(DateOnly date, IReadOnlyList<AttendanceRow> rows) =>
            new(FormatTitle(date), $"{rows.Count} unmarked {(rows.Count == 1 ? "class" : "classes")}", false, rows);

        private static string FormatTitle(DateOnly date) => date.ToString("dddd, d MMM yyyy");
    }

    private sealed class AttendanceRow
    {
        public AttendanceEntry Entry { get; }
        public string TimeText { get; }
        public AttendanceStatus SelectedStatus { get; }

        public bool IsPresent => SelectedStatus == AttendanceStatus.Present;
        public bool IsAbsent => SelectedStatus == AttendanceStatus.Absent;
        public bool IsCancelled => SelectedStatus == AttendanceStatus.Cancelled;

        // Attendance rows are always editable.
        public bool CanEdit => true;

        // The Delete button only appears on records that were added by hand.
        public Visibility DeleteVisibility => Entry.IsManual ? Visibility.Visible : Visibility.Collapsed;

        public AttendanceRow(AttendanceEntry entry, string scheduledTime, AttendanceStatus selectedStatus)
        {
            Entry = entry;
            TimeText = scheduledTime;
            SelectedStatus = selectedStatus;
        }
    }
}