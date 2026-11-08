using System.Windows;
using System.Windows.Controls;
using AttendanceApp.Features.Attendance;
using AttendanceApp.Infrastructure.Database;

namespace AttendanceApp.Views;

public partial class OverviewView : UserControl
{
    private readonly SemesterSettingsRepository _settings = new(DatabaseLocation.GetPath());
    private readonly AttendanceRepository _attendance = new(DatabaseLocation.GetPath());

    public OverviewView()
    {
        InitializeComponent();
        var startDate = _settings.GetSemesterStartDate();
        if (startDate is not null)
            SemesterStartPicker.SelectedDate = startDate.Value.ToDateTime(TimeOnly.MinValue);
    }

    private void GenerateRecords_Click(object sender, RoutedEventArgs e)
    {
        if (SemesterStartPicker.SelectedDate is not DateTime selectedDate)
        {
            MessageBox.Show("Choose the semester start date first.", "Date required",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var startDate = DateOnly.FromDateTime(selectedDate);
        _settings.SaveSemesterStartDate(startDate);
        var today = DateOnly.FromDateTime(DateTime.Today);
        var created = _attendance.GenerateMissingBetween(startDate, today);
        GenerationMessage.Text = startDate > today
            ? "The semester start is in the future. Historical records will be generated when that date arrives."
            : $"Semester start saved. {created} historical class record(s) were created; existing attendance was preserved.";
    }
}
