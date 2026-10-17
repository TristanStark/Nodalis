using System.IO;
using System.Windows;
using System.Windows.Controls;
using Nodalis.App.Reliability;
using Nodalis.Core.Inbox;
using Nodalis.Core.Navigation;
using Nodalis.Infrastructure.Inbox;
using Nodalis.Infrastructure.Navigation;

namespace Nodalis.App.Dialogs;

/// <summary>
/// Explicit Inbox conversion wizard: only real Global/Application/Project
/// scopes, existing Markdown targets, previews and protected confirmation.
/// </summary>
public partial class InboxConversionDialog : Window
{
    private readonly string _root;
    private readonly InboxEntry _entry;
    private readonly string? _preferredContextPath;
    private readonly WorkspaceInboxConversionService _converter;
    private bool _ready;
    private int _previewRevision;

    /// <summary>Opens conversion choices for one non-trashed capture.</summary>
    /// <param name="workspaceRoot">Existing workspace.</param>
    /// <param name="entry">Inbox capture.</param>
    /// <param name="preferredContextPath">Optional active project for default selection.</param>
    public InboxConversionDialog(
        string workspaceRoot, InboxEntry entry, string? preferredContextPath = null)
    {
        ArgumentNullException.ThrowIfNull(entry);
        _root = Path.GetFullPath(workspaceRoot);
        _entry = entry;
        _preferredContextPath = preferredContextPath;
        _converter = new WorkspaceInboxConversionService(_root);
        InitializeComponent();
        Loaded += async (_, _) =>
            await UiActionGuard.RunAsync(this, "Inbox · destinations", LoadChoicesAsync);
    }

    /// <summary>Conversion target after the confirmation was committed.</summary>
    public InboxConversionResult? Result { get; private set; }

    /// <summary>Loads the standard navigation hierarchy to select valid scopes and documents.</summary>
    /// <returns>A task representing the initial preview.</returns>
    private async Task LoadChoicesAsync()
    {
        WorkspaceNavigationBuilder builder = new WorkspaceNavigationBuilder();
        WorkspaceNavigationNode root = await builder.BuildAsync(_root);
        List<ScopeChoice> scopes = new List<ScopeChoice>();
        CollectScopes(root, scopes);

        InboxConversionRequest? resume =
            await _converter.ReadPreparedConversionAsync(_entry.Id);
        ScopeComboBox.ItemsSource = scopes;
        string? preferred = resume is null ? _preferredContextPath : null;
        if (!string.IsNullOrWhiteSpace(preferred))
        {
            string full = Path.GetFullPath(preferred);
            ScopeChoice? scope = scopes
                .Where(candidate => IsInsideOrEqual(full, candidate.Node.FullPath))
                .OrderByDescending(candidate => candidate.Node.FullPath.Length)
                .FirstOrDefault();
            ScopeComboBox.SelectedItem = scope ?? scopes.FirstOrDefault();
        }
        else
        {
            ScopeComboBox.SelectedItem = resume is null
                ? scopes.FirstOrDefault()
                : scopes.FirstOrDefault(candidate =>
                    string.Equals(candidate.RelativeDirectory,
                        resume.DestinationRelativeDirectory,
                        StringComparison.OrdinalIgnoreCase));
        }

        ModeComboBox.SelectedIndex = resume?.Mode switch
        {
            InboxConversionMode.NewDocument => 1,
            InboxConversionMode.AppendDocument => 2,
            InboxConversionMode.MarkdownTask => 3,
            _ => 0
        };
        if (resume?.Mode == InboxConversionMode.NewDocument)
        {
            NewFileTextBox.Text = resume.NewDocumentName ?? string.Empty;
        }

        _ready = true;
        RefreshTargetOptions();
        if (resume?.Mode == InboxConversionMode.AppendDocument)
        {
            ExistingFileComboBox.SelectedItem =
                ExistingFileComboBox.Items.OfType<TargetChoice>().FirstOrDefault(
                    item => string.Equals(item.RelativePath,
                        resume.ExistingDocumentRelativePath,
                        StringComparison.OrdinalIgnoreCase));
        }

        if (resume is not null)
        {
            // An interrupted intent can only be replayed to its original
            // target. Locking these controls prevents accidental divergence.
            ScopeComboBox.IsEnabled = false;
            ModeComboBox.IsEnabled = false;
            NewFileTextBox.IsEnabled = false;
            ExistingFileComboBox.IsEnabled = false;
        }

        await UpdatePreviewAsync();
        if (resume is not null)
        {
            ValidationText.Text = ConvertButton.IsEnabled
                ? "Reprise d'une conversion interrompue : destination verrouillée, " +
                  "la cible existante sera vérifiée avant toute écriture."
                : "Conversion interrompue : vérifier ou restaurer le document cible. " +
                  "La capture reste conservée dans l'Inbox.";
        }
    }

    /// <summary>Collects canonical scope nodes while avoiding grouping-row choices.</summary>
    /// <param name="node">Current navigation node.</param>
    /// <param name="choices">Accumulated valid scopes.</param>
    private void CollectScopes(WorkspaceNavigationNode node, List<ScopeChoice> choices)
    {
        if (node.Kind is WorkspaceNodeKind.Global or
            WorkspaceNodeKind.Application or WorkspaceNodeKind.Project)
        {
            string relative = Path.GetRelativePath(_root, node.FullPath)
                .Replace('\\', '/');
            choices.Add(new ScopeChoice(
                node,
                relative == "." ? string.Empty : relative,
                node.Kind + " · " + node.DisplayName));
        }

        foreach (WorkspaceNavigationNode child in node.Children)
        {
            CollectScopes(child, choices);
        }
    }

    /// <summary>Refreshes the existing-file picker for the selected scope.</summary>
    private void RefreshTargetOptions()
    {
        bool newDoc = SelectedMode == InboxConversionMode.NewDocument;
        bool existing = SelectedMode == InboxConversionMode.AppendDocument;
        NewFileLabel.Visibility = newDoc ? Visibility.Visible : Visibility.Collapsed;
        NewFileTextBox.Visibility = NewFileLabel.Visibility;
        ExistingFileLabel.Visibility = existing ? Visibility.Visible : Visibility.Collapsed;
        ExistingFileComboBox.Visibility = ExistingFileLabel.Visibility;

        if (ScopeComboBox.SelectedItem is not ScopeChoice scope)
        {
            ExistingFileComboBox.ItemsSource = null;
            return;
        }

        List<TargetChoice> targets = new List<TargetChoice>();
        CollectDocuments(scope.Node, scope.Node, targets);
        ExistingFileComboBox.ItemsSource = targets;
        ExistingFileComboBox.SelectedItem = targets.FirstOrDefault(item =>
            Path.GetFileName(item.RelativePath).Equals(
                "Fiche de Maintenance.md", StringComparison.OrdinalIgnoreCase))
            ?? targets.FirstOrDefault();
    }

    /// <summary>
    /// Shows native Markdown files without silently crossing into another
    /// project's ownership when an application was selected.
    /// </summary>
    /// <param name="scope">Selected scope node.</param>
    /// <param name="node">Current subtree.</param>
    /// <param name="targets">Matching Markdown files.</param>
    private void CollectDocuments(
        WorkspaceNavigationNode scope,
        WorkspaceNavigationNode node,
        List<TargetChoice> targets)
    {
        if (!ReferenceEquals(scope, node) &&
            node.Kind is (WorkspaceNodeKind.Project or WorkspaceNodeKind.Application))
        {
            return;
        }

        if (node.Kind == WorkspaceNodeKind.Document &&
            node.FullPath.EndsWith(".md", StringComparison.OrdinalIgnoreCase) &&
            File.Exists(node.FullPath))
        {
            string relative = Path.GetRelativePath(_root, node.FullPath)
                .Replace('\\', '/');
            targets.Add(new TargetChoice(relative,
                Path.GetRelativePath(scope.FullPath, node.FullPath)));
        }

        foreach (WorkspaceNavigationNode child in node.Children)
        {
            CollectDocuments(scope, child, targets);
        }
    }

    /// <summary>Rebuilds target options after selecting a project or format.</summary>
    /// <param name="sender">Combo box.</param>
    /// <param name="e">Selection changed.</param>
    private async void ConversionOption_Changed(
        object sender, SelectionChangedEventArgs e)
    {
        if (!_ready)
        {
            return;
        }

        if (ReferenceEquals(sender, ExistingFileComboBox))
        {
            await UpdatePreviewAsync();
            return;
        }

        RefreshTargetOptions();
        await UpdatePreviewAsync();
    }

    /// <summary>Refreshes the preview on new-document filename changes.</summary>
    /// <param name="sender">Filename box.</param>
    /// <param name="e">Text event.</param>
    private async void NewFileTextBox_TextChanged(
        object sender, TextChangedEventArgs e)
    {
        if (_ready && SelectedMode == InboxConversionMode.NewDocument)
        {
            await UpdatePreviewAsync();
        }
    }

    /// <summary>The chosen native conversion type.</summary>
    private InboxConversionMode SelectedMode =>
        ModeComboBox.SelectedIndex switch
        {
            1 => InboxConversionMode.NewDocument,
            2 => InboxConversionMode.AppendDocument,
            3 => InboxConversionMode.MarkdownTask,
            _ => InboxConversionMode.QuickNote
        };

    /// <summary>Builds one immutable request from the visible UI.</summary>
    /// <returns>User-selected scope, mode and target.</returns>
    private InboxConversionRequest GetRequest()
    {
        if (ScopeComboBox.SelectedItem is not ScopeChoice scope)
        {
            throw new InvalidOperationException("Sélectionner un projet, une application ou Global.");
        }

        if (SelectedMode == InboxConversionMode.AppendDocument &&
            ExistingFileComboBox.SelectedItem is not TargetChoice)
        {
            throw new InvalidOperationException(
                "Aucun document Markdown existant dans cette destination.");
        }

        return new InboxConversionRequest
        {
            EntryId = _entry.Id,
            Mode = SelectedMode,
            DestinationRelativeDirectory = scope.RelativeDirectory,
            NewDocumentName = SelectedMode == InboxConversionMode.NewDocument
                ? NewFileTextBox.Text : null,
            ExistingDocumentRelativePath = SelectedMode == InboxConversionMode.AppendDocument
                ? ((TargetChoice)ExistingFileComboBox.SelectedItem).RelativePath
                : null
        };
    }

    /// <summary>Updates the read-only preview and keeps invalid input non-destructive.</summary>
    /// <returns>Preview task.</returns>
    private async Task UpdatePreviewAsync()
    {
        int revision = ++_previewRevision;
        try
        {
            InboxConversionRequest request = GetRequest();
            string markdown = await _converter.PreviewAsync(request);
            if (revision != _previewRevision)
            {
                return;
            }

            ConversionPreviewTextBox.Text = markdown;
            ValidationText.Text = "Aperçu uniquement : aucune cible n'est modifiée avant Convertir.";
            ConvertButton.IsEnabled = true;
        }
        catch (Exception exception) when (exception is
            ArgumentException or InvalidOperationException or IOException)
        {
            if (revision != _previewRevision)
            {
                return;
            }

            ConversionPreviewTextBox.Text = _entry.ContentMarkdown;
            ValidationText.Text = exception.Message;
            ConvertButton.IsEnabled = false;
        }
    }

    /// <summary>Commits the user's selected format and keeps the dialog open on failure.</summary>
    /// <param name="sender">Convert button.</param>
    /// <param name="e">Click event.</param>
    private async void Convert_Click(object sender, RoutedEventArgs e)
    {
        await UiActionGuard.RunAsync(this, "Inbox · convertir", async () =>
        {
            ConvertButton.IsEnabled = false;
            try
            {
                InboxConversionRequest request = GetRequest();
                Result = await _converter.ConvertAsync(request);
                DialogResult = true;
            }
            finally
            {
                ConvertButton.IsEnabled = true;
            }
        });
    }

    /// <summary>Checks that a path belongs to a candidate existing scope.</summary>
    /// <param name="path">Active context.</param>
    /// <param name="scope">Scope root.</param>
    /// <returns>Whether the user was working within it.</returns>
    private static bool IsInsideOrEqual(string path, string scope) =>
        string.Equals(path, scope, StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith(Path.TrimEndingDirectorySeparator(scope) +
            Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

    /// <summary>One actual Global, Application or Project destination.</summary>
    /// <param name="Node">Navigation node.</param>
    /// <param name="RelativeDirectory">Path under workspace.</param>
    /// <param name="DisplayName">Picker label.</param>
    private sealed record ScopeChoice(
        WorkspaceNavigationNode Node, string RelativeDirectory, string DisplayName);

    /// <summary>One real Markdown file selectable as an append destination.</summary>
    /// <param name="RelativePath">Workspace-relative file path.</param>
    /// <param name="DisplayName">Picker label.</param>
    private sealed record TargetChoice(string RelativePath, string DisplayName);
}
