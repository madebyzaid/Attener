using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Attener.Domain;
using Attener.Features.Attendance;
using Attener.Features.Subjects;
using Attener.Infrastructure.Database;
using Attener.Infrastructure.Dialogs;

namespace Attener.Views;

public partial class AttendanceView : UserControl
{
    // Left text padding inside the Add entry dialog, tuned so the Date and Duration text line up
    // with the Subject and Status text. If the dialog looks misaligned, change these two numbers.
    private const double DateTextPaddingLeft = 4.5;
    private const double DurationTextPaddingLeft = 15;

    private readonly AttendanceRepository _attendance = new(DatabaseLocation.GetPath());
    private readonly SemesterSettingsRepository _settings = new(DatabaseLocation.GetPath());
    private readonly SubjectRepository _subjects = new(DatabaseLocation.GetPath());
    private long? _selectedSubjectId;
    private SubjectType? _selectedType;
    private string _selectedSubjectLabel = "All subjects";

    public AttendanceView()
    {
        InitializeComponent();
        DataContext = this;
        LoadAttendance();
    }

    public void RefreshData()
    {
        UpdateSubjectFilterLabel();
        LoadAttendance();
    }

    private void UpdateSubjectFilterLabel()
    {
        if (_selectedSubjectId is null)
        {
            _selectedSubjectLabel = "All subjects";
        }
        else
        {
            var subject = _subjects.GetSubjects(includeArchived: false)
                .Concat(_subjects.GetSubjects(includeArchived: true))
                .FirstOrDefault(candidate => candidate.Id == _selectedSubjectId);
            if (subject is null)
            {
                _selectedSubjectId = null;
                _selectedType = null;
                _selectedSubjectLabel = "All subjects";
            }
            else
            {
                _selectedSubjectLabel = subject.Name + (subject.IsArchived ? " · Archived" : "");
                if (_selectedType is SubjectType type)
                    _selectedSubjectLabel += $" · {TypeLetter(type)}";
            }
        }

        SubjectFilterButton.Content = _selectedSubjectLabel;
    }

    private void SubjectFilterButton_Click(object sender, RoutedEventArgs e)
    {
        SubjectFilterList.Children.Clear();
        SubjectFilterList.Children.Add(CreateFilterRow("All subjects", null, null));

        var subjects = _subjects.GetSubjects(includeArchived: false)
            .Concat(_subjects.GetSubjects(includeArchived: true));
        foreach (var subject in subjects)
        {
            var label = subject.Name + (subject.IsArchived ? " · Archived" : "");
            SubjectFilterList.Children.Add(CreateFilterRow(label, subject.Id, subject.Types));
        }

        SubjectFilterPopup.IsOpen = true;
    }

    private Grid CreateFilterRow(string label, long? subjectId, IReadOnlyList<SubjectType>? availableTypes)
    {
        var row = new Grid { Margin = new Thickness(0, 2, 0, 2) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var name = new Button
        {
            Content = label,
            HorizontalContentAlignment = HorizontalAlignment.Left,
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            Padding = new Thickness(10, 4, 8, 4),
            Margin = new Thickness(0),
            BorderThickness = new Thickness(0),
            Background = Brushes.Transparent,
            Cursor = Cursors.Hand
        };
        name.Click += (_, _) => SelectSubjectFilter(subjectId, null, label);
        row.Children.Add(name);

        if (subjectId is not null && availableTypes is not null)
        {
            var badges = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(0, 0, 6, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            foreach (var type in availableTypes)
                badges.Children.Add(CreateTypeBadge(subjectId.Value, label, type));
            Grid.SetColumn(badges, 1);
            row.Children.Add(badges);
        }

        return row;
    }

    private Button CreateTypeBadge(long subjectId, string subjectLabel, SubjectType type)
    {
        var normalBackground = (Brush)FindResource("WindowBackgroundBrush");
        var selected = _selectedSubjectId == subjectId && _selectedType == type;
        var selectedBackground = (Brush)FindResource("AccentBrush");
        var badge = new Button
        {
            Content = TypeLetter(type),
            ToolTip = type.ToString(),
            Width = 30,
            Height = 28,
            Padding = new Thickness(0),
            Margin = new Thickness(5, 0, 0, 0),
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            Background = selected ? selectedBackground : normalBackground,
            Foreground = (Brush)FindResource("PrimaryTextBrush"),
            BorderBrush = (Brush)FindResource("BorderBrush"),
            BorderThickness = new Thickness(1),
            Cursor = Cursors.Hand
        };
        badge.MouseEnter += (_, _) =>
        {
            if (_selectedSubjectId != subjectId || _selectedType != type)
                badge.Background = (Brush)FindResource("HoverBrush");
        };
        badge.MouseLeave += (_, _) =>
        {
            badge.Background = _selectedSubjectId == subjectId && _selectedType == type
                ? selectedBackground : normalBackground;
        };
        badge.Click += (_, _) => SelectSubjectFilter(subjectId, type, subjectLabel);
        return badge;
    }

    private void SelectSubjectFilter(long? subjectId, SubjectType? type, string label)
    {
        _selectedSubjectId = subjectId;
        _selectedType = type;
        _selectedSubjectLabel = subjectId is null
            ? label
            : label + (type is null ? "" : $" · {TypeLetter(type.Value)}");
        SubjectFilterButton.Content = _selectedSubjectLabel;
        SubjectFilterPopup.IsOpen = false;
        LoadAttendance();
    }

    private static string TypeLetter(SubjectType type) => type switch
    {
        SubjectType.Lecture => "L",
        SubjectType.Tutorial => "T",
        SubjectType.Practical => "P",
        _ => throw new ArgumentOutOfRangeException(nameof(type))
    };

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
                .Where(MatchesFilter)
                .OrderBy(entry => entry.ScheduledStart is null ? 1 : 0)
                .ThenBy(entry => entry.ScheduledStart, StringComparer.Ordinal)
                .Select(entry => new AttendanceRow(entry, DescribeTime(entry),
                    entry.Status ?? AttendanceStatus.NotMarked))
                .ToArray();
            cards.Add(DayCard.ForToday(today, todayRows));

            cards.AddRange(_attendance.GetPendingAttendance(today)
                .Where(entry => entry.AttendanceDate < today)
                .Where(MatchesFilter)
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

    private bool MatchesFilter(AttendanceEntry entry) =>
        (_selectedSubjectId is null || entry.SubjectId == _selectedSubjectId)
        && (_selectedType is null || entry.Type == _selectedType);

    private static string DescribeTime(AttendanceEntry entry) =>
        entry.IsManual
            ? $"Added manually · {FormatHours(entry.ScheduledDurationMinutes)}"
            : entry.ScheduledStart is null || entry.ScheduledEnd is null
                ? "No scheduled time"
                : $"{entry.ScheduledStart}–{entry.ScheduledEnd}";

    private void Present_Click(object sender, RoutedEventArgs e) => SetStatus(sender, AttendanceStatus.Present);

    private void Absent_Click(object sender, RoutedEventArgs e) => SetStatus(sender, AttendanceStatus.Absent);

    private void Cancelled_Click(object sender, RoutedEventArgs e) => SetStatus(sender, AttendanceStatus.Cancelled);

    // Clicking the status that is already selected un-marks the class (back to Not Marked).
    // Attendance always counts the full scheduled duration, so no duration is saved.
    private void SetStatus(object sender, AttendanceStatus clicked)
    {
        if (sender is not FrameworkElement { DataContext: AttendanceRow row })
            return;

        if (row.Entry.IsManual && row.SelectedStatus == clicked)
        {
            var answer = InAppDialogService.Show(this,
                $"Delete this {row.Entry.SubjectName} entry for {row.Entry.AttendanceDate:dd MMM yyyy}? This cannot be undone.",
                "Delete entry", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No);
            if (answer != MessageBoxResult.Yes)
                return;

            _attendance.DeleteManualEntry(row.Entry.Id);
            LoadAttendance();
            return;
        }

        var newStatus = row.SelectedStatus == clicked ? AttendanceStatus.NotMarked : clicked;
        _attendance.SaveAttendance(row.Entry, newStatus);
        LoadAttendance();
    }

    private void DeleteManual_Click(object sender, RoutedEventArgs e)
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

        var choices = _subjects.GetSubjects(includeArchived: false)
            .Concat(_subjects.GetSubjects(includeArchived: true))
            .SelectMany(subject => subject.Types.Select(type => new SubjectChoice(
                subject.Id, type, subject.Name, subject.IsArchived)))
            .ToList();
        if (choices.Count == 0)
        {
            InAppDialogService.Show(this, "Add a subject in Subjects first.", "No subjects yet",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var secondary = (Brush)FindResource("SecondaryTextBrush");
        var subjectBox = new ComboBox
        {
            Height = 38,
            ItemsSource = choices,
            ItemTemplate = (DataTemplate)FindResource("SubjectChoiceTemplate"),
            SelectedIndex = 0,
            Margin = new Thickness(0, 6, 0, 14)
        };
        var dateBox = new DatePicker
        {
            Height = 38,
            DisplayDateStart = semesterStartDate,
            SelectedDate = selectedDate,
            IsEnabled = !lockDate,
            Margin = new Thickness(0, 6, 0, 14)
        };
        // DatePicker has no Padding of its own; the text lives in the template's PART_TextBox.
        // Adjust its padding so the date text lines up with the other fields.
        dateBox.Loaded += (_, _) =>
        {
            if (dateBox.Template.FindName("PART_TextBox", dateBox) is System.Windows.Controls.Primitives.DatePickerTextBox textBox)
                textBox.Padding = new Thickness(DateTextPaddingLeft, 0, 0, 0);
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
        var durationBox = new TextBox { Height = 38, Text = "1", Padding = new Thickness(DurationTextPaddingLeft, 6, 6, 6), VerticalContentAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 6, 0, 18) };

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
        panel.Children.Add(MakeLabel("Date"));
        panel.Children.Add(dateBox);
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

        var chosenSubject = (SubjectChoice)subjectBox.SelectedItem;
        var status = (StatusChoice)statusBox.SelectedItem;
        var date = dateBox.SelectedDate!.Value;
        _attendance.AddManualEntry(chosenSubject.Id, chosenSubject.Type,
            DateOnly.FromDateTime(date), status.Status, minutes);
        LoadAttendance();
    }

    private static string FormatHours(int minutes) =>
        $"{(minutes / 60m).ToString("0.##", CultureInfo.InvariantCulture)} h";

    private sealed record SubjectChoice(long Id, SubjectType Type, string SubjectName, bool IsArchived)
    {
        public string TypeLetter => Type switch
        {
            SubjectType.Lecture => "L",
            SubjectType.Tutorial => "T",
            SubjectType.Practical => "P",
            _ => throw new ArgumentOutOfRangeException(nameof(Type))
        };
        public Visibility ArchivedVisibility => IsArchived ? Visibility.Visible : Visibility.Collapsed;
    }
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
        public string ScheduledTime { get; }
        public AttendanceStatus SelectedStatus { get; }

        public bool IsPresent => SelectedStatus == AttendanceStatus.Present;
        public bool IsAbsent => SelectedStatus == AttendanceStatus.Absent;
        public bool IsCancelled => SelectedStatus == AttendanceStatus.Cancelled;

        // The Delete button only appears on records that were added by hand.
        public Visibility DeleteVisibility => Entry.IsManual ? Visibility.Visible : Visibility.Collapsed;

        public AttendanceRow(AttendanceEntry entry, string scheduledTime, AttendanceStatus selectedStatus)
        {
            Entry = entry;
            ScheduledTime = scheduledTime;
            SelectedStatus = selectedStatus;
        }
    }
}