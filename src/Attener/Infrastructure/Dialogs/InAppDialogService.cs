using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Attener;

namespace Attener.Infrastructure.Dialogs;

public static class InAppDialogService
{
    public static MessageBoxResult Show(
        DependencyObject owner,
        string message,
        string title,
        MessageBoxButton buttons = MessageBoxButton.OK,
        MessageBoxImage image = MessageBoxImage.Information,
        MessageBoxResult defaultResult = MessageBoxResult.None)
    {
        var host = GetHost(owner);
        var choices = GetChoices(buttons);
        var defaultChoice = defaultResult == MessageBoxResult.None
            ? choices.First(choice => choice.IsPrimary).Result
            : defaultResult;

        var primaryBrush = image == MessageBoxImage.Warning
            ? (Brush)host.FindResource("DangerBrush")
            : (Brush)host.FindResource("AccentBrush");
        var (glyph, iconBrush) = image switch
        {
            MessageBoxImage.Error or MessageBoxImage.Hand or MessageBoxImage.Stop =>
                ("!", (Brush)host.FindResource("DangerBrush")),
            MessageBoxImage.Warning or MessageBoxImage.Exclamation =>
                ("!", (Brush)host.FindResource("WarningBrush")),
            MessageBoxImage.Question => ("?", (Brush)host.FindResource("AccentBrush")),
            _ => ("i", (Brush)host.FindResource("AccentBrush"))
        };

        var heading = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        heading.Children.Add(new Border
        {
            Width = 26,
            Height = 26,
            CornerRadius = new CornerRadius(13),
            Background = new SolidColorBrush(Color.FromArgb(32, 255, 255, 255)),
            Child = new TextBlock
            {
                Text = glyph,
                Foreground = iconBrush,
                FontWeight = FontWeights.Bold,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            }
        });
        heading.Children.Add(new TextBlock
        {
            Text = title,
            FontSize = 17,
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(10, 0, 0, 0)
        });

        var content = new StackPanel { Width = 420, Margin = new Thickness(24) };
        content.Children.Add(heading);
        content.Children.Add(new TextBlock
        {
            Text = message,
            TextWrapping = TextWrapping.Wrap,
            FontSize = 13,
            Margin = new Thickness(0, 16, 0, 20)
        });

        var buttonPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        MessageBoxResult? selectedResult = null;
        FrameworkElement? initialFocus = null;
        foreach (var choice in choices)
        {
            var button = new Button
            {
                Content = choice.Label,
                MinWidth = 76,
                Height = 34,
                Padding = new Thickness(14, 6, 14, 6),
                Margin = new Thickness(8, 0, 0, 0),
                Background = choice.IsPrimary ? primaryBrush : (Brush)host.FindResource("HoverBrush"),
                Foreground = (Brush)host.FindResource("PrimaryTextBrush"),
                BorderThickness = new Thickness(0),
                Cursor = System.Windows.Input.Cursors.Hand,
                IsDefault = choice.Result == defaultChoice,
                IsCancel = !choice.IsPrimary && choice.Result == GetNegativeChoice(buttons)
            };
            if (choice.Result == defaultChoice)
                initialFocus = button;
            button.Click += (_, _) =>
            {
                selectedResult = choice.Result;
                host.CloseInAppDialog(choice.IsPrimary);
            };
            buttonPanel.Children.Add(button);
        }
        content.Children.Add(buttonPanel);

        var result = host.ShowInAppDialog(content, initialFocus);
        if (selectedResult is MessageBoxResult selected)
            return selected;
        if (result == true)
            return choices.First(choice => choice.IsPrimary).Result;
        if (result == false)
            return GetNegativeChoice(buttons) ?? defaultChoice;
        return defaultChoice;
    }

    public static bool? ShowCustom(DependencyObject owner, UIElement content, FrameworkElement? initialFocus = null) =>
        GetHost(owner).ShowInAppDialog(content, initialFocus);

    public static void CloseCustom(DependencyObject owner, bool? result) =>
        GetHost(owner).CloseInAppDialog(result);

    private static MainWindow GetHost(DependencyObject owner) =>
        Window.GetWindow(owner) as MainWindow
        ?? throw new InvalidOperationException("In-app dialogs require an owner inside the main window.");

    private static IReadOnlyList<DialogChoice> GetChoices(MessageBoxButton buttons) => buttons switch
    {
        MessageBoxButton.OK => [new("OK", MessageBoxResult.OK, true)],
        MessageBoxButton.OKCancel =>
        [
            new("Cancel", MessageBoxResult.Cancel, false),
            new("OK", MessageBoxResult.OK, true)
        ],
        MessageBoxButton.YesNo =>
        [
            new("No", MessageBoxResult.No, false),
            new("Yes", MessageBoxResult.Yes, true)
        ],
        MessageBoxButton.YesNoCancel =>
        [
            new("Cancel", MessageBoxResult.Cancel, false),
            new("No", MessageBoxResult.No, false),
            new("Yes", MessageBoxResult.Yes, true)
        ],
        _ => throw new ArgumentOutOfRangeException(nameof(buttons), buttons, "Unsupported dialog button set.")
    };

    private static MessageBoxResult? GetNegativeChoice(MessageBoxButton buttons) => buttons switch
    {
        MessageBoxButton.OK => null,
        MessageBoxButton.OKCancel or MessageBoxButton.YesNoCancel => MessageBoxResult.Cancel,
        MessageBoxButton.YesNo => MessageBoxResult.No,
        _ => throw new ArgumentOutOfRangeException(nameof(buttons), buttons, "Unsupported dialog button set.")
    };

    private sealed record DialogChoice(string Label, MessageBoxResult Result, bool IsPrimary);
}
