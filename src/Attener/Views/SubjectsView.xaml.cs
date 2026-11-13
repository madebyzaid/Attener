using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
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
        AddLectureCheckBox.Checked += AddTypeCheckBox_Changed;
        AddLectureCheckBox.Unchecked += AddTypeCheckBox_Changed;
        AddTutorialCheckBox.Checked += AddTypeCheckBox_Changed;
        AddTutorialCheckBox.Unchecked += AddTypeCheckBox_Changed;
        AddPracticalCheckBox.Checked += AddTypeCheckBox_Changed;
        AddPracticalCheckBox.Unchecked += AddTypeCheckBox_Changed;
        UpdateAddTypeDetailsVisibility();
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

        ActiveSubjectsButton.Background = _showArchived ? System.Windows.Media.Brushes.Transparent : (System.Windows.Media.Brush)FindResource("AccentBrush");
        ActiveSubjectsButton.Foreground = _showArchived ? (System.Windows.Media.Brush)FindResource("SecondaryTextBrush") : (System.Windows.Media.Brush)FindResource("PrimaryTextBrush");
        ArchivedSubjectsButton.Background = _showArchived ? (System.Windows.Media.Brush)FindResource("AccentBrush") : System.Windows.Media.Brushes.Transparent;
        ArchivedSubjectsButton.Foreground = _showArchived ? (System.Windows.Media.Brush)FindResource("PrimaryTextBrush") : (System.Windows.Media.Brush)FindResource("SecondaryTextBrush");
    }

    private void AddSubject_Click(object sender, RoutedEventArgs e)
    {
        var name = SubjectNameBox.Text.Trim();
        var types = GetSelectedTypes(AddLectureCheckBox, AddTutorialCheckBox, AddPracticalCheckBox);
        if (name.Length == 0)
        {
            MessageBox.Show("Enter a subject code first.", "Subject code required",
                MessageBoxButton.OK, MessageBoxImage.Information);
            SubjectNameBox.Focus();
            return;
        }

        if (types.Count == 0)
        {
            MessageBox.Show("Select at least one class type.", "Class type required",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var classDetails = new List<SubjectClassDetails>();
        if (AddLectureCheckBox.IsChecked == true)
            classDetails.Add(new SubjectClassDetails(SubjectType.Lecture, AddLectureProfessorBox.Text, AddLectureVenueBox.Text));
        if (AddTutorialCheckBox.IsChecked == true)
            classDetails.Add(new SubjectClassDetails(SubjectType.Tutorial, AddTutorialProfessorBox.Text, AddTutorialVenueBox.Text));
        if (AddPracticalCheckBox.IsChecked == true)
            classDetails.Add(new SubjectClassDetails(SubjectType.Practical, AddPracticalProfessorBox.Text, AddPracticalVenueBox.Text));

        if (!_repository.AddSubject(name, classDetails))
        {
            MessageBox.Show("A subject with that code already exists, including archived subjects.",
                "Subject already exists", MessageBoxButton.OK, MessageBoxImage.Information);
            SubjectNameBox.SelectAll();
            SubjectNameBox.Focus();
            return;
        }

        _showArchived = false;
        SubjectNameBox.Clear();
        AddLectureProfessorBox.Clear();
        AddLectureVenueBox.Clear();
        AddTutorialProfessorBox.Clear();
        AddTutorialVenueBox.Clear();
        AddPracticalProfessorBox.Clear();
        AddPracticalVenueBox.Clear();
        AddLectureCheckBox.IsChecked = true;
        AddTutorialCheckBox.IsChecked = false;
        AddPracticalCheckBox.IsChecked = false;
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

    private void EditSubject_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: SubjectSummary subject }) return;

        TextBox CreateInput(string text) => new()
        {
            Text = text,
            Height = 40,
            VerticalContentAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 5, 0, 12),
            Style = (Style)FindResource("RoundedTextBoxStyle")
        };
        var nameInput = CreateInput(subject.Name);
        var typeInputs = CreateTypeEditors(subject.ClassDetails);
        var save = new Button
        {
            Content = "Save changes",
            Padding = new Thickness(18, 7, 18, 7),
            Background = (Brush)FindResource("AccentBrush"),
            Foreground = (Brush)FindResource("PrimaryTextBrush"),
            FontWeight = FontWeights.SemiBold,
            BorderThickness = new Thickness(0),
            IsDefault = true,
            Cursor = Cursors.Hand
        };
        var cancel = new Button
        {
            Content = "Cancel",
            Padding = new Thickness(14, 7, 14, 7),
            Margin = new Thickness(0, 0, 8, 0),
            IsCancel = true,
            BorderThickness = new Thickness(0),
            Cursor = Cursors.Hand
        };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        buttons.Children.Add(cancel);
        buttons.Children.Add(save);

        var panel = new StackPanel { Margin = new Thickness(22), Width = 360 };
        panel.Children.Add(new TextBlock { Text = "Edit subject", FontSize = 18, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 16) });
        panel.Children.Add(CreateLabel("Subject code"));
        panel.Children.Add(nameInput);
        panel.Children.Add(CreateLabel("Class types"));
        panel.Children.Add(typeInputs.CheckBoxes);
        panel.Children.Add(CreateLabel("Class details"));
        panel.Children.Add(typeInputs.Cards);
        panel.Children.Add(buttons);

        var dialog = new Window
        {
            Title = "Edit subject",
            Content = new ScrollViewer
            {
                Content = panel,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                MaxHeight = SystemParameters.WorkArea.Height * 0.85
            },
            SizeToContent = SizeToContent.WidthAndHeight,
            Width = 470,
            ResizeMode = ResizeMode.CanResize,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = Window.GetWindow(this),
            Background = (Brush)FindResource("SurfaceBackgroundBrush")
        };
        save.Click += (_, _) =>
        {
            var selectedDetails = GetSelectedClassDetails(typeInputs.Editors);
            if (selectedDetails.Count == 0)
            {
                MessageBox.Show("Select at least one class type.", "Class type required",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var removedTypes = subject.Types.Except(selectedDetails.Select(details => details.Type)).ToArray();
            if (removedTypes.Length > 0)
            {
                var removedList = string.Join(", ", removedTypes);
                var confirmation = MessageBox.Show(
                    $"Remove {removedList} from {subject.Name}? Its timetable entries and attendance history for those class types will be permanently deleted. This cannot be undone.",
                    "Delete class type data", MessageBoxButton.YesNo, MessageBoxImage.Warning,
                    MessageBoxResult.No);
                if (confirmation != MessageBoxResult.Yes)
                    return;
            }

            dialog.DialogResult = true;
        };
        dialog.Loaded += (_, _) => { nameInput.Focus(); nameInput.SelectAll(); };

        if (dialog.ShowDialog() != true || string.IsNullOrWhiteSpace(nameInput.Text)) return;
        try
        {
            _repository.UpdateSubject(subject.Id, nameInput.Text, GetSelectedClassDetails(typeInputs.Editors));
            LoadSubjects();
        }
        catch (Microsoft.Data.Sqlite.SqliteException)
        {
            MessageBox.Show("A subject with that code already exists.", "Code already used",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private TypeEditorCollection CreateTypeEditors(IReadOnlyCollection<SubjectClassDetails> classDetails)
    {
        var checkBoxes = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 5, 0, 12) };
        var cards = new StackPanel();
        var editors = new Dictionary<SubjectType, TypeEditor>();
        foreach (var type in Enum.GetValues<SubjectType>())
        {
            var details = classDetails.FirstOrDefault(item => item.Type == type)
                ?? new SubjectClassDetails(type, "", "");
            var professorInput = new TextBox
            {
                Text = details.Professor,
                Height = 36,
                Padding = new Thickness(9, 6, 9, 6),
                VerticalContentAlignment = VerticalAlignment.Center,
                Style = (Style)FindResource("RoundedTextBoxStyle")
            };
            var venueInput = new TextBox
            {
                Text = details.Venue,
                Height = 36,
                Padding = new Thickness(9, 6, 9, 6),
                VerticalContentAlignment = VerticalAlignment.Center,
                Style = (Style)FindResource("RoundedTextBoxStyle")
            };
            var cardContent = new StackPanel();
            cardContent.Children.Add(new TextBlock
            {
                Text = type.ToString(),
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 0, 7)
            });
            cardContent.Children.Add(CreateLabel("Professor"));
            cardContent.Children.Add(professorInput);
            cardContent.Children.Add(CreateLabel("Venue"));
            cardContent.Children.Add(venueInput);
            var card = new Border
            {
                Background = (Brush)FindResource("WindowBackgroundBrush"),
                BorderBrush = (Brush)FindResource("BorderBrush"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(7),
                Padding = new Thickness(10),
                Margin = new Thickness(0, 0, 0, 8),
                Child = cardContent,
                Visibility = classDetails.Any(item => item.Type == type) ? Visibility.Visible : Visibility.Collapsed
            };
            var checkBox = new CheckBox
            {
                Content = type.ToString(),
                Tag = type,
                IsChecked = classDetails.Any(item => item.Type == type),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 14, 0)
            };
            checkBox.Checked += (_, _) => card.Visibility = Visibility.Visible;
            checkBox.Unchecked += (_, _) => card.Visibility = Visibility.Collapsed;
            checkBoxes.Children.Add(checkBox);
            cards.Children.Add(card);
            editors.Add(type, new TypeEditor(checkBox, professorInput, venueInput));
        }
        return new TypeEditorCollection(checkBoxes, cards, editors);
    }

    private TextBlock CreateLabel(string text) => new()
    {
        Text = text,
        Foreground = (Brush)FindResource("SecondaryTextBrush"),
        FontSize = 12
    };

    private static IReadOnlyCollection<SubjectClassDetails> GetSelectedClassDetails(
        IReadOnlyDictionary<SubjectType, TypeEditor> editors) =>
        editors.Where(item => item.Value.CheckBox.IsChecked == true)
            .Select(item => new SubjectClassDetails(
                item.Key, item.Value.ProfessorInput.Text, item.Value.VenueInput.Text))
            .ToArray();

    private static IReadOnlyCollection<SubjectType> GetSelectedTypes(
        CheckBox lecture, CheckBox tutorial, CheckBox practical)
    {
        var selected = new List<SubjectType>();
        if (lecture.IsChecked == true) selected.Add(SubjectType.Lecture);
        if (tutorial.IsChecked == true) selected.Add(SubjectType.Tutorial);
        if (practical.IsChecked == true) selected.Add(SubjectType.Practical);
        return selected;
    }

    private void AddTypeCheckBox_Changed(object sender, RoutedEventArgs e) =>
        UpdateAddTypeDetailsVisibility();

    private void UpdateAddTypeDetailsVisibility()
    {
        AddLectureDetailsCard.Visibility = AddLectureCheckBox.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        AddTutorialDetailsCard.Visibility = AddTutorialCheckBox.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        AddPracticalDetailsCard.Visibility = AddPracticalCheckBox.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
    }

    private sealed record TypeEditor(CheckBox CheckBox, TextBox ProfessorInput, TextBox VenueInput);
    private sealed record TypeEditorCollection(
        StackPanel CheckBoxes,
        StackPanel Cards,
        IReadOnlyDictionary<SubjectType, TypeEditor> Editors);
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