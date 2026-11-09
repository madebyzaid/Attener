using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Attener.Features.Attendance;
using Attener.Features.Settings;
using Attener.Infrastructure.Database;

namespace Attener.Views;

public partial class SettingsView : UserControl
{
    private readonly SemesterSettingsRepository _settings = new(DatabaseLocation.GetPath());
    private readonly AttendanceRepository _attendance = new(DatabaseLocation.GetPath());
    private readonly DataResetRepository _reset = new(DatabaseLocation.GetPath());

    public SettingsView()
    {
        InitializeComponent();
        RefreshData();
    }

    public void RefreshData()
    {
        var startDate = _settings.GetSemesterStartDate();
        SemesterStartPicker.SelectedDate = startDate?.ToDateTime(TimeOnly.MinValue);
        GenerationMessage.Text = "";
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
        var today = DateOnly.FromDateTime(DateTime.Today);
        _settings.SaveSemesterStartDate(startDate);
        var created = _attendance.GenerateMissingBetween(startDate, today);

        GenerationMessage.Text = startDate > today
            ? "The semester start is in the future. Historical records will be generated when that date arrives."
            : $"Semester start saved. {created} pending class record(s) were created; existing attendance was preserved.";
    }

    private void DeleteAllData_Click(object sender, RoutedEventArgs e)
    {
        if (!ConfirmDeleteAll())
            return;

        try
        {
            _reset.DeleteAllData();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not delete the data. {ex.Message}", "Delete failed",
                MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        RefreshData();
        MessageBox.Show("All data has been deleted.", "Done",
            MessageBoxButton.OK, MessageBoxImage.Information);
    }

    // Small pop-up: the red button only turns on after the user types DELETE exactly.
    private bool ConfirmDeleteAll()
    {
        var input = new TextBox { Width = 300, Margin = new Thickness(0, 12, 0, 16), HorizontalAlignment = HorizontalAlignment.Left };

        var confirm = new Button
        {
            Content = "Delete everything",
            Padding = new Thickness(14, 7, 14, 7),
            IsEnabled = false,
            Background = new SolidColorBrush(Color.FromRgb(0xD6, 0x45, 0x50)),
            Foreground = Brushes.White,
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
        buttons.Children.Add(confirm);

        var panel = new StackPanel { Margin = new Thickness(20) };
        panel.Children.Add(new TextBlock
        {
            Text = "This permanently deletes all subjects, timetable entries, attendance records and settings.",
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 340
        });
        panel.Children.Add(new TextBlock
        {
            Text = "Type DELETE to confirm.",
            Foreground = (Brush)FindResource("SecondaryTextBrush"),
            Margin = new Thickness(0, 12, 0, 0)
        });
        panel.Children.Add(input);
        panel.Children.Add(buttons);

        var dialog = new Window
        {
            Title = "Delete all data",
            Content = panel,
            SizeToContent = SizeToContent.WidthAndHeight,
            ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = Window.GetWindow(this),
            Background = (Brush)FindResource("SurfaceBackgroundBrush")
        };

        input.TextChanged += (_, _) => confirm.IsEnabled = string.Equals(input.Text.Trim(), "DELETE", StringComparison.Ordinal);
        confirm.Click += (_, _) => dialog.DialogResult = true;

        return dialog.ShowDialog() == true;
    }
}