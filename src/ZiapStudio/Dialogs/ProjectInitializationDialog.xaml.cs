using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using ZiapStudio.Services.Initialization;

namespace ZiapStudio.Dialogs;

public sealed partial class ProjectInitializationDialog : ContentDialog
{
    private readonly ProjectIdGenerator _projectIdGenerator;
    private bool _isInitializing;
    private bool _isUpdatingId;
    private bool _isIdManuallyEdited;

    public ProjectInitializationDialog(
        ProjectInitializationOptions defaults,
        ProjectIdGenerator projectIdGenerator)
    {
        ArgumentNullException.ThrowIfNull(defaults);

        _projectIdGenerator = projectIdGenerator;
        InitializeComponent();

        _isInitializing = true;
        NameTextBox.Text = defaults.Name;
        IdTextBox.Text = defaults.Id;
        TypeComboBox.Text = defaults.Type;
        VersionTextBox.Text = defaults.Version ?? string.Empty;
        PublisherTextBox.Text = defaults.Publisher ?? string.Empty;
        _isInitializing = false;
    }

    public ProjectInitializationOptions? Options { get; private set; }

    private void NameTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_isInitializing || _isIdManuallyEdited)
        {
            return;
        }

        _isUpdatingId = true;
        IdTextBox.Text = _projectIdGenerator.Generate(NameTextBox.Text);
        _isUpdatingId = false;
    }

    private void IdTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!_isInitializing && !_isUpdatingId)
        {
            _isIdManuallyEdited = true;
        }
    }

    private void ContentDialog_PrimaryButtonClick(
        ContentDialog sender,
        ContentDialogButtonClickEventArgs args)
    {
        var name = NameTextBox.Text.Trim();
        var projectId = IdTextBox.Text.Trim();
        var projectType = GetProjectType();

        var validationMessage = Validate(name, projectId, projectType);
        if (validationMessage is not null)
        {
            args.Cancel = true;
            ValidationInfoBar.Message = validationMessage;
            ValidationInfoBar.IsOpen = true;
            return;
        }

        ValidationInfoBar.IsOpen = false;
        Options = new ProjectInitializationOptions
        {
            Name = name,
            Id = projectId,
            Type = projectType,
            Version = NullIfWhiteSpace(VersionTextBox.Text),
            Publisher = NullIfWhiteSpace(PublisherTextBox.Text),
        };
    }

    private string? Validate(string name, string projectId, string projectType)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return "Inserisci il nome del progetto.";
        }

        if (!_projectIdGenerator.IsValid(projectId))
        {
            return "L'ID deve contenere solo lettere minuscole, numeri e trattini singoli.";
        }

        if (string.IsNullOrWhiteSpace(projectType))
        {
            return "Seleziona o inserisci il tipo di progetto.";
        }

        return null;
    }

    private string GetProjectType() =>
        (TypeComboBox.Text ?? TypeComboBox.SelectedItem?.ToString() ?? string.Empty).Trim();

    private static string? NullIfWhiteSpace(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
