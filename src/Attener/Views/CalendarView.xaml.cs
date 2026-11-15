using System.Globalization;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using Attener.Domain;
using Attener.Features.Attendance;
using Attener.Features.Subjects;
using Attener.Infrastructure.Dialogs;
using Attener.Infrastructure.Database;
using System.Windows.Controls.Primitives;

namespace Attener.Views;

public partial class CalendarView : UserControl
{
    private readonly AttendanceRepository _attendance = new(DatabaseLocation.GetPath());
    private readonly CalendarStatusRepository _calendarStatuses = new(DatabaseLocation.GetPath());
    private readonly SemesterSettingsRepository _settings = new(DatabaseLocation.GetPath());
    private readonly SubjectRepository _subjects = new(DatabaseLocation.GetPath());
    private long? _selectedSubjectId;
    private SubjectType? _selectedType;
    private bool _loadingSubjectFilters;
    private bool _selectingTypeFilter;

    public CalendarView()
    {
        InitializeComponent();
        MonthCalendar.AddHandler(UIElement.GotMouseCaptureEvent,
            new System.Windows.Input.MouseEventHandler(ReleaseCalendarCapture));
        LoadSubjectFilters();
        LoadDayColors();
        MonthCalendar.SelectedDate = DateTime.Today;
        UpdateSemesterBlackoutDates();
        LoadSelectedDate();
    }

    public void RefreshData()
    {
        UpdateSemesterBlackoutDates();
        LoadSubjectFilters();
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

    private void LoadSubjectFilters()
    {
        _loadingSubjectFilters = true;
        try
        {
            var subjects = _subjects.GetSubjects(includeArchived: false)
                .Concat(_subjects.GetSubjects(includeArchived: true))
                .ToArray();
            if (_selectedSubjectId is not null && subjects.All(subject => subject.Id != _selectedSubjectId))
            {
                _selectedSubjectId = null;
                _selectedType = null;
            }

            var filters = new List<CalendarSubjectFilter>
            {
                new(null, "All subjects", [], null)
            };
            filters.AddRange(subjects.Select(subject =>
            {
                var label = subject.Name + (subject.IsArchived ? " · Archived" : "");
                var badges = subject.Types.Select(type => new CalendarTypeFilter(
                    subject.Id, type, _selectedSubjectId == subject.Id && _selectedType == type)).ToArray();
                var selectedType = _selectedSubjectId == subject.Id ? _selectedType : null;
                return new CalendarSubjectFilter(subject.Id, label, badges, selectedType);
            }));

            SubjectFilter.ItemsSource = filters;
            SubjectFilter.SelectedValue = _selectedSubjectId;
            if (_selectedSubjectId is null)
                SubjectFilter.SelectedIndex = 0;
        }
        finally
        {
            _loadingSubjectFilters = false;
        }
    }

    private static string TypeLetter(SubjectType type) => type switch
    {
        SubjectType.Lecture => "L",
        SubjectType.Tutorial => "T",
        SubjectType.Practical => "P",
        _ => throw new ArgumentOutOfRangeException(nameof(type))
    };

    private void SubjectFilter_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded || _loadingSubjectFilters || _selectingTypeFilter
            || SubjectFilter.SelectedItem is not CalendarSubjectFilter filter)
            return;

        ApplySubjectFilter(filter.SubjectId, null);
    }

    private void SubjectNameFilter_MouseLeftButtonUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: CalendarSubjectFilter filter }
            || (_selectedSubjectId == filter.SubjectId && _selectedType is null))
            return;

        e.Handled = true;
        ApplySubjectFilter(filter.SubjectId, null);
    }

    private void SubjectTypeBadge_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: CalendarTypeFilter filter })
            return;

        e.Handled = true;
        ApplySubjectFilter(filter.SubjectId, filter.Type);
    }

    private void ApplySubjectFilter(long? subjectId, SubjectType? type)
    {
        _selectedSubjectId = subjectId;
        _selectedType = type;
        _selectingTypeFilter = true;
        SubjectFilter.SelectedValue = subjectId;
        if (subjectId is null)
            SubjectFilter.SelectedIndex = 0;
        SubjectFilter.IsDropDownOpen = false;
        _selectingTypeFilter = false;
        UpdateSelectedTypeBadges();
        LoadDayColors();
        LoadSelectedDate();
    }

    private void UpdateSelectedTypeBadges()
    {
        if (SubjectFilter.ItemsSource is not IEnumerable<CalendarSubjectFilter> filters)
            return;

        foreach (var filter in filters)
        {
            filter.SelectedType = filter.SubjectId == _selectedSubjectId ? _selectedType : null;
            foreach (var badge in filter.TypeBadges)
                badge.IsSelected = badge.SubjectId == _selectedSubjectId && badge.Type == _selectedType;
        }
    }

    // When a subject or class type is chosen, give the calendar its date -> status map.
    // Each day cell reads that map (through the calendar's Tag) and colours itself.
    private void LoadDayColors()
    {
        MonthCalendar.Tag = _selectedSubjectId is null
            ? null
            : _calendarStatuses.GetStatusesForSubject(_selectedSubjectId.Value, _selectedType);
        LegendPanel.Visibility = _selectedSubjectId is null ? Visibility.Collapsed : Visibility.Visible;
    }

    private static void ReleaseCalendarCapture(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (e.OriginalSource is CalendarDayButton or CalendarItem)
            ((UIElement)e.OriginalSource).ReleaseMouseCapture();
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

    private sealed class CalendarSubjectFilter : INotifyPropertyChanged
    {
        private SubjectType? _selectedType;

        public long? SubjectId { get; }
        public string Label { get; }
        public IReadOnlyList<CalendarTypeFilter> TypeBadges { get; }
        public string? SelectedTypeLetter => _selectedType is SubjectType type ? TypeLetter(type) : null;
        public Visibility SelectedTypeVisibility => _selectedType is null ? Visibility.Collapsed : Visibility.Visible;

        public SubjectType? SelectedType
        {
            get => _selectedType;
            set
            {
                if (_selectedType == value)
                    return;
                _selectedType = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SelectedTypeLetter)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SelectedTypeVisibility)));
            }
        }

        public CalendarSubjectFilter(long? subjectId, string label,
            IReadOnlyList<CalendarTypeFilter> typeBadges, SubjectType? selectedType)
        {
            SubjectId = subjectId;
            Label = label;
            TypeBadges = typeBadges;
            _selectedType = selectedType;
        }

        public override string ToString() => Label;
        public event PropertyChangedEventHandler? PropertyChanged;
    }

    private sealed class CalendarTypeFilter : INotifyPropertyChanged
    {
        private bool _isSelected;

        public long SubjectId { get; }
        public SubjectType Type { get; }
        public string Letter => TypeLetter(Type);
        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (_isSelected == value)
                    return;
                _isSelected = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
            }
        }

        public CalendarTypeFilter(long subjectId, SubjectType type, bool isSelected)
        {
            SubjectId = subjectId;
            Type = type;
            _isSelected = isSelected;
        }

        public event PropertyChangedEventHandler? PropertyChanged;
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
