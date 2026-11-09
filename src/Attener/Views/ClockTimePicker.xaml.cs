using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace Attener.Views;

// A time field with a Material-style clock dial. Hours 0-11 are on the outer ring, 12-23 on the inner ring.
// ShowMinutes is false by default: the dial only picks the hour and the minutes stay :00.
// Set ShowMinutes="True" to get the minute step after the hour is picked.
public partial class ClockTimePicker : UserControl
{
    private const double DialSize = 260;
    private const double Middle = DialSize / 2;
    private const double OuterRadius = 104;
    private const double InnerRadius = 68;
    private const double BubbleSize = 34;

    private static readonly Brush DarkText = new SolidColorBrush(Color.FromRgb(0x11, 0x13, 0x18));
    private static readonly Brush DialFace = new SolidColorBrush(Color.FromRgb(0x29, 0x2E, 0x39));
    private static readonly Brush InactiveChip = new SolidColorBrush(Color.FromRgb(0x2B, 0x30, 0x3B));

    private TimeOnly _time = new(8, 0);
    private int _hour;
    private int _minute;
    private bool _pickingMinutes;
    private bool _dragging;

    public event EventHandler? EnterPressed;

    public bool ShowMinutes { get; set; }

    public ClockTimePicker()
    {
        InitializeComponent();
        TimeBox.Text = FormatTime(_time);
    }

    // The time in the box. If the typed text is not a valid time, this is the last valid time.
    public TimeOnly Time
    {
        get => TryGetTime(out var time) ? time : _time;
        set
        {
            _time = value;
            TimeBox.Text = FormatTime(value);
        }
    }

    // Reads what is currently typed in the box. Accepts "8", "08" or "08:00" (minutes only if ShowMinutes).
    public bool TryGetTime(out TimeOnly time)
    {
        time = default;
        var parts = TimeBox.Text.Trim().Split(':');
        if (parts.Length > 2
            || !int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var hour)
            || hour > 23)
            return false;

        var minute = 0;
        if (parts.Length == 2
            && !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out minute))
            return false;
        if (minute > 59 || (!ShowMinutes && minute != 0))
            return false;

        time = new TimeOnly(hour, minute);
        return true;
    }

    private static string FormatTime(TimeOnly time) => $"{time.Hour:00}:{time.Minute:00}";

    private void TimeBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
            EnterPressed?.Invoke(this, EventArgs.Empty);
    }

    private void OpenButton_Click(object sender, RoutedEventArgs e)
    {
        var time = Time;
        _hour = time.Hour;
        _minute = ShowMinutes ? time.Minute : 0;
        _pickingMinutes = false;
        Refresh();
        DialPopup.IsOpen = true;
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        Time = new TimeOnly(_hour, ShowMinutes ? _minute : 0);
        DialPopup.IsOpen = false;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialPopup.IsOpen = false;

    private void HourChip_MouseDown(object sender, MouseButtonEventArgs e)
    {
        _pickingMinutes = false;
        Refresh();
    }

    private void MinuteChip_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (!ShowMinutes) return;
        _pickingMinutes = true;
        Refresh();
    }

    // ----- Mouse: turn a click position on the dial into an hour or minute -----

    private void Dial_MouseDown(object sender, MouseButtonEventArgs e)
    {
        _dragging = true;
        PickFrom(e.GetPosition(Dial));
    }

    private void Dial_MouseMove(object sender, MouseEventArgs e)
    {
        if (_dragging && e.LeftButton == MouseButtonState.Pressed)
            PickFrom(e.GetPosition(Dial));
    }

    private void Dial_MouseUp(object sender, MouseButtonEventArgs e) => FinishDrag();

    private void Dial_MouseLeave(object sender, MouseEventArgs e) => FinishDrag();

    // After the hour is chosen, go on to the minutes (only when minutes are enabled).
    private void FinishDrag()
    {
        if (!_dragging) return;
        _dragging = false;
        if (!_pickingMinutes && ShowMinutes)
        {
            _pickingMinutes = true;
            Refresh();
        }
    }

    private void PickFrom(Point point)
    {
        var dx = point.X - Middle;
        var dy = point.Y - Middle;
        var distance = Math.Sqrt(dx * dx + dy * dy);
        if (distance < 14) return;

        // Angle clockwise from 12 o'clock, 0 to 360.
        var angle = Math.Atan2(dx, -dy) * 180 / Math.PI;
        if (angle < 0) angle += 360;

        if (_pickingMinutes)
        {
            _minute = (int)Math.Round(angle / 6) % 60;
        }
        else
        {
            var slot = (int)Math.Round(angle / 30) % 12;
            var isInnerRing = distance < (OuterRadius + InnerRadius) / 2;
            _hour = isInnerRing ? slot + 12 : slot;
        }
        Refresh();
    }

    // ----- Drawing -----

    private void Refresh()
    {
        DrawDial();
        UpdateHeader();
    }

    private void UpdateHeader()
    {
        var accent = (Brush)FindResource("AccentBrush");
        var primary = (Brush)FindResource("PrimaryTextBrush");

        HourChipText.Text = _hour.ToString("00");
        MinuteChipText.Text = _minute.ToString("00");
        HourChip.Background = _pickingMinutes ? InactiveChip : accent;
        HourChipText.Foreground = _pickingMinutes ? primary : DarkText;
        MinuteChip.Background = _pickingMinutes ? accent : InactiveChip;
        MinuteChipText.Foreground = _pickingMinutes ? DarkText : primary;
        MinuteChip.Opacity = ShowMinutes ? 1 : 0.4;
    }

    private void DrawDial()
    {
        var accent = (Brush)FindResource("AccentBrush");
        var primary = (Brush)FindResource("PrimaryTextBrush");
        Dial.Children.Clear();

        Dial.Children.Add(new Ellipse { Width = DialSize, Height = DialSize, Fill = DialFace, IsHitTestVisible = false });

        var selected = _pickingMinutes ? MinutePoint(_minute) : HourPoint(_hour);
        Dial.Children.Add(new Line
        {
            X1 = Middle, Y1 = Middle, X2 = selected.X, Y2 = selected.Y,
            Stroke = accent, StrokeThickness = 2, IsHitTestVisible = false
        });
        AddDot(new Point(Middle, Middle), 8, accent);
        AddDot(selected, BubbleSize, accent);

        if (_pickingMinutes)
        {
            for (var minute = 0; minute < 60; minute += 5)
            {
                if (minute != _minute)
                    AddLabel(minute.ToString("00"), MinutePoint(minute), primary);
            }
            AddLabel(_minute.ToString("00"), selected, DarkText);
        }
        else
        {
            for (var hour = 0; hour < 24; hour++)
            {
                if (hour != _hour)
                    AddLabel(hour.ToString(), HourPoint(hour), primary);
            }
            AddLabel(_hour.ToString(), selected, DarkText);
        }
    }

    private void AddDot(Point centre, double size, Brush fill)
    {
        var dot = new Ellipse { Width = size, Height = size, Fill = fill, IsHitTestVisible = false };
        Canvas.SetLeft(dot, centre.X - size / 2);
        Canvas.SetTop(dot, centre.Y - size / 2);
        Dial.Children.Add(dot);
    }

    private void AddLabel(string text, Point centre, Brush foreground)
    {
        var holder = new Grid { Width = 36, Height = 36, IsHitTestVisible = false };
        holder.Children.Add(new TextBlock
        {
            Text = text,
            Foreground = foreground,
            FontSize = 14,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        });
        Canvas.SetLeft(holder, centre.X - 18);
        Canvas.SetTop(holder, centre.Y - 18);
        Dial.Children.Add(holder);
    }

    // 0-11 sit on the outer ring, 12-23 on the inner ring; each hour is 30 degrees apart.
    private static Point HourPoint(int hour) =>
        Polar(hour >= 12 ? InnerRadius : OuterRadius, (hour % 12) * 30);

    // Each minute is 6 degrees apart.
    private static Point MinutePoint(int minute) => Polar(OuterRadius, minute * 6);

    private static Point Polar(double radius, double degreesClockwiseFromTop)
    {
        var radians = degreesClockwiseFromTop * Math.PI / 180;
        return new Point(Middle + radius * Math.Sin(radians), Middle - radius * Math.Cos(radians));
    }
}