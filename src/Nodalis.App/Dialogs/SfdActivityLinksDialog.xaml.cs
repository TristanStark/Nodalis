using System.IO;
using System.Windows;
using System.Windows.Controls;
using Nodalis.Core.Sfd;
using Nodalis.Infrastructure.Projects;

namespace Nodalis.App.Dialogs;

/// <summary>Explicit, read/review/edit interface for SFD ACT↔FCT links.</summary>
public partial class SfdActivityLinksDialog : Window
{
    private readonly SfdActivityFunctionalityService _service;
    private readonly string _projectDirectory;
    private readonly bool _readOnly;
    private readonly IReadOnlyDictionary<Guid, SfdDocumentEntry> _entries;

    private sealed record Choice(Guid Id, string Label);
    private sealed record LinkChoice(Guid ActivityId, Guid FunctionalityId, string Label);

    /// <summary>Opens a bounded list of known canonical ACT/FCT source identities.</summary>
    /// <param name="service">Workspace-bound link persistence service.</param>
    /// <param name="projectDirectory">Verified SFD project.</param>
    /// <param name="catalog">Already validated SFD objects.</param>
    /// <param name="readOnly">Whether mutation is forbidden.</param>
    public SfdActivityLinksDialog(SfdActivityFunctionalityService service,
        string projectDirectory, SfdDocumentCatalog catalog, bool readOnly)
    {
        ArgumentNullException.ThrowIfNull(service);
        ArgumentNullException.ThrowIfNull(catalog);
        _service = service;
        _projectDirectory = projectDirectory;
        _readOnly = readOnly;
        _entries = catalog.Entries.ToDictionary(entry => entry.Card.Id);
        InitializeComponent();
        ActivityComboBox.ItemsSource = catalog.Entries
            .Where(entry => entry.Card.Kind == SfdDocumentKind.Activity)
            .OrderBy(entry => entry.Card.Reference, StringComparer.Ordinal)
            .Select(entry => new Choice(entry.Card.Id,
                entry.Card.Reference + " — " + entry.Card.Title)).ToArray();
        FunctionalityComboBox.ItemsSource = catalog.Entries
            .Where(entry => entry.Card.Kind == SfdDocumentKind.Functionality)
            .OrderBy(entry => entry.Card.Reference, StringComparer.Ordinal)
            .Select(entry => new Choice(entry.Card.Id,
                entry.Card.Reference + " — " + entry.Card.Title)).ToArray();
        if (ActivityComboBox.Items.Count > 0)
        {
            ActivityComboBox.SelectedIndex = 0;
        }

        if (FunctionalityComboBox.Items.Count > 0)
        {
            FunctionalityComboBox.SelectedIndex = 0;
        }

        AddButton.IsEnabled = !readOnly &&
            ActivityComboBox.Items.Count > 0 && FunctionalityComboBox.Items.Count > 0;
        Loaded += async (_, _) => await ReloadSafeAsync();
    }

    /// <summary>Indicates whether a human-reviewed change was saved.</summary>
    public bool Changed { get; private set; }

    /// <summary>Refreshes visible links after a successful save or conflict.</summary>
    /// <returns>Completed refresh.</returns>
    private async Task ReloadAsync()
    {
        SfdActivityFunctionalitySnapshot snapshot = await _service.ReadAsync(
            _projectDirectory);
        LinkChoice[] rows = snapshot.Registry.Links
            .OrderBy(link => _entries.TryGetValue(link.ActivityId,
                out SfdDocumentEntry? activity)
                ? activity.Card.Reference : string.Empty, StringComparer.Ordinal)
            .Select(link => new LinkChoice(link.ActivityId, link.FunctionalityId,
                LabelFor(link.ActivityId) + " ↔ " + LabelFor(link.FunctionalityId)))
            .ToArray();
        LinksListBox.ItemsSource = rows;
        RemoveButton.IsEnabled = !_readOnly && LinksListBox.SelectedItem is LinkChoice;
        StatusText.Text = rows.Length + " lien(s)";
    }

    /// <summary>Returns a human-readable current source label or an orphan marker.</summary>
    /// <param name="id">Stable SFD document identity.</param>
    /// <returns>Short UI label.</returns>
    private string LabelFor(Guid id) =>
        _entries.TryGetValue(id, out SfdDocumentEntry? entry)
            ? entry.Card.Reference + " — " + entry.Card.Title
            : "Objet introuvable (" + id.ToString("D") + ")";

    /// <summary>Shows recoverable storage errors rather than crashing the window.</summary>
    /// <returns>Completed error-protected refresh.</returns>
    private async Task ReloadSafeAsync()
    {
        try
        {
            await ReloadAsync();
        }
        catch (Exception exception) when (exception is IOException or
            InvalidDataException or UnauthorizedAccessException)
        {
            MessageBox.Show(this, exception.Message,
                "Liens SFD", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    /// <summary>Commits the explicitly selected association.</summary>
    /// <param name="sender">Add button.</param>
    /// <param name="e">Click event.</param>
    private async void Add_Click(object sender, RoutedEventArgs e)
    {
        if (_readOnly || ActivityComboBox.SelectedItem is not Choice activity ||
            FunctionalityComboBox.SelectedItem is not Choice function)
        {
            return;
        }

        try
        {
            await _service.LinkAsync(_projectDirectory, activity.Id, function.Id);
            Changed = true;
            await ReloadAsync();
        }
        catch (Exception exception) when (exception is IOException or
            InvalidOperationException or UnauthorizedAccessException)
        {
            MessageBox.Show(this, exception.Message,
                "Association SFD", MessageBoxButton.OK, MessageBoxImage.Warning);
            await ReloadSafeAsync();
        }
    }

    /// <summary>Commits explicit unlinking; never deletes either source document.</summary>
    /// <param name="sender">Remove button.</param>
    /// <param name="e">Click event.</param>
    private async void Remove_Click(object sender, RoutedEventArgs e)
    {
        if (_readOnly || LinksListBox.SelectedItem is not LinkChoice link)
        {
            return;
        }

        try
        {
            await _service.UnlinkAsync(_projectDirectory, link.ActivityId,
                link.FunctionalityId);
            Changed = true;
            await ReloadAsync();
        }
        catch (Exception exception) when (exception is IOException or
            InvalidOperationException or UnauthorizedAccessException)
        {
            MessageBox.Show(this, exception.Message,
                "Dissociation SFD", MessageBoxButton.OK, MessageBoxImage.Warning);
            await ReloadSafeAsync();
        }
    }

    /// <summary>Enables unlink only for a selected persisted pair.</summary>
    /// <param name="sender">Link selection.</param>
    /// <param name="e">Selection event.</param>
    private void Links_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        RemoveButton.IsEnabled = !_readOnly &&
            LinksListBox.SelectedItem is LinkChoice;
    }
}
