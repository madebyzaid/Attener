using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Data;
using Attener.Domain;
using Attener.Features.Attendance;
using Attener.Features.Subjects;
using Attener.Infrastructure.Dialogs;
using System.Windows.Media;
using Attener.Infrastructure.Database;

namespace Attener.Views;

public partial class CalendarView : UserControl
{
    private readonly AttendanceRepository _attendance = new(DatabaseLocation.GetPath());
    private readonly CalendarStatusRepository _calendarStatuses = new(DatabaseLocation.GetPath());
    private readonly SemesterSettingsRepository _settings = new(DatabaseLocation.GetPath());
    private readonly SubjectRepository _subjects = new(DatabaseLocation.GetPath());
    private long? _selectedSubjectId;
    private SubjectType? _selectedType;
    private string _selectedSubjectLabel = "All subjects";

    public CalendarView()
    {
        InitializeComponent();
        UpdateSubjectFilterLabel();
        LoadDayColors();
        MonthCalendar.SelectedDate = DateTime.Today;
        UpdateSemesterBlackoutDates();
        LoadSelectedDate();
    }

    public void RefreshData()
    {
        UpdateSemesterBlackoutDates();
        UpdateSubjectFilterLabel();
        LoadDayColors();
        LoadSelectedDate();
    }

    private void UpdateSemesterBlackoutDates()
    {
        MonthCalendar.BlackoutDates.Clear();
        var semesterStart = _settings.GetSemesterStartDate();
        if (semesterStart is null)
            return;

        var semesterStartDate = semesterStart.Value.ToDateTime(TimeOnly.MinValue);
        var selectedDate = MonthCalendar.SelectedDate ?? DateTime.Today;
        if (selectedDate.Date < semesterStartDate)
            selectedDate = DateTime.Today < semesterStartDate ? semesterStartDate : DateTime.Today;
        MonthCalendar.SelectedDate = selectedDate;

        if (semesterStartDate == DateTime.MinValue)
            return;

        var blackoutStart = new DateTime(2000, 1, 1);
        if (semesterStartDate < blackoutStart)
            blackoutStart = DateTime.MinValue;

        var blackoutEnd = semesterStartDate.AddDays(-1);
        if (blackoutEnd >= blackoutStart)
            MonthCalendar.BlackoutDates.Add(new CalendarDateRange(blackoutStart, blackoutEnd));
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

        SubjectFilterLabel.Text = _selectedSubjectLabel;
    }

    private void SubjectFilterButton_Click(object sender, RoutedEventArgs e)
    {
        SubjectFilterList.Children.Clear();
        SubjectFilterList.Children.Add(CreateFilterRow("All subjects", null, null, null));

        var subjects = _subjects.GetSubjects(includeArchived: false)
            .Concat(_subjects.GetSubjects(includeArchived: true));
        foreach (var subject in subjects)
        {
            var label = subject.Name + (subject.IsArchived ? " · Archived" : "");
            SubjectFilterList.Children.Add(CreateFilterRow(label, subject.Id, null, subject.Types));
        }

        SubjectFilterPopup.IsOpen = true;
    }

    private Grid CreateFilterRow(string label, long? subjectId, SubjectType? rowType,
        IReadOnlyList<SubjectType>? availableTypes)
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
        name.Click += (_, _) => SelectSubjectFilter(subjectId, rowType, label);
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
        SubjectFilterLabel.Text = _selectedSubjectLabel;
        SubjectFilterPopup.IsOpen = false;
        LoadDayColors();
        LoadSelectedDate();
    }

    private static string TypeLetter(SubjectType type) => type switch
    {
        SubjectType.Lecture => "L",
        SubjectType.Tutorial => "T",
        SubjectType.Practical => "P",
        _ => throw new ArgumentOutOfRangeException(nameof(type))
    };

    // When a subject or class type is chosen, give the calendar its date -> status map.
    // Each day cell reads that map (through the calendar's Tag) and colours itself.
    private void LoadDayColors()
    {
        MonthCalendar.Tag = _selectedSubjectId is null
            ? null
            : _calendarStatuses.GetStatusesForSubject(_selectedSubjectId.Value, _selectedType);
        LegendPanel.Visibility = _selectedSubjectId is null ? Visibility.Collapsed : Visibility.Visible;
    }

    private void MonthCalendar_SelectedDatesChanged(object sender, SelectionChangedEventArgs e)
    {
        LoadSelectedDate();
    }

    private void LoadSelectedDate()
    {
        if (MonthCalendar.SelectedDate is not DateTime selectedDate)
            return;

        SelectedDateHeading.Text = selectedDate.ToString("dddd, d MMMM yyyy");
        var date = DateOnly.FromDateTime(selectedDate);
        var today = DateOnly.FromDateTime(DateTime.Today);
        var semesterStart = _settings.GetSemesterStartDate();
        if (date <= today && semesterStart is not null && date >= semesterStart.Value)
            _attendance.GenerateMissingBetween(date, date);

        var rows = _attendance.GetAttendanceForDate(date, allowUnmarked: true)
            .Where(entry => _selectedSubjectId is null || entry.SubjectId == _selectedSubjectId)
            .Where(entry => _selectedType is null || entry.Type == _selectedType)
            .Select(entry => new CalendarSessionRow(entry,
                entry.IsManual
                    ? $"Manual · {(entry.ScheduledDurationMinutes / 60m).ToString("0.##", CultureInfo.InvariantCulture)} h"
                    : entry.ScheduledStart is null || entry.ScheduledEnd is null
                        ? "Time unavailable"
                        : $"{entry.ScheduledStart}–{entry.ScheduledEnd}",
                date > today || (semesterStart is not null && date >= semesterStart.Value)))
            .OrderBy(row => row.TimeRange).ToArray();

        SessionsList.ItemsSource = rows;
        EmptyStateText.Visibility = rows.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void Present_Click(object sender, RoutedEventArgs e) => SetStatus(sender, AttendanceStatus.Present);
    private void Absent_Click(object sender, RoutedEventArgs e) => SetStatus(sender, AttendanceStatus.Absent);
    private void Cancelled_Click(object sender, RoutedEventArgs e) => SetStatus(sender, AttendanceStatus.Cancelled);

    private void SetStatus(object sender, AttendanceStatus clicked)
    {
        if (sender is not FrameworkElement { DataContext: CalendarSessionRow row }) return;

        if (row.Entry.IsManual && row.SelectedStatus == clicked)
        {
            var answer = InAppDialogService.Show(this,
                $"Delete this {row.Entry.SubjectName} entry for {row.Entry.AttendanceDate:dd MMM yyyy}? This cannot be undone.",
                "Delete entry", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No);
            if (answer != MessageBoxResult.Yes) return;

            _attendance.DeleteManualEntry(row.Entry.Id);
            RefreshAttendanceViews();
            return;
        }

        _attendance.SaveAttendance(row.Entry,
            row.SelectedStatus == clicked ? AttendanceStatus.NotMarked : clicked);
        RefreshAttendanceViews();
    }

    private void DeleteManual_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: CalendarSessionRow row } || !row.Entry.IsManual) return;
        var answer = InAppDialogService.Show(this, $"Delete this {row.Entry.SubjectName} entry for {row.Entry.AttendanceDate:dd MMM yyyy}? This cannot be undone.",
            "Delete entry", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No);
        if (answer != MessageBoxResult.Yes) return;
        _attendance.DeleteManualEntry(row.Entry.Id);
        RefreshAttendanceViews();
    }

    private void AddManual_Click(object sender, RoutedEventArgs e)
    {
        if (MonthCalendar.SelectedDate is not DateTime date) return;
        if (Application.Current.MainWindow is MainWindow window)
            window.ShowAddAttendanceEntry(date, lockDate: true);
        RefreshAttendanceViews();
    }

    private void RefreshAttendanceViews()
    {
        LoadSelectedDate();
        LoadDayColors();
    }

    private sealed class CalendarSessionRow
    {
        public AttendanceEntry Entry { get; }
        public string SubjectName => Entry.SubjectName;
        public string Professor => Entry.Professor;
        public string Venue => Entry.Venue;
        public bool HasProfessor => Entry.HasProfessor;
        public bool HasVenue => Entry.HasVenue;
        public string TimeRange { get; }
        public bool CanEdit { get; }
        public AttendanceStatus SelectedStatus => Entry.Status ?? AttendanceStatus.NotMarked;
        public bool IsPresent => SelectedStatus == AttendanceStatus.Present;
        public bool IsAbsent => SelectedStatus == AttendanceStatus.Absent;
        public bool IsCancelled => SelectedStatus == AttendanceStatus.Cancelled;
        public Visibility ManualVisibility => Entry.IsManual ? Visibility.Visible : Visibility.Collapsed;
        public Visibility DeleteVisibility => Entry.IsManual ? Visibility.Visible : Visibility.Collapsed;
        public CalendarSessionRow(AttendanceEntry entry, string timeRange, bool canEdit)
        { Entry = entry; TimeRange = timeRange; CanEdit = canEdit; }
    }
}

// Used by the Calendar page's day cells (see StatusDayButtonStyle in CalendarView.xaml).
// Input 1: the day's date. Input 2: the calendar's Tag, a date -> status map (or null).
// Output: that day's status word ("Present", "Absent", ...), or "" when there is none.
public sealed class DayStatusConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values.Length == 2
            && values[0] is DateTime date
            && values[1] is IReadOnlyDictionary<DateTime, string> statuses
            && statuses.TryGetValue(date.Date, out var status))
        {
            return status;
        }

        return "";
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
