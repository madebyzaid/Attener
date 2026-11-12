using System.Windows;
using System.Windows.Controls;

namespace Attener;

public partial class MainWindow : Window
{
    public MainWindow() => InitializeComponent();

    public void ShowAddAttendanceEntry(DateTime date, bool lockDate) => AttendancePage.AddManualForDate(date, lockDate);

    private void Navigate_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not RadioButton { Tag: string page })
            return;

        AttendancePage.Visibility = page == "Attendance" ? Visibility.Visible : Visibility.Collapsed;
        StatisticsPage.Visibility = page == "Statistics" ? Visibility.Visible : Visibility.Collapsed;
        TimetablePage.Visibility = page == "Timetable" ? Visibility.Visible : Visibility.Collapsed;
        CalendarPage.Visibility = page == "Calendar" ? Visibility.Visible : Visibility.Collapsed;
        SubjectsPage.Visibility = page == "Subjects" ? Visibility.Visible : Visibility.Collapsed;
        SettingsPage.Visibility = page == "Settings" ? Visibility.Visible : Visibility.Collapsed;

        if (page == "Attendance") AttendancePage.RefreshData();
        if (page == "Statistics") StatisticsPage.RefreshData();
        if (page == "Timetable") TimetablePage.RefreshData();
        if (page == "Calendar") CalendarPage.RefreshData();
        if (page == "Subjects") SubjectsPage.RefreshData();
        if (page == "Settings") SettingsPage.RefreshData();
    }
}
