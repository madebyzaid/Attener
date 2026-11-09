using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Attener.Features.Subjects;
using Attener.Features.Timetable;
using Attener.Infrastructure.Database;

namespace Attener.Views;

public partial class TimetableView : UserControl
{
    private readonly SubjectRepository _subjects = new(DatabaseLocation.GetPath());
    private readonly TimetableRepository _timetable = new(DatabaseLocation.GetPath());
    private static readonly string[] Days = ["Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday", "Sunday"];
    private const int StartHour = 8;
    private const int EndHour = 20;
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
        BuildGrid();
    }

    private void LoadAssignments()
    {
        _assignments = [];
        if (EffectiveDatePicker.SelectedDate is not DateTime selected) return;
        var date = DateOnly.FromDateTime(selected);
        foreach (var session in _timetable.GetWeeklySessions(date))
        {
            var start = TimeOnly.Parse(session.StartTime);
            var end = TimeOnly.Parse(session.EndTime);
            var first = Math.Max(StartHour, start.Hour);
            var last = Math.Min(EndHour, (int)Math.Ceiling(end.ToTimeSpan().TotalHours));
            var mondayIndex = session.DayOfWeek == 0 ? 6 : session.DayOfWeek - 1;
            for (var hour = first; hour < last; hour++) _assignments[(mondayIndex, hour)] = session.SubjectId;
        }
    }

    private void BuildGrid()
    {
        LoadAssignments();
        TimetableGrid.Children.Clear(); TimetableGrid.RowDefinitions.Clear(); TimetableGrid.ColumnDefinitions.Clear();
        TimetableGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110) });
        for (var hour = StartHour; hour < EndHour; hour++) TimetableGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 78 });
        TimetableGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(42) });
        for (var day = 0; day < 7; day++) TimetableGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(62) });
        AddCell("Day", 0, 0, true);
        for (var hour = StartHour; hour < EndHour; hour++) AddCell($"{hour:00}:00", hour - StartHour + 1, 0, true);

        for (var day = 0; day < 7; day++)
        {
            AddCell(Days[day], 0, day + 1, true);
            var hour = StartHour;
            while (hour < EndHour)
            {
                _assignments.TryGetValue((day, hour), out var id);

                // Count how many consecutive hours (including this one) have the same subject.
                var span = 1;
                if (id != 0)
                {
                    while (hour + span < EndHour && _assignments.GetValueOrDefault((day, hour + span)) == id)
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
            container.MouseEnter += (_, _) => remove.Visibility = Visibility.Visible;
            container.MouseLeave += (_, _) => remove.Visibility = Visibility.Collapsed;
            container.Children.Add(remove);
        }

        Grid.SetColumn(container, firstHour - StartHour + 1);
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
            var existing = _timetable.GetWeeklySessions(date).Where(s => s.DayOfWeek == dayOfWeek).ToArray();
            foreach (var existingSubject in existing.Select(s => s.SubjectId).Distinct()) _timetable.RemoveWeeklySession(existingSubject, dayOfWeek, date);
            var allHours = Enumerable.Range(StartHour, EndHour - StartHour).Select(h => (Hour: h, Id: _assignments.GetValueOrDefault((day, h)))).ToArray();
            for (var i = 0; i < allHours.Length;)
            {
                if (allHours[i].Id == 0) { i++; continue; }
                var id = allHours[i].Id; var start = allHours[i].Hour; var end = start + 1; i++;
                while (i < allHours.Length && allHours[i].Id == id) { end++; i++; }
                _timetable.SaveWeeklySession(id, dayOfWeek, new TimeOnly(start, 0), new TimeOnly(end, 0), date);
            }
            BuildGrid();
        }
        catch (Exception ex) { MessageBox.Show($"Could not update this day. {ex.Message}", "Timetable update failed", MessageBoxButton.OK, MessageBoxImage.Error); BuildGrid(); }
    }

    private void EffectiveDatePicker_SelectedDateChanged(object sender, SelectionChangedEventArgs e) { if (IsLoaded) BuildGrid(); }
}