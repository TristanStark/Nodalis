using System.IO;
using System.Windows;
using System.Windows.Controls;
using Nodalis.Infrastructure.Projects;

namespace Nodalis.App.Dialogs;

/// <summary>
/// Human-controlled, volatile mapping of original prompt tokens to previously
/// linked files, exact Markdown headings or manually entered values.
/// </summary>
public partial class ProjectJourneyPromptBindingsDialog : Window
{
    private readonly IReadOnlyList<string> _variables;
    private readonly List<ProjectJourneyPromptVariableBinding> _bindings = [];
    private readonly ProjectJourneyPromptSectionService _sections;
    private int _selectionRevision;
    private bool _loadingSections;

    /// <summary>Creates a source picker without writing any workspace file.</summary>
    /// <param name="workspaceRoot">Current single workspace.</param>
    /// <param name="variables">Exact tokens from the original prompt.</param>
    /// <param name="linkedSources">Only operation-linked source references.</param>
    public ProjectJourneyPromptBindingsDialog(
        string workspaceRoot, IReadOnlyList<string> variables,
        IReadOnlyList<string> linkedSources)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRoot);
        ArgumentNullException.ThrowIfNull(variables);
        ArgumentNullException.ThrowIfNull(linkedSources);
        _variables = variables;
        _sections = new ProjectJourneyPromptSectionService(workspaceRoot);
        InitializeComponent();
        VariablesList.ItemsSource = _variables;
        List<SourceChoice> choices =
        [
            new("Saisie manuelle / aucune source", null)
        ];
        foreach (string reference in linkedSources.Distinct(
                     StringComparer.OrdinalIgnoreCase))
        {
            string extension = Path.GetExtension(reference);
            if (extension.Equals(".md", StringComparison.OrdinalIgnoreCase) ||
                extension.Equals(".txt", StringComparison.OrdinalIgnoreCase))
            {
                choices.Add(new SourceChoice(reference, reference));
            }
        }

        SourceCombo.ItemsSource = choices;
        SectionCombo.ItemsSource =
            new[] { new SectionChoice("Document complet", null, null) };
        SectionCombo.SelectedIndex = 0;
        SourceCombo.SelectedIndex = 0;
        VariablesList.SelectedIndex = 0;
        UpdateControls();
    }

    /// <summary>Human-reviewed bindings; never written to project JSON.</summary>
    public IReadOnlyList<ProjectJourneyPromptVariableBinding> Bindings =>
        _bindings.ToArray();

    /// <summary>Displays how many mappings the selected token already has.</summary>
    /// <param name="sender">Token list.</param>
    /// <param name="e">Selection change.</param>
    private void VariablesList_SelectionChanged(
        object sender, SelectionChangedEventArgs e)
    {
        UpdateControls();
    }

    /// <summary>
    /// Scans ONLY a newly chosen, explicitly linked Markdown document.
    /// Old asynchronous results cannot replace a newer source selection.
    /// </summary>
    /// <param name="sender">Source selection.</param>
    /// <param name="e">Selection change.</param>
    private async void SourceCombo_SelectionChanged(
        object sender, SelectionChangedEventArgs e)
    {
        if (SectionCombo is null)
        {
            return;
        }

        int revision = ++_selectionRevision;
        _loadingSections = true;
        UpdateControls();
        SectionCombo.ItemsSource =
            new[] { new SectionChoice("Document complet", null, null) };
        SectionCombo.SelectedIndex = 0;
        PreviewBox.Text = string.Empty;
        string? reference = (SourceCombo.SelectedItem as SourceChoice)?.Reference;
        if (reference is null || !Path.GetExtension(reference).Equals(
                ".md", StringComparison.OrdinalIgnoreCase))
        {
            _loadingSections = false;
            UpdateControls();
            return;
        }

        try
        {
            IReadOnlyList<ProjectJourneyPromptSection> found =
                await Task.Run(() => _sections.ListAsync(reference));
            if (revision != _selectionRevision)
            {
                return;
            }

            SectionCombo.ItemsSource = new[]
            {
                new SectionChoice("Document complet", null, null)
            }.Concat(found.Select(item =>
                new SectionChoice(item.Label, item.StartLine, item.Title)))
                .ToArray();
            SectionCombo.SelectedIndex = 0;
            StatusText.Text = found.Count +
                " section(s) détectée(s) dans la source sélectionnée.";
        }
        catch (Exception exception) when (exception is
            IOException or InvalidDataException or ArgumentException or
            UnauthorizedAccessException)
        {
            if (revision == _selectionRevision)
            {
                StatusText.Text = "Source indisponible : " +
                    exception.Message + " · aucun contenu lu.";
                SourceCombo.SelectedIndex = 0;
            }
        }
        finally
        {
            if (revision == _selectionRevision)
            {
                _loadingSections = false;
                UpdateControls();
            }
        }
    }

    /// <summary>Clears a potentially stale source excerpt after navigation.</summary>
    /// <param name="sender">Section selection.</param>
    /// <param name="e">Selection change.</param>
    private void SectionCombo_SelectionChanged(
        object sender, SelectionChangedEventArgs e)
    {
        if (PreviewBox is not null)
        {
            PreviewBox.Text = string.Empty;
        }
    }

    /// <summary>
    /// Allows inspecting selected content before associating its token.
    /// Reads a capped excerpt off the Dispatcher and performs no mutation.
    /// </summary>
    /// <param name="sender">Explicit preview button.</param>
    /// <param name="e">Click event.</param>
    private async void Preview_Click(object sender, RoutedEventArgs e)
    {
        string? reference = (SourceCombo.SelectedItem as SourceChoice)?.Reference;
        SectionChoice? section = SectionCombo.SelectedItem as SectionChoice;
        if (reference is null || section is null || _loadingSections)
        {
            return;
        }

        int revision = _selectionRevision;
        PreviewSourceButton.IsEnabled = false;
        PreviewBox.Text = "Lecture de l'extrait sélectionné…";
        try
        {
            ProjectJourneyPromptExcerpt? excerpt = await Task.Run(() =>
                _sections.ReadAsync(reference, section.StartLine,
                    section.Title, 12000));
            if (revision == _selectionRevision)
            {
                PreviewBox.Text = excerpt is null
                    ? "Section déplacée ou supprimée : choisissez à nouveau."
                    : excerpt.Text;
                if (excerpt?.Truncated == true)
                {
                    StatusText.Text =
                        "Extrait limité à 12 000 caractères : consultez la source.";
                }
            }
        }
        catch (Exception exception) when (exception is
            IOException or InvalidDataException or ArgumentException or
            UnauthorizedAccessException)
        {
            if (revision == _selectionRevision)
            {
                PreviewBox.Text =
                    "Source inaccessible : " + exception.Message;
            }
        }
        finally
        {
            if (revision == _selectionRevision)
            {
                PreviewSourceButton.IsEnabled = true;
            }
        }
    }

    /// <summary>Adds exactly one explicit mapping without reading its body.</summary>
    /// <param name="sender">Associate button.</param>
    /// <param name="e">Click event.</param>
    private void AddBinding_Click(object sender, RoutedEventArgs e)
    {
        if (_loadingSections || VariablesList.SelectedItem is not string variable)
        {
            return;
        }

        string? reference = (SourceCombo.SelectedItem as SourceChoice)?.Reference;
        SectionChoice? section = SectionCombo.SelectedItem as SectionChoice;
        if (section is null)
        {
            return;
        }

        string? manual = reference is null ? ManualTextBox.Text : null;
        if (reference is null && string.IsNullOrWhiteSpace(manual))
        {
            StatusText.Text = "Saisissez un texte ou sélectionnez " +
                "un document déjà lié.";
            return;
        }

        ProjectJourneyPromptVariableBinding binding = new(
            variable, reference,
            reference is null ? null : section.StartLine,
            reference is null ? null : section.Title,
            manual);
        if (_bindings.Contains(binding))
        {
            StatusText.Text = "Cette association figure déjà dans le brouillon.";
            return;
        }

        _bindings.Add(binding);
        ManualTextBox.Text = string.Empty;
        RenderBindings();
        StatusText.Text = "Association ajoutée en mémoire. " +
            "La source sera revalidée lors de la préparation.";
    }

    /// <summary>Forgets a chosen mapping without touching any source file.</summary>
    /// <param name="sender">Remove button.</param>
    /// <param name="e">Click event.</param>
    private void RemoveBinding_Click(object sender, RoutedEventArgs e)
    {
        if (BindingsList.SelectedItem is BindingRow row)
        {
            _bindings.Remove(row.Binding);
            RenderBindings();
        }
    }

    /// <summary>Re-renders the volatile binding list after an explicit action.</summary>
    private void RenderBindings()
    {
        BindingsList.ItemsSource = _bindings.Select(binding =>
        {
            string detail = binding.SourceRelativePath ??
                "[texte saisi manuellement]";
            if (binding.HeadingTitle is string title)
            {
                detail += " · section L" + binding.HeadingLine + " " + title;
            }

            return new BindingRow(binding,
                binding.Variable + " ← " + detail);
        }).ToArray();
        UpdateControls();
    }

    /// <summary>Applies basic enablement while asynchronous headings load.</summary>
    private void UpdateControls()
    {
        if (AddBindingButton is null)
        {
            return;
        }

        bool hasVariable = VariablesList.SelectedItem is string;
        bool hasSource = (SourceCombo.SelectedItem as SourceChoice)?.Reference
            is not null;
        AddBindingButton.IsEnabled = hasVariable && !_loadingSections;
        PreviewSourceButton.IsEnabled = hasSource && !_loadingSections;
        ManualTextBox.IsEnabled = !hasSource;
    }

    /// <summary>Returns the reviewed choices, allowing unresolved tokens.</summary>
    /// <param name="sender">Prepare button.</param>
    /// <param name="e">Click event.</param>
    private void Confirm_Click(object sender, RoutedEventArgs e)
    {
        int missing = _variables.Count(variable =>
            _bindings.All(binding => binding.Variable != variable));
        if (missing > 0 && MessageBox.Show(this,
            missing + " variable(s) resteront non renseignée(s). " +
            "Elles resteront visibles dans le brouillon. Continuer ?",
            "Prompt partiellement renseigné",
            MessageBoxButton.YesNo, MessageBoxImage.Warning) !=
            MessageBoxResult.Yes)
        {
            return;
        }

        DialogResult = true;
    }

    /// <summary>Discards all mappings without writing a file.</summary>
    /// <param name="sender">Cancel button.</param>
    /// <param name="e">Click event.</param>
    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }

    /// <summary>One linked path or the explicit text-entry choice.</summary>
    /// <param name="Label">Displayed source name.</param>
    /// <param name="Reference">Workspace relative path or null.</param>
    private sealed record SourceChoice(string Label, string? Reference);

    /// <summary>Whole file or a stable physical Markdown heading anchor.</summary>
    /// <param name="Label">Readable selected section.</param>
    /// <param name="StartLine">Physical line or null for full file.</param>
    /// <param name="Title">Exact Markdown heading or null.</param>
    private sealed record SectionChoice(
        string Label, int? StartLine, string? Title);

    /// <summary>Display-only summary of a deliberate variable association.</summary>
    /// <param name="Binding">Volatile selection.</param>
    /// <param name="Description">Textual explanation for the reviewer.</param>
    private sealed record BindingRow(
        ProjectJourneyPromptVariableBinding Binding, string Description);
}
