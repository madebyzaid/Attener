using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;

namespace Attener;

public partial class MainWindow : Window
{
    private readonly Stack<DialogState> _dialogStack = [];
    private DispatcherFrame? _activeDialogFrame;
    private bool? _activeDialogResult;

    public MainWindow()
    {
        InitializeComponent();
        SourceInitialized += MainWindow_SourceInitialized;
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    private void MainWindow_SourceInitialized(object? sender, EventArgs e)
    {
        try
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            var darkMode = 1;
            var captionColor = 0x001B1917;
            var textColor = 0x00ECE9E6;

            _ = DwmSetWindowAttribute(hwnd, 20, ref darkMode, sizeof(int));
            _ = DwmSetWindowAttribute(hwnd, 35, ref captionColor, sizeof(int));
            _ = DwmSetWindowAttribute(hwnd, 36, ref textColor, sizeof(int));
        }
        catch
        {
        }
    }

    public void ShowAddAttendanceEntry(DateTime date, bool lockDate) => AttendancePage.AddManualForDate(date, lockDate);

    public bool? ShowInAppDialog(UIElement content, FrameworkElement? initialFocus = null)
    {
        var previousState = new DialogState(
            _activeDialogFrame,
            _activeDialogResult,
            DialogContent.Content,
            DialogOverlay.Visibility,
            Keyboard.FocusedElement);
        _dialogStack.Push(previousState);
        var frame = new DispatcherFrame();
        _activeDialogResult = null;
        _activeDialogFrame = frame;
        DialogContent.Content = content;
        DialogOverlay.Visibility = Visibility.Visible;

        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
        {
            if (_activeDialogFrame == frame)
                Keyboard.Focus(initialFocus ?? DialogFrame);
        }));

        try
        {
            Dispatcher.PushFrame(frame);
            return _activeDialogResult;
        }
        finally
        {
            var state = _dialogStack.Pop();
            _activeDialogFrame = state.Frame;
            _activeDialogResult = state.Result;
            DialogContent.Content = state.Content;
            DialogOverlay.Visibility = state.Visibility;
            if (state.Focus is not null)
                Keyboard.Focus(state.Focus);
        }
    }

    public void CloseInAppDialog(bool? result)
    {
        if (_activeDialogFrame is null)
            throw new InvalidOperationException("There is no in-app dialog to close.");

        _activeDialogResult = result;
        _activeDialogFrame.Continue = false;
    }

    private void DialogOverlay_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape)
            return;

        CloseInAppDialog(false);
        e.Handled = true;
    }

    private sealed record DialogState(
        DispatcherFrame? Frame,
        bool? Result,
        object? Content,
        Visibility Visibility,
        IInputElement? Focus);

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
