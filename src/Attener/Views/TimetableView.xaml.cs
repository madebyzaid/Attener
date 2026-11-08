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
            for (var hour = StartHour; hour < EndHour; hour++)
            {
                _assignments.TryGetValue((day, hour), out var id);
                var name = _activeSubjects.FirstOrDefault(s => s.Id == id)?.Name ?? "＋";
                var button = new Button { Content = name, Tag = (day, hour), Margin = new Thickness(2), Padding = new Thickness(4), FontSize = 11, Background = id == 0 ? (Brush)FindResource("SurfaceBackgroundBrush") : (Brush)FindResource("AccentBrush"), Foreground = id == 0 ? (Brush)FindResource("SecondaryTextBrush") : Brushes.Black, BorderThickness = new Thickness(0), Cursor = Cursors.Hand, ToolTip = $"{Days[day]} {hour:00}:00–{hour + 1:00}:00" };
                button.Click += Cell_Click; Grid.SetColumn(button, hour - StartHour + 1); Grid.SetRow(button, day + 1); TimetableGrid.Children.Add(button);
            }
        }
    }

    private void AddCell(string text, int column, int row, bool header)
    {
        var block = new TextBlock { Text = text, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, FontWeight = FontWeights.SemiBold, Foreground = header ? (Brush)FindResource("PrimaryTextBrush") : Brushes.White };
        var border = new Border { Child = block, Background = header ? (Brush)FindResource("SurfaceBackgroundBrush") : Brushes.Transparent, Margin = new Thickness(1) };
        Grid.SetColumn(border, column); Grid.SetRow(border, row); TimetableGrid.Children.Add(border);
    }

    private void Cell_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: ValueTuple<int, int> cell }) return;
        var menu = new ContextMenu();
        var clear = new MenuItem { Header = "Clear hour" }; clear.Click += (_, _) => ChangeCell(cell, null); menu.Items.Add(clear);
        foreach (var subject in _activeSubjects)
        {
            var item = new MenuItem { Header = subject.Name }; var id = subject.Id;
            item.Click += (_, _) => ChangeCell(cell, id); menu.Items.Add(item);
        }
        menu.PlacementTarget = (UIElement)sender; menu.IsOpen = true;
    }

    private void ChangeCell((int Day, int Hour) cell, long? subjectId)
    {
        if (EffectiveDatePicker.SelectedDate is not DateTime effective) return;
        if (subjectId is null) _assignments.Remove(cell); else _assignments[cell] = subjectId.Value;
        try
        {
            var date = DateOnly.FromDateTime(effective);
            var dayOfWeek = (int)(cell.Day == 6 ? DayOfWeek.Sunday : (DayOfWeek)(cell.Day + 1));
            var existing = _timetable.GetWeeklySessions(date).Where(s => s.DayOfWeek == dayOfWeek).ToArray();
            foreach (var existingSubject in existing.Select(s => s.SubjectId).Distinct()) _timetable.RemoveWeeklySession(existingSubject, dayOfWeek, date);
            var hours = Enumerable.Range(StartHour, EndHour - StartHour).Select(h => (Hour: h, Id: _assignments.GetValueOrDefault((cell.Day, h)))).ToArray();
            for (var i = 0; i < hours.Length;)
            {
                if (hours[i].Id == 0) { i++; continue; }
                var id = hours[i].Id; var start = hours[i].Hour; var end = start + 1; i++;
                while (i < hours.Length && hours[i].Id == id) { end++; i++; }
                _timetable.SaveWeeklySession(id, dayOfWeek, new TimeOnly(start, 0), new TimeOnly(end, 0), date);
            }
            BuildGrid();
        }
        catch (Exception ex) { MessageBox.Show($"Could not update this day. {ex.Message}", "Timetable update failed", MessageBoxButton.OK, MessageBoxImage.Error); BuildGrid(); }
    }

    private void EffectiveDatePicker_SelectedDateChanged(object sender, SelectionChangedEventArgs e) { if (IsLoaded) BuildGrid(); }
}


