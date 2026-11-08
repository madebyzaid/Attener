using System.Windows;
using System.Windows.Controls;

namespace AttendanceApp;

public partial class MainWindow : Window
{
    public MainWindow() => InitializeComponent();

    private void Navigate_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string page })
            return;

        OverviewPage.Visibility = page == "Overview" ? Visibility.Visible : Visibility.Collapsed;
        AttendancePage.Visibility = page == "Attendance" ? Visibility.Visible : Visibility.Collapsed;
        SubjectsPage.Visibility = page == "Subjects" ? Visibility.Visible : Visibility.Collapsed;
        TimetablePage.Visibility = page == "Timetable" ? Visibility.Visible : Visibility.Collapsed;
        CalendarPage.Visibility = page == "Calendar" ? Visibility.Visible : Visibility.Collapsed;
        StatisticsPage.Visibility = page == "Statistics" ? Visibility.Visible : Visibility.Collapsed;

        if (page == "Overview") OverviewPage.RefreshData();
        if (page == "Attendance") AttendancePage.RefreshData();
        if (page == "Timetable") TimetablePage.RefreshData();
        if (page == "Subjects") SubjectsPage.RefreshData();
        if (page == "Calendar") CalendarPage.RefreshData();
        if (page == "Statistics") StatisticsPage.RefreshData();
    }
}

