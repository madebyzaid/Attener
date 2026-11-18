using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Attener.Features.Subjects;

namespace Attener.Controls;

public enum SubjectTypePickerMode
{
    Filter,
    Select
}

public sealed class SubjectTypeBadge : INotifyPropertyChanged
{
    private bool _isSelected;

    public SubjectType Type { get; }
    public string Letter => Type switch
    {
        SubjectType.Lecture => "L",
        SubjectType.Tutorial => "T",
        SubjectType.Practical => "P",
        _ => throw new ArgumentOutOfRangeException(nameof(Type))
    };

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

    public SubjectTypeBadge(SubjectType type, bool isSelected = false)
    {
        Type = type;
        _isSelected = isSelected;
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}

public sealed class SubjectTypePickerEventArgs : EventArgs
{
    public SubjectType? Type { get; }

    public SubjectTypePickerEventArgs(SubjectType? type)
    {
        Type = type;
    }
}

public partial class SubjectTypePicker : UserControl
{
    public static readonly DependencyProperty ModeProperty =
        DependencyProperty.Register(nameof(Mode), typeof(SubjectTypePickerMode), typeof(SubjectTypePicker),
            new PropertyMetadata(SubjectTypePickerMode.Select, OnModeChanged));

    public static readonly DependencyProperty SubjectNameProperty =
        DependencyProperty.Register(nameof(SubjectName), typeof(string), typeof(SubjectTypePicker),
            new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty BadgesProperty =
        DependencyProperty.Register(nameof(Badges), typeof(IEnumerable<SubjectTypeBadge>), typeof(SubjectTypePicker),
            new PropertyMetadata(Array.Empty<SubjectTypeBadge>()));

    public static readonly DependencyProperty NameMaxWidthProperty =
        DependencyProperty.Register(nameof(NameMaxWidth), typeof(double), typeof(SubjectTypePicker),
            new PropertyMetadata(double.PositiveInfinity));

    public SubjectTypePickerMode Mode
    {
        get => (SubjectTypePickerMode)GetValue(ModeProperty);
        set => SetValue(ModeProperty, value);
    }

    public string SubjectName
    {
        get => (string)GetValue(SubjectNameProperty);
        set => SetValue(SubjectNameProperty, value);
    }

    public IEnumerable<SubjectTypeBadge> Badges
    {
        get => (IEnumerable<SubjectTypeBadge>)GetValue(BadgesProperty);
        set => SetValue(BadgesProperty, value);
    }

    public double NameMaxWidth
    {
        get => (double)GetValue(NameMaxWidthProperty);
        set => SetValue(NameMaxWidthProperty, value);
    }

    public event EventHandler<SubjectTypePickerEventArgs>? SubjectClicked;
    public event EventHandler<SubjectTypePickerEventArgs>? TypeClicked;

    public SubjectTypePicker()
    {
        InitializeComponent();
        UpdateSubjectNameCursor();
    }

    private static void OnModeChanged(DependencyObject sender, DependencyPropertyChangedEventArgs e)
    {
        if (sender is SubjectTypePicker picker)
            picker.UpdateSubjectNameCursor();
    }

    private void UpdateSubjectNameCursor()
    {
        SubjectNameText.Cursor = Mode == SubjectTypePickerMode.Filter ? Cursors.Hand : Cursors.Arrow;
        // A capped-width name gets centred in its column when stretched, so in the popup keep it left.
        // Filter mode stays stretched so the whole name area stays clickable.
        SubjectNameText.HorizontalAlignment = Mode == SubjectTypePickerMode.Filter ? HorizontalAlignment.Stretch : HorizontalAlignment.Left;
    }

    private void SubjectName_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (Mode != SubjectTypePickerMode.Filter)
            return;

        e.Handled = true;
        SubjectClicked?.Invoke(this, new SubjectTypePickerEventArgs(null));
    }

    private void Badge_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (sender is Button { Tag: SubjectTypeBadge badge })
            TypeClicked?.Invoke(this, new SubjectTypePickerEventArgs(badge.Type));
    }
}
