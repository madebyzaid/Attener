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

    // Hours of the day that can hold classes. Stored times are HH:mm, so the day ends at 23:00.
    private const int DayStartHour = 0;
    private const int DayEndHour = TimetableSettingsRepository.LatestEndHour;

    // The hours currently SHOWN in the grid (user-chosen). Changing them crops classes from the selected date onward;
    // classes on earlier dates that lie outside the range are kept, just hidden.
    private int _startHour = TimetableSettingsRepository.DefaultStartHour;
    private int _endHour = TimetableSettingsRepository.DefaultEndHour;

    private IReadOnlyList<SubjectSummary> _activeSubjects = [];
    private Dictionary<(int Day, int Hour), long> _assignments = [];

    public TimetableView()
    {
        InitializeComponent();
        EffectiveDatePicker.SelectedDate = DateTime.Today;
        RefreshData();
    }

    public void RefreshData()
    {
        _activeSubjects = _subjects.GetSubjects(includeArchived: false);
        (_startHour, _endHour) = _hourSettings.GetHourRange();
        ShowHourRange();
        BuildGrid();
        ShowEndDate();
    }

    private void ShowHourRange()
    {
        StartHourPicker.Time = new TimeOnly(_startHour, 0);
        EndHourPicker.Time = new TimeOnly(_endHour, 0);
    }

    // Shows the end date (if any) of the timetable that is in force on the "effective from" date.
    private void ShowEndDate()
    {
        if (EffectiveDatePicker.SelectedDate is not DateTime selected) return;
        var end = _timetable.GetEndDate(DateOnly.FromDateTime(selected));
        EndDatePicker.SelectedDate = end?.ToDateTime(TimeOnly.MinValue);
        EndDateStatus.Text = end is null ? "No end date" : $"Ends on {end:dd MMM yyyy}";
    }

    // Everything on this page works from the "Changes effective from" date. Without it nothing is
    // changed, and the person is told why.
    private bool TryGetStartDate(out DateOnly date)
    {
        if (EffectiveDatePicker.SelectedDate is DateTime selected)
        {
            date = DateOnly.FromDateTime(selected);
            return true;
        }

        date = default;
        MessageBox.Show("A start date is required. Pick the date these changes take effect first.", "Start date required",
            MessageBoxButton.OK, MessageBoxImage.Information);
        return false;
    }

    // Loads every class of the day, including hours outside the visible range,
    // so editing a day never throws away classes that are currently hidden.
    private void LoadAssignments()
    {
        _assignments = [];
        if (EffectiveDatePicker.SelectedDate is not DateTime selected) return;
        var date = DateOnly.FromDateTime(selected);
        foreach (var session in _timetable.GetWeeklySessions(date))
        {
            var start = TimeOnly.Parse(session.StartTime);
            var end = TimeOnly.Parse(session.EndTime);
            var first = Math.Max(DayStartHour, start.Hour);
            var last = Math.Min(DayEndHour, (int)Math.Ceiling(end.ToTimeSpan().TotalHours));
            var mondayIndex = session.DayOfWeek == 0 ? 6 : session.DayOfWeek - 1;
            for (var hour = first; hour < last; hour++) _assignments[(mondayIndex, hour)] = session.SubjectId;
        }
    }

    private void BuildGrid()
    {
        LoadAssignments();
        TimetableGrid.Children.Clear(); TimetableGrid.RowDefinitions.Clear(); TimetableGrid.ColumnDefinitions.Clear();
        TimetableGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110) });
        for (var hour = _startHour; hour < _endHour; hour++) TimetableGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 78 });
        TimetableGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(42) });
        for (var day = 0; day < 7; day++) TimetableGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(62) });
        AddCell("Day", 0, 0, true);
        for (var hour = _startHour; hour < _endHour; hour++) AddCell($"{hour:00}:00", hour - _startHour + 1, 0, true);

        for (var day = 0; day < 7; day++)
        {
            AddCell(Days[day], 0, day + 1, true);
            var hour = _startHour;
            while (hour < _endHour)
            {
                _assignments.TryGetValue((day, hour), out var id);

                // Count how many consecutive visible hours (including this one) have the same subject.
                var span = 1;
                if (id != 0)
                {
                    while (hour + span < _endHour && _assignments.GetValueOrDefault((day, hour + span)) == id)
                        span++;
                }

                AddHourButton(day, hour, span, id);
                hour += span;
            }
        }
    }

    private void AddHourButton(int day, int firstHour, int span, long id)
    {
        var name = _activeSubjects.FirstOrDefault(s => s.Id == id)?.Name ?? "";
        var button = new Button
        {
            Content = name,
            Tag = (day, firstHour, span),
            Padding = new Thickness(4),
            FontSize = 11,
            Background = id == 0 ? (Brush)FindResource("SurfaceBackgroundBrush") : (Brush)FindResource("AccentBrush"),
            Foreground = id == 0 ? (Brush)FindResource("SecondaryTextBrush") : Brushes.Black,
            BorderThickness = new Thickness(0),
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
                Background = new SolidColorBrush(Color.FromRgb(0x11, 0x13, 0x18)),
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
            var hoverBackground = new SolidColorBrush(Color.FromRgb(0x34, 0x3B, 0x49));
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

        Grid.SetColumn(container, firstHour - _startHour + 1);
        Grid.SetColumnSpan(container, span);
        Grid.SetRow(container, day + 1);
        TimetableGrid.Children.Add(container);
    }

    private void AddCell(string text, int column, int row, bool header)
    {
        var block = new TextBlock { Text = text, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, FontWeight = FontWeights.SemiBold, Foreground = header ? (Brush)FindResource("PrimaryTextBrush") : Brushes.White };
        var border = new Border { Child = block, Background = header ? (Brush)FindResource("SurfaceBackgroundBrush") : Brushes.Transparent, Margin = new Thickness(1) };
        Grid.SetColumn(border, column); Grid.SetRow(border, row); TimetableGrid.Children.Add(border);
    }

    private void Cell_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: ValueTuple<int, int, int> block } button) return;
        if (!TryGetStartDate(out _)) return;
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
            var item = new MenuItem { Header = subject.Name }; var id = subject.Id;
            item.Click += (_, _) => ChangeHours(cell.Day, [cell.Hour], id); menu.Items.Add(item);
        }
        menu.PlacementTarget = button; menu.IsOpen = true;
    }

    private void RemoveBlock_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: ValueTuple<int, int, int> block }) return;
        if (!TryGetStartDate(out _)) return;
        var (day, firstHour, span) = block;
        // Remove every hour that belongs to this block, not just one.
        ChangeHours(day, Enumerable.Range(firstHour, span).ToArray(), null);
    }

    private void ChangeHours(int day, int[] hours, long? subjectId)
    {
        if (EffectiveDatePicker.SelectedDate is not DateTime effective) return;
        foreach (var hour in hours)
        {
            if (subjectId is null) _assignments.Remove((day, hour)); else _assignments[(day, hour)] = subjectId.Value;
        }
        try
        {
            var date = DateOnly.FromDateTime(effective);
            var dayOfWeek = (int)(day == 6 ? DayOfWeek.Sunday : (DayOfWeek)(day + 1));

            // Read the end date BEFORE removing anything, so the rewritten classes keep it.
            var lastDay = _timetable.GetEndDate(date);

            var existing = _timetable.GetWeeklySessions(date).Where(s => s.DayOfWeek == dayOfWeek).ToArray();
            foreach (var existingSubject in existing.Select(s => s.SubjectId).Distinct()) _timetable.RemoveWeeklySession(existingSubject, dayOfWeek, date);

            // Rebuild the WHOLE day (not just the visible hours) so hidden classes are kept.
            var allHours = Enumerable.Range(DayStartHour, DayEndHour - DayStartHour).Select(h => (Hour: h, Id: _assignments.GetValueOrDefault((day, h)))).ToArray();
            for (var i = 0; i < allHours.Length;)
            {
                if (allHours[i].Id == 0) { i++; continue; }
                var id = allHours[i].Id; var start = allHours[i].Hour; var end = start + 1; i++;
                while (i < allHours.Length && allHours[i].Id == id) { end++; i++; }
                _timetable.SaveWeeklySession(id, dayOfWeek, new TimeOnly(start, 0), new TimeOnly(end, 0), date, lastDay);
            }
            SyncAttendance(date);
            BuildGrid();
        }
        catch (Exception ex) { MessageBox.Show($"Could not update this day. {ex.Message}", "Timetable update failed", MessageBoxButton.OK, MessageBoxImage.Error); BuildGrid(); }
    }

    // After a timetable change from this date, bring the stored attendance records in line with it:
    // Not Marked records whose class no longer exists are deleted, and missing records for scheduled
    // classes are created. Marked records (Present, Absent, Cancelled) are never touched.
    private void SyncAttendance(DateOnly fromDate)
    {
        _attendance.DeleteStaleUnmarkedFrom(fromDate);

        var semesterStart = _semester.GetSemesterStartDate();
        if (semesterStart is null)
            return;

        var start = fromDate > semesterStart.Value ? fromDate : semesterStart.Value;
        _attendance.GenerateMissingBetween(start, DateOnly.FromDateTime(DateTime.Today));
    }

    // Saves the end date. An empty end date means "no end date". It needs the date the changes take effect.
    private void SaveEndDate_Click(object sender, RoutedEventArgs e)
    {
        if (!TryGetStartDate(out var effectiveDate)) return;

        if (EndDatePicker.SelectedDate is not DateTime end)
        {
            RemoveEndDate(effectiveDate);
            return;
        }

        var lastDay = DateOnly.FromDateTime(end);
        if (lastDay < effectiveDate)
        {
            MessageBox.Show($"The end date can't be before the date these changes take effect ({effectiveDate:dd MMM yyyy}).",
                "Invalid end date", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        try
        {
            var changed = _timetable.SetEndDate(effectiveDate, lastDay);
            SyncAttendance(effectiveDate);
            if (changed == 0)
            {
                MessageBox.Show("No classes run from this date onward, so there is nothing to end.",
                    "Nothing to end", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not set the end date. {ex.Message}", "End date failed",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }

        BuildGrid();
        ShowEndDate();
    }

    private void RemoveEndDate(DateOnly effectiveDate)
    {
        try
        {
            _timetable.ClearEndDate(effectiveDate);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not remove the end date. {ex.Message}", "End date failed",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }

        BuildGrid();
        ShowEndDate();
    }

    private void HourPicker_TimeCommitted(object? sender, EventArgs e) => ApplyHours();

    // Guards against the message boxes below triggering another commit while they are open.
    private bool _applyingHours;

    // Changing the hours takes effect straight away. Classes that fall outside the new hours are
    // shortened or removed from the selected date onward; earlier dates and attendance are not touched.
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

            if (!TryGetStartDate(out var cropFrom))
            {
                ShowHourRange();
                return;
            }

            try
            {
                var outside = _timetable.CountOutsideHours(cropFrom, start, end);
                if (outside > 0)
                {
                    var answer = MessageBox.Show(
                        $"{outside} weekly {(outside == 1 ? "class falls" : "classes fall")} outside {start:00}:00 to {end:00}:00. " +
                        $"Continuing will shorten or remove {(outside == 1 ? "it" : "them")} from {cropFrom:dd MMM yyyy} onward.\n\n" +
                        "Earlier dates and attendance records are not changed. This cannot be undone.\n\nContinue?",
                        "Remove classes outside these hours?", MessageBoxButton.YesNo, MessageBoxImage.Warning);
                    if (answer != MessageBoxResult.Yes)
                    {
                        ShowHourRange();
                        return;
                    }
                }

                _timetable.CropToHours(cropFrom, start, end);
                _hourSettings.SaveHourRange(start, end);
                SyncAttendance(cropFrom);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Could not change the hours. {ex.Message}", "Hours not changed",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                ShowHourRange();
                BuildGrid();
                return;
            }

            _startHour = start;
            _endHour = end;
            ShowHourRange();
            BuildGrid();
        }
        finally
        {
            _applyingHours = false;
        }
    }

    private void EffectiveDatePicker_SelectedDateChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded) return;
        // An empty date has no timetable to show. Leave the grid as it is instead of blanking it,
        // so it does not look like the classes were deleted.
        if (EffectiveDatePicker.SelectedDate is null) return;
        BuildGrid();
        ShowEndDate();
    }
}