using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Attener.Features.Attendance;
using Attener.Features.Subjects;
using Attener.Features.Timetable;
using Attener.Infrastructure.Database;

namespace Attener.Views;

public partial class TimetableView : UserControl
{
    private readonly SubjectRepository _subjects = new(DatabaseLocation.GetPath());
    private readonly TimetableRepository _timetable = new(DatabaseLocation.GetPath());
    private readonly TimetableSettingsRepository _hourSettings = new(DatabaseLocation.GetPath());
    private readonly AttendanceRepository _attendance = new(DatabaseLocation.GetPath());
    private readonly SemesterSettingsRepository _semester = new(DatabaseLocation.GetPath());
    private static readonly string[] Days = ["Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday", "Sunday"];
    private static readonly (Color Fill, Color Border)[] SubjectTilePalette =
    [
        (Color.FromRgb(0x10, 0x37, 0x30), Color.FromRgb(0x00, 0xA8, 0x78)),
        (Color.FromRgb(0x3A, 0x30, 0x0B), Color.FromRgb(0xD8, 0x89, 0x00)),
        (Color.FromRgb(0x12, 0x2F, 0x58), Color.FromRgb(0x2F, 0x7B, 0xF5)),
        (Color.FromRgb(0x2D, 0x20, 0x45), Color.FromRgb(0x9C, 0x5C, 0xFF)),
        (Color.FromRgb(0x12, 0x35, 0x40), Color.FromRgb(0x00, 0xA6, 0xC8))
    ];

    // Hours of the day that can hold classes. Stored times are HH:mm, so the day ends at 23:00.
    private const int DayStartHour = 0;
    private const int DayEndHour = TimetableSettingsRepository.LatestEndHour;

    // The selected timetable's visible hours. Changing them crops classes only within that timetable period.
    private int _startHour = TimetableSettingsRepository.DefaultStartHour;
    private int _endHour = TimetableSettingsRepository.DefaultEndHour;
    private DateOnly _hourRangeStart = Today;

    private IReadOnlyList<SubjectSummary> _activeSubjects = [];
    private Dictionary<(int Day, int Hour), ClassAssignment> _assignments = [];
    private Dictionary<long, SubjectSummary> _subjectsById = [];

    // The current timetable is edited from today onward. Picking an older timetable from the history list
    // lets you edit how classes were scheduled in that period only; _period is that period (null = current).
    private TimetableRepository.TimetablePeriod? _period;
    private DateOnly _gridDate = Today;
    private int _gridStart = TimetableSettingsRepository.DefaultStartHour;
    private int _gridEnd = TimetableSettingsRepository.DefaultEndHour;
    private bool _loadingHistory;

    private static DateOnly Today => DateOnly.FromDateTime(DateTime.Today);
    private readonly record struct ClassAssignment(long SubjectId, SubjectType Type);

    public TimetableView()
    {
        InitializeComponent();
        RefreshData();
    }

    public void RefreshData()
    {
        _activeSubjects = _subjects.GetSubjects(includeArchived: false);
        _subjectsById = _activeSubjects
            .Concat(_subjects.GetSubjects(includeArchived: true))
            .ToDictionary(subject => subject.Id);
        LoadHistory();
        ShowSelectedTimetable();
    }

    // Fills the list at the top: the current timetable first, then every earlier one, newest first.
    private void LoadHistory(DateOnly? keepDate = null)
    {
        var today = Today;
        var history = _timetable.GetHistory();
        var current = history.FirstOrDefault(period => period.Start <= today && (period.End is null || period.End > today));

        var choices = new List<HistoryChoice>
        {
            new(null, current is null
                ? "Current timetable"
                : $"Current timetable since {current.Start:dd MMM yyyy}", current?.Start ?? today)
        };
        foreach (var period in history.Where(p => p.End is not null && p.End <= today).OrderByDescending(p => p.Start))
            choices.Add(new HistoryChoice(period, $"{period.Start:dd MMM yyyy} to {period.LastDay:dd MMM yyyy}", period.Start));

        _loadingHistory = true;
        HistoryBox.ItemsSource = choices;
        // After editing an earlier timetable, stay on the period that contains keepDate.
        var keepIndex = keepDate is null ? -1 : choices.FindIndex(choice =>
            choice.Period is not null && choice.Period.Start <= keepDate.Value
            && (choice.Period.End is null || choice.Period.End > keepDate.Value));
        HistoryBox.SelectedIndex = keepIndex < 0 ? 0 : keepIndex;
        _loadingHistory = false;
    }

    // Shows the timetable picked in the history list: the current one, or an earlier one (both editable).
    private void ShowSelectedTimetable()
    {
        var choice = HistoryBox.SelectedItem as HistoryChoice;
        _period = choice?.Period;
        _gridDate = _period?.Start ?? Today;
        _hourRangeStart = choice?.RangeStart ?? Today;
        (_startHour, _endHour) = _hourSettings.GetHourRange(_hourRangeStart, _period is null);
        ShowHourRange();
        StartHourPicker.IsEnabled = true;
        EndHourPicker.IsEnabled = true;
        HistoryNote.Text = _period is null
            ? string.Empty
            : $"Editing {_period.Start:dd MMM yyyy} to {_period.LastDay:dd MMM yyyy}. " +
              "Changes only affect those dates; marked attendance is kept.";
        HistoryNote.Visibility = _period is null ? Visibility.Collapsed : Visibility.Visible;
        BuildGrid();
    }

    private void HistoryBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loadingHistory) return;
        ShowSelectedTimetable();
    }

    private sealed record HistoryChoice(
        TimetableRepository.TimetablePeriod? Period, string Label, DateOnly RangeStart)
    {
        public override string ToString() => Label;
    }

    private void ShowHourRange()
    {
        StartHourPicker.Time = new TimeOnly(_startHour, 0);
        EndHourPicker.Time = new TimeOnly(_endHour, 0);
    }

    // Loads every class of the day, including hours outside the visible range,
    // so editing a day never throws away classes that are currently hidden.
    private void LoadAssignments()
    {
        _assignments = [];
        foreach (var session in _timetable.GetWeeklySessions(_gridDate))
        {
            var start = TimeOnly.Parse(session.StartTime);
            var end = TimeOnly.Parse(session.EndTime);
            var first = Math.Max(DayStartHour, start.Hour);
            var last = Math.Min(DayEndHour, (int)Math.Ceiling(end.ToTimeSpan().TotalHours));
            var mondayIndex = session.DayOfWeek == 0 ? 6 : session.DayOfWeek - 1;
            for (var hour = first; hour < last; hour++)
                _assignments[(mondayIndex, hour)] = new ClassAssignment(session.SubjectId, session.Type);
        }
    }

    private void BuildGrid()
    {
        LoadAssignments();

        _gridStart = _startHour;
        _gridEnd = _endHour;

        TimetableGrid.Children.Clear(); TimetableGrid.RowDefinitions.Clear(); TimetableGrid.ColumnDefinitions.Clear();
        TimetableGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110) });
        for (var hour = _gridStart; hour < _gridEnd; hour++) TimetableGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 78 });
        TimetableGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(42) });
        for (var day = 0; day < 7; day++) TimetableGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(84) });
        AddCell("Day", 0, 0, true);
        for (var hour = _gridStart; hour < _gridEnd; hour++) AddCell($"{hour:00}:00", hour - _gridStart + 1, 0, true);

        for (var day = 0; day < 7; day++)
        {
            AddCell(Days[day], 0, day + 1, true);
            var hour = _gridStart;
            while (hour < _gridEnd)
            {
                var assignment = _assignments.GetValueOrDefault((day, hour));

                // Count consecutive visible hours assigned to the same subject and class type.
                var span = 1;
                if (assignment.SubjectId != 0)
                {
                    while (hour + span < _gridEnd
                           && _assignments.GetValueOrDefault((day, hour + span)) == assignment)
                        span++;
                }

                AddHourButton(day, hour, span, assignment);
                hour += span;
            }
        }
    }

    private void AddHourButton(int day, int firstHour, int span, ClassAssignment assignment)
    {
        var id = assignment.SubjectId;
        object content = "";
        if (id != 0 && _subjectsById.TryGetValue(id, out var subject))
        {
            var classDetails = subject.DetailsFor(assignment.Type);
            var contentPanel = new StackPanel();
            var title = new StackPanel { Orientation = Orientation.Horizontal };
            title.Children.Add(new TextBlock
            {
                Text = subject.Name,
                FontSize = 11,
                FontWeight = FontWeights.SemiBold,
                TextTrimming = TextTrimming.CharacterEllipsis
            });
            title.Children.Add(new Border
            {
                Background = (Brush)FindResource("HoverBrush"),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(4, 1, 4, 1),
                Margin = new Thickness(5, 0, 0, 0),
                Child = new TextBlock
                {
                    Text = assignment.Type.ToString(),
                    FontSize = 8,
                    Foreground = (Brush)FindResource("SecondaryTextBrush")
                }
            });
            contentPanel.Children.Add(title);
            if (classDetails.HasProfessor)
                contentPanel.Children.Add(CreateTileDetail(classDetails.Professor));
            if (classDetails.HasVenue)
                contentPanel.Children.Add(CreateTileDetail(classDetails.Venue));
            content = contentPanel;
        }
        var background = id == 0
            ? (Brush)FindResource("SurfaceBackgroundBrush")
            : new SolidColorBrush(SubjectTilePalette[(int)(id % SubjectTilePalette.Length)].Fill);
        var borderBrush = id == 0
            ? (Brush)FindResource("BorderBrush")
            : new SolidColorBrush(SubjectTilePalette[(int)(id % SubjectTilePalette.Length)].Border);
        TextBlock CreateTileDetail(string text) => new()
        {
            Text = text,
            FontSize = 8,
            Foreground = (Brush)FindResource("SecondaryTextBrush"),
            TextTrimming = TextTrimming.CharacterEllipsis
        };

        var button = new Button
        {
            Content = content,
            Tag = (day, firstHour, span),
            Padding = new Thickness(4),
            FontSize = 11,
            Background = background,
            Foreground = id == 0 ? (Brush)FindResource("SecondaryTextBrush") : (Brush)FindResource("PrimaryTextBrush"),
            BorderBrush = borderBrush,
            BorderThickness = new Thickness(1),
            Cursor = Cursors.Hand,
            ToolTip = $"{Days[day]} {firstHour:00}:00–{firstHour + span:00}:00"
        };
        button.Click += Cell_Click;

        // The container holds the cell button, plus a hover-only "−" button for assigned blocks.
        var container = new Grid { Margin = new Thickness(2), Background = Brushes.Transparent };
        container.Children.Add(button);

        if (id != 0)
        {
            var remove = new Button
            {
                Content = "−",
                Tag = (day, firstHour, span),
                Width = 20,
                Height = 20,
                Padding = new Thickness(0),
                FontSize = 14,
                FontWeight = FontWeights.Bold,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, 4, 4, 0),
                Background = (Brush)FindResource("HoverBrush"),
                Foreground = Brushes.White,
                BorderThickness = new Thickness(0),
                Cursor = Cursors.Hand,
                ToolTip = "Remove this class",
                Visibility = Visibility.Collapsed
            };
            remove.Click += RemoveBlock_Click;

            // The button's own hover colour switches off while the cursor is over the "−" (a separate
            // control on top of it), so the hover look is driven by the whole container instead.
            var normalBackground = button.Background;
            var normalForeground = button.Foreground;
            var hoverBackground = (Brush)FindResource("HoverBrush");
            var hoverForeground = (Brush)FindResource("PrimaryTextBrush");

            container.MouseEnter += (_, _) =>
            {
                remove.Visibility = Visibility.Visible;
                button.Background = hoverBackground;
                button.Foreground = hoverForeground;
            };
            container.MouseLeave += (_, _) =>
            {
                remove.Visibility = Visibility.Collapsed;
                button.Background = normalBackground;
                button.Foreground = normalForeground;
            };
            container.Children.Add(remove);
        }

        Grid.SetColumn(container, firstHour - _gridStart + 1);
        Grid.SetColumnSpan(container, span);
        Grid.SetRow(container, day + 1);
        TimetableGrid.Children.Add(container);
    }

    private void AddCell(string text, int column, int row, bool header)
    {
        var block = new TextBlock
        {
            Text = text,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            FontWeight = FontWeights.SemiBold,
            FontSize = 11,
            FontFamily = column > 0 && row == 0
                ? (FontFamily)FindResource("MonoFontFamily")
                : (FontFamily)FindResource("AppFontFamily"),
            Foreground = (Brush)FindResource("PrimaryTextBrush")
        };
        var border = new Border
        {
            Child = block,
            Background = header ? (Brush)FindResource("SurfaceBackgroundBrush") : Brushes.Transparent,
            BorderBrush = (Brush)FindResource("BorderBrush"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(5),
            Margin = new Thickness(1)
        };
        Grid.SetColumn(border, column); Grid.SetRow(border, row); TimetableGrid.Children.Add(border);
    }

    private void Cell_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: ValueTuple<int, int, int> block } button) return;
        var (day, firstHour, span) = block;

        // For a merged block, work out which hour of it was clicked from the mouse position.
        var hourWidth = button.ActualWidth / span;
        var offset = hourWidth > 0 ? (int)(Mouse.GetPosition(button).X / hourWidth) : 0;
        var cell = (Day: day, Hour: firstHour + Math.Clamp(offset, 0, span - 1));

        var menu = new ContextMenu();
        if (_activeSubjects.Count == 0)
        {
            menu.Items.Add(new MenuItem { Header = "No subjects yet - add one in Subjects", IsEnabled = false });
        }
        foreach (var subject in _activeSubjects)
        {
            var item = new MenuItem { Header = subject.Name };
            foreach (var type in subject.Types)
            {
                var typeItem = new MenuItem { Header = type.ToString() };
                var assignment = new ClassAssignment(subject.Id, type);
                typeItem.Click += (_, _) => ChangeHours(cell.Day, [cell.Hour], assignment);
                item.Items.Add(typeItem);
            }
            menu.Items.Add(item);
        }
        menu.PlacementTarget = button; menu.IsOpen = true;
    }

    private void RemoveBlock_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: ValueTuple<int, int, int> block }) return;
        var (day, firstHour, span) = block;
        // Remove every hour that belongs to this block, not just one.
        ChangeHours(day, Enumerable.Range(firstHour, span).ToArray(), null);
    }

    private void ChangeHours(int day, int[] hours, ClassAssignment? assignment)
    {
        foreach (var hour in hours)
        {
            if (assignment is null) _assignments.Remove((day, hour));
            else _assignments[(day, hour)] = assignment.Value;
        }
        try
        {
            // The current timetable is edited from today onward (earlier days keep what they had).
            // An earlier timetable is edited over its own dates only, from its first day up to its last.
            var fromDate = _period?.Start ?? Today;
            var until = _period?.End;
            var dayOfWeek = (int)(day == 6 ? DayOfWeek.Sunday : (DayOfWeek)(day + 1));

            // Rebuild the WHOLE day (not just the visible hours) so hidden classes are kept.
            var allHours = Enumerable.Range(DayStartHour, DayEndHour - DayStartHour)
                .Select(h => (Hour: h, Assignment: _assignments.GetValueOrDefault((day, h)))).ToArray();
            var classes = new List<(long SubjectId, SubjectType Type, TimeOnly Start, TimeOnly End)>();
            for (var i = 0; i < allHours.Length;)
            {
                if (allHours[i].Assignment.SubjectId == 0) { i++; continue; }
                var classAssignment = allHours[i].Assignment;
                var start = allHours[i].Hour; var end = start + 1; i++;
                while (i < allHours.Length && allHours[i].Assignment == classAssignment) { end++; i++; }
                classes.Add((classAssignment.SubjectId, classAssignment.Type,
                    new TimeOnly(start, 0), new TimeOnly(end, 0)));
            }
            _timetable.ReplaceDay(dayOfWeek, fromDate, until, classes);
            var updatedPeriod = _timetable.GetHistory().FirstOrDefault(period =>
                period.Start <= fromDate && (period.End is null || period.End > fromDate));
            _hourRangeStart = updatedPeriod?.Start ?? _hourRangeStart;
            _hourSettings.SaveHourRange(_hourRangeStart, _startHour, _endHour);
            SyncAttendance(fromDate, until);
            LoadHistory(_period is null ? null : fromDate);
            ShowSelectedTimetable();
        }
        catch (Exception ex) { MessageBox.Show($"Could not update this day. {ex.Message}", "Timetable update failed", MessageBoxButton.OK, MessageBoxImage.Error); BuildGrid(); }
    }

    // After a timetable change from this date, bring the stored attendance records in line with it:
    // Not Marked records whose class no longer exists are deleted, and missing records for scheduled
    // classes are created. Marked records (Present, Absent, Cancelled) are never touched.
    // untilExclusive is the day after the last changed day (null = the change runs onward);
    // records are only created up to today either way.
    private void SyncAttendance(DateOnly fromDate, DateOnly? untilExclusive = null)
    {
        _attendance.DeleteStaleUnmarkedFrom(fromDate, untilExclusive);

        var semesterStart = _semester.GetSemesterStartDate();
        if (semesterStart is null)
            return;

        var start = fromDate > semesterStart.Value ? fromDate : semesterStart.Value;
        var today = DateOnly.FromDateTime(DateTime.Today);
        var end = untilExclusive is null || untilExclusive.Value.AddDays(-1) > today
            ? today
            : untilExclusive.Value.AddDays(-1);
        _attendance.GenerateMissingBetween(start, end);
    }

    private void HourPicker_TimeCommitted(object? sender, EventArgs e) => ApplyHours();

    // Guards against the message boxes below triggering another commit while they are open.
    private bool _applyingHours;

    // Changing the hours affects only the selected timetable period; marked attendance is preserved.
    private void ApplyHours()
    {
        if (_applyingHours) return;
        _applyingHours = true;
        try
        {
            if (!StartHourPicker.TryGetTime(out var startTime) || !EndHourPicker.TryGetTime(out var endTime))
            {
                MessageBox.Show("Enter whole hours, for example 8 or 08:00, or pick them on the clock.", "Invalid hours",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                ShowHourRange();
                return;
            }

            var start = startTime.Hour;
            var end = endTime.Hour;
            if (start == _startHour && end == _endHour) return;

            if (start >= end)
            {
                MessageBox.Show("The start must be earlier than the end, between 00:00 and 23:00.", "Invalid hours",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                ShowHourRange();
                return;
            }

            var cropFrom = _period?.Start ?? Today;
            var until = _period?.End;
            var affectedDates = until is null
                ? $"{cropFrom:dd MMM yyyy} onward"
                : $"{cropFrom:dd MMM yyyy} to {until.Value.AddDays(-1):dd MMM yyyy}";

            try
            {
                var outside = _timetable.CountOutsideHours(cropFrom, start, end, until);
                if (outside > 0)
                {
                    var answer = MessageBox.Show(
                        $"{outside} weekly {(outside == 1 ? "class falls" : "classes fall")} outside {start:00}:00 to {end:00}:00. " +
                        $"Continuing will shorten or remove {(outside == 1 ? "it" : "them")} for {affectedDates}.\n\n" +
                        "Other timetable periods and marked attendance are not changed. This cannot be undone.\n\nContinue?",
                        "Remove classes outside these hours?", MessageBoxButton.YesNo, MessageBoxImage.Warning);
                    if (answer != MessageBoxResult.Yes)
                    {
                        ShowHourRange();
                        return;
                    }
                }

                _timetable.CropToHours(cropFrom, until, start, end);
                var selectedPeriod = _timetable.GetHistory().FirstOrDefault(period =>
                    period.Start <= cropFrom && (period.End is null || period.End > cropFrom));
                _hourRangeStart = selectedPeriod?.Start ?? _hourRangeStart;
                _hourSettings.SaveHourRange(_hourRangeStart, start, end);
                SyncAttendance(cropFrom, until);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Could not change the hours. {ex.Message}", "Hours not changed",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                ShowHourRange();
                BuildGrid();
                return;
            }

            LoadHistory(_period is null ? null : cropFrom);
            ShowSelectedTimetable();
        }
        finally
        {
            _applyingHours = false;
        }
    }
}