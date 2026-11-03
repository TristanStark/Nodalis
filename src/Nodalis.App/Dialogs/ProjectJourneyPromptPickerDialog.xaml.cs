using System.IO;
using System.Windows;
using System.Windows.Controls;
using Nodalis.Infrastructure.AI;

namespace Nodalis.App.Dialogs;

/// <summary>
/// Read-only catalog of the 31 authentic FRT-100 user prompts and other
/// safe local Markdown prompts. Persists nothing before journey confirmation.
/// </summary>
public partial class ProjectJourneyPromptPickerDialog : Window
{
    private readonly FileSystemAiPromptStore _store;
    private readonly IReadOnlyList<LocalAiPromptDescriptor> _catalog;
    private long _previewRevision;

    /// <summary>Creates the reviewer-owned chooser from a read-only catalog.</summary>
    /// <param name="store">Local prompt reader.</param>
    /// <param name="catalog">Eligible locally available templates.</param>
    /// <param name="selectedKey">Previously assigned prompt, if any.</param>
    public ProjectJourneyPromptPickerDialog(
        FileSystemAiPromptStore store,
        IReadOnlyList<LocalAiPromptDescriptor> catalog,
        string? selectedKey)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(catalog);
        _store = store;
        _catalog = catalog;
        InitializeComponent();
        PromptsList.ItemsSource = _catalog;
        PromptsList.SelectedItem = _catalog.FirstOrDefault(item =>
            item.Id == selectedKey);
        UpdateButtons();
    }

    /// <summary>Selected local user prompt key, null when explicitly cleared.</summary>
    public string? SelectedKey { get; private set; }

    /// <summary>Filters the already-read catalog without any I/O on each key.</summary>
    /// <param name="sender">Search input.</param>
    /// <param name="e">Text change.</param>
    private void SearchBox_TextChanged(
        object sender, TextChangedEventArgs e)
    {
        if (PromptsList is null)
        {
            return;
        }

        string query = SearchBox.Text.Trim();
        LocalAiPromptDescriptor? selected =
            PromptsList.SelectedItem as LocalAiPromptDescriptor;
        LocalAiPromptDescriptor[] filtered = _catalog.Where(item =>
            item.DisplayName.Contains(
                query, StringComparison.CurrentCultureIgnoreCase)).ToArray();
        PromptsList.ItemsSource = filtered;
        PromptsList.SelectedItem = filtered.FirstOrDefault(item =>
            item.Id == selected?.Id) ?? filtered.FirstOrDefault();
        StatusText.Text = filtered.Length + " prompt(s) disponible(s).";
        UpdateButtons();
    }

    /// <summary>
    /// Reads only the selected prompt on a worker; late results cannot
    /// overwrite a more recent selection's preview.
    /// </summary>
    /// <param name="sender">Prompt selection.</param>
    /// <param name="e">Selection change.</param>
    private async void PromptsList_SelectionChanged(
        object sender, SelectionChangedEventArgs e)
    {
        long revision = ++_previewRevision;
        LocalAiPromptDescriptor? selected =
            PromptsList.SelectedItem as LocalAiPromptDescriptor;
        UpdateButtons();
        PreviewText.Text = selected is null
            ? "Sélectionnez un prompt local." : "Lecture du prompt…";
        if (selected is null)
        {
            return;
        }

        try
        {
            string content = await _store.LoadAsync(selected.Id);
            if (revision == _previewRevision)
            {
                PreviewText.Text = content;
                StatusText.Text = selected.IsFrt100
                    ? "Prompt UTILISATEUR original. FRT-100 SYSTEME " +
                        "est à configurer séparément à l'export."
                    : "Prompt local. Aucun système FRT-100 implicite.";
            }
        }
        catch (Exception exception) when (exception is
            IOException or ArgumentException or InvalidDataException or
            UnauthorizedAccessException)
        {
            if (revision == _previewRevision)
            {
                PreviewText.Text = "Prompt indisponible : " +
                    exception.Message;
                StatusText.Text = "Sélection à actualiser.";
                AssignButton.IsEnabled = false;
            }
        }
    }

    /// <summary>Enables assigning only a current visible selection.</summary>
    private void UpdateButtons()
    {
        if (AssignButton is not null)
        {
            AssignButton.IsEnabled =
                PromptsList.SelectedItem is LocalAiPromptDescriptor;
        }
    }

    /// <summary>Returns the explicit chosen key for journey confirmation.</summary>
    /// <param name="sender">Apply.</param>
    /// <param name="e">Click.</param>
    private void Assign_Click(object sender, RoutedEventArgs e)
    {
        if (PromptsList.SelectedItem is LocalAiPromptDescriptor selected)
        {
            SelectedKey = selected.Id;
            DialogResult = true;
        }
    }

    /// <summary>Clears only the journey link, never deletes a template.</summary>
    /// <param name="sender">Clear.</param>
    /// <param name="e">Click.</param>
    private void Clear_Click(object sender, RoutedEventArgs e)
    {
        SelectedKey = null;
        DialogResult = true;
    }

    /// <summary>Aborts without touching the project.</summary>
    /// <param name="sender">Cancel.</param>
    /// <param name="e">Click.</param>
    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}
