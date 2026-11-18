using System.Windows;
using System.Windows.Controls;
using Attener.Domain;

namespace Attener.Controls;

// One class / entry row, shared by the Attendance and Calendar pages. It only draws the row and
// raises events; the page that hosts it decides what to save. The row object it shows is its
// DataContext (it must have Entry, TimeText, CanEdit, IsPresent/IsAbsent/IsCancelled,
// and DeleteVisibility).
public partial class AttendanceRowCard : UserControl
{
    public event EventHandler<AttendanceStatus>? StatusClicked;
    public event EventHandler? DeleteClicked;

    public AttendanceRowCard() => InitializeComponent();

    private void Present_Click(object sender, RoutedEventArgs e) => StatusClicked?.Invoke(this, AttendanceStatus.Present);
    private void Absent_Click(object sender, RoutedEventArgs e) => StatusClicked?.Invoke(this, AttendanceStatus.Absent);
    private void Cancelled_Click(object sender, RoutedEventArgs e) => StatusClicked?.Invoke(this, AttendanceStatus.Cancelled);
    private void Delete_Click(object sender, RoutedEventArgs e) => DeleteClicked?.Invoke(this, EventArgs.Empty);
}
