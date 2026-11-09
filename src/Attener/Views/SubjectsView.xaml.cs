using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Attener.Features.Subjects;
using Attener.Infrastructure.Database;

namespace Attener.Views;

public partial class SubjectsView : UserControl
{
    private readonly SubjectRepository _repository = new(DatabaseLocation.GetPath());
    private bool _showArchived;

    public SubjectsView()
    {
        InitializeComponent();
        LoadSubjects();
    }

    public void RefreshData() => LoadSubjects();

    private void LoadSubjects()
    {
        var subjects = _repository.GetSubjects(_showArchived);
        SubjectsList.ItemsSource = subjects;
        EmptyStateText.Visibility = subjects.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        EmptyStateText.Text = _showArchived
            ? "Archived subjects will appear here. Their attendance records remain saved."
            : "No subjects here yet. Add one above to get started.";

        ActiveSubjectsButton.Background = _showArchived ? System.Windows.Media.Brushes.Transparent : (System.Windows.Media.Brush)FindResource("SurfaceBackgroundBrush");
        ActiveSubjectsButton.Foreground = _showArchived ? (System.Windows.Media.Brush)FindResource("SecondaryTextBrush") : (System.Windows.Media.Brush)FindResource("PrimaryTextBrush");
        ArchivedSubjectsButton.Background = _showArchived ? (System.Windows.Media.Brush)FindResource("SurfaceBackgroundBrush") : System.Windows.Media.Brushes.Transparent;
        ArchivedSubjectsButton.Foreground = _showArchived ? (System.Windows.Media.Brush)FindResource("PrimaryTextBrush") : (System.Windows.Media.Brush)FindResource("SecondaryTextBrush");
    }

    private void AddSubject_Click(object sender, RoutedEventArgs e)
    {
        var name = SubjectNameBox.Text.Trim();
        if (name.Length == 0)
        {
            MessageBox.Show("Enter a subject name first.", "Subject name required",
                MessageBoxButton.OK, MessageBoxImage.Information);
            SubjectNameBox.Focus();
            return;
        }

        if (!_repository.AddSubject(name))
        {
            MessageBox.Show("A subject with that name already exists, including archived subjects.",
                "Subject already exists", MessageBoxButton.OK, MessageBoxImage.Information);
            SubjectNameBox.SelectAll();
            SubjectNameBox.Focus();
            return;
        }

        _showArchived = false;
        SubjectNameBox.Clear();
        LoadSubjects();
        SubjectNameBox.Focus();
    }

    private void SubjectNameBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            AddSubject_Click(sender, e);
        }
    }

    private void ShowActive_Click(object sender, RoutedEventArgs e)
    {
        _showArchived = false;
        LoadSubjects();
    }

    private void ShowArchived_Click(object sender, RoutedEventArgs e)
    {
        _showArchived = true;
        LoadSubjects();
    }

    private void RenameSubject_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: SubjectSummary subject }) return;
        var input = new TextBox { Text = subject.Name, Width = 300, Margin = new Thickness(0, 12, 0, 12) };
        var panel = new StackPanel { Margin = new Thickness(18) };
        panel.Children.Add(new TextBlock { Text = "New subject name" });
        panel.Children.Add(input);
        var dialog = new Window { Title = "Rename subject", Content = panel, SizeToContent = SizeToContent.WidthAndHeight, WindowStartupLocation = WindowStartupLocation.CenterOwner, Owner = Window.GetWindow(this), Background = (System.Windows.Media.Brush)FindResource("SurfaceBackgroundBrush"), Foreground = (System.Windows.Media.Brush)FindResource("PrimaryTextBrush") };
        input.KeyDown += (_, key) => { if (key.Key == Key.Enter) dialog.DialogResult = true; };
        if (dialog.ShowDialog() != true || string.IsNullOrWhiteSpace(input.Text)) return;
        try { _repository.RenameSubject(subject.Id, input.Text); LoadSubjects(); }
        catch (Microsoft.Data.Sqlite.SqliteException) { MessageBox.Show("A subject with that name already exists.", "Name already used", MessageBoxButton.OK, MessageBoxImage.Information); }
    }
    private void ArchiveSubject_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: long subjectId, DataContext: SubjectSummary subject })
        {
            return;
        }

        var result = MessageBox.Show(
            $"Archive {subject.Name}? Its attendance history will be preserved.",
            "Archive subject", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (result != MessageBoxResult.Yes)
        {
            return;
        }

        _repository.ArchiveSubject(subjectId, DateOnly.FromDateTime(DateTime.Today));
        LoadSubjects();
    }

    // Permanently deletes the subject and everything recorded for it. Just one OK click confirms.
    private void DeleteSubject_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: long subjectId, DataContext: SubjectSummary subject })
        {
            return;
        }

        var result = MessageBox.Show(
            $"Permanently delete {subject.Name}? All of its attendance records and timetable entries will be deleted. This cannot be undone.",
            "Delete subject", MessageBoxButton.OKCancel, MessageBoxImage.Warning, MessageBoxResult.Cancel);
        if (result != MessageBoxResult.OK)
        {
            return;
        }

        try
        {
            _repository.DeleteSubject(subjectId);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not delete the subject. {ex.Message}", "Delete failed",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }

        LoadSubjects();
    }
}