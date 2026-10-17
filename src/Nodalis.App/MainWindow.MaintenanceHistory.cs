using System.IO;
using System.Text;
using System.Windows;
using Nodalis.App.Dialogs;
using Nodalis.App.Editor;
using Nodalis.App.Navigation;
using Nodalis.Core.Maintenance;
using Nodalis.Core.Navigation;

namespace Nodalis.App;

/// <summary>Provides read-only project/application maintenance history and precise source navigation.</summary>
public partial class MainWindow
{
    /// <summary>Opens an indexed, searchable history for the current project or application.</summary>
    /// <returns>Context search and source navigation task.</returns>
    private async Task ShowMaintenanceHistoryAsync()
    {
        NavigationNodeViewModel? selected = _selectedNode;
        if (GetFocusedDocumentTab() is { } focused &&
            IsMaintenanceDocument(focused.FullPath))
        {
            selected = FindAndExpand(_root, focused.FullPath) ?? selected;
        }

        if (selected is null)
        {
            MessageBox.Show(this, "Sélectionnez d'abord un projet ou une application.",
                "Historique de maintenance", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        NavigationNodeViewModel? project = ResolveSelectedProjectForHealthCheck();
        // Prefer the selected/focused document's project if one is available.
        if (selected.Kind == WorkspaceNodeKind.Document)
        {
            project = _root.DescendantsAndSelf()
                .Where(node => node.Kind == WorkspaceNodeKind.Project &&
                    IsProjectPathAncestorOrEqual(node.FullPath, selected.FullPath))
                .OrderByDescending(node => node.FullPath.Length)
                .FirstOrDefault();
        }

        NavigationNodeViewModel? application = _root.DescendantsAndSelf()
            .Where(node => node.Kind == WorkspaceNodeKind.Application &&
                IsProjectPathAncestorOrEqual(node.FullPath, selected.FullPath))
            .OrderByDescending(node => node.FullPath.Length)
            .FirstOrDefault();

        if (project is null && application is null)
        {
            MessageBox.Show(this, "Sélectionnez un projet ou une application pour consulter son historique.",
                "Historique de maintenance", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        string scopePath = application?.FullPath ?? project!.FullPath;
        NavigationNodeViewModel[] fileNodes = _root.DescendantsAndSelf()
            .Where(node => node.Kind == WorkspaceNodeKind.Document &&
                IsMaintenanceDocument(node.FullPath) &&
                IsProjectPathAncestorOrEqual(scopePath, node.FullPath))
            .ToArray();

        using SemaphoreSlim throttle = new SemaphoreSlim(4);
        Task<(string Path, string Project, IReadOnlyList<MaintenanceHistoryItem> Rows,
            string? Error)>[] loads = fileNodes.Select(node =>
                LoadMaintenanceHistoryFileAsync(node, throttle)).ToArray();
        (string Path, string Project, IReadOnlyList<MaintenanceHistoryItem> Rows,
            string? Error)[] files = await Task.WhenAll(loads);
        MaintenanceHistoryItem[] all = files.SelectMany(item => item.Rows).ToArray();
        string[] errors = files
            .Where(item => item.Error is not null)
            .Select(item => item.Project + " : " + item.Error)
            .ToArray();
        List<MaintenanceHistoryScope> scopes = [];

        if (project is not null)
        {
            MaintenanceHistoryItem[] inProject = all
                .Where(item => string.Equals(
                    Path.GetDirectoryName(item.SourcePath), project.FullPath,
                    StringComparison.OrdinalIgnoreCase)).ToArray();
            scopes.Add(new MaintenanceHistoryScope("Projet · " + project.DisplayName, inProject));
        }

        if (application is not null)
        {
            scopes.Add(new MaintenanceHistoryScope("Application · " + application.DisplayName, all));
        }

        MaintenanceHistoryDialog dialog = new MaintenanceHistoryDialog(scopes, errors)
        {
            Owner = this
        };
        if (dialog.ShowDialog() != true || dialog.SelectedEntry is not MaintenanceHistoryItem entry)
        {
            return;
        }

        if (dialog.SelectedAction == MaintenanceHistoryAction.EditIntervention)
        {
            await EditMaintenanceHistoryAsync(editExisting: true, selected: entry);
        }
        else
        {
            await OpenMaintenanceHistorySourceAsync(entry);
        }
    }

    /// <summary>Reads one Markdown document with bounded parallelism and explicit error reporting.</summary>
    /// <param name="node">A canonical navigation document.</param>
    /// <param name="throttle">Shared file-load bound.</param>
    /// <returns>Entries or an error for the source; never an implicitly missing file.</returns>
    private static async Task<(string Path, string Project,
        IReadOnlyList<MaintenanceHistoryItem> Rows, string? Error)>
        LoadMaintenanceHistoryFileAsync(
            NavigationNodeViewModel node, SemaphoreSlim throttle)
    {
        string path = node.FullPath;
        string project = Path.GetFileName(Path.GetDirectoryName(path)) ?? "(Projet)";
        await throttle.WaitAsync();
        try
        {
            string text = await File.ReadAllTextAsync(path, Encoding.UTF8);
            IReadOnlyList<MaintenanceHistoryItem> rows =
                MaintenanceHistoryIndex.Parse(path, project, text);
            return (path, project, rows, null);
        }
        catch (Exception exception) when (exception is IOException or
                UnauthorizedAccessException or DecoderFallbackException)
        {
            return (path, project, [], exception.Message);
        }
        finally
        {
            throttle.Release();
        }
    }

    /// <summary>Opens a selected source, resolving its stable identifier after external edits.</summary>
    /// <param name="entry">Timeline entry with source path and original line.</param>
    /// <returns>Editor navigation task.</returns>
    private async Task OpenMaintenanceHistorySourceAsync(MaintenanceHistoryItem entry)
    {
        NavigationNodeViewModel? node = FindAndExpand(_root, Path.GetFullPath(entry.SourcePath));
        if (node is null || node.Kind != WorkspaceNodeKind.Document)
        {
            MessageBox.Show(this, "Cette fiche a été déplacée ou supprimée. Actualisez l'explorateur.",
                "Historique de maintenance", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        // Keep unsaved in-memory edits. Otherwise refresh a clean tab if another
        // program changed its file after the history window was opened.
        DocumentTabViewModel? existing = _documentTabs.FirstOrDefault(tab =>
            string.Equals(tab.FullPath, entry.SourcePath, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            if (ReferenceEquals(existing, _activeDocumentTab))
            {
                StoreActiveDocumentTabState();
            }

            if (ReferenceEquals(existing, _secondaryDocumentTab))
            {
                StoreSecondaryDocumentTabState();
            }

            if (!existing.IsDirty && !existing.HasConflict &&
                await existing.Session.HasExternalChangesAsync())
            {
                await existing.Session.ReloadAsync();
                existing.Content = existing.Session.Content;
            }
        }

        SetFocusedEditorPane(EditorPaneSlot.Primary);
        await OpenOrActivateDocumentTabAsync(node);
        string current = MarkdownEditorTextBox.Text;
        IReadOnlyList<MaintenanceHistoryItem> currentEntries =
            MaintenanceHistoryIndex.Parse(entry.SourcePath, entry.ProjectName, current);
        MaintenanceHistoryItem? currentEntry = entry.Id is null
            ? currentEntries.FirstOrDefault(item =>
                item.Kind == entry.Kind && item.LineNumber == entry.LineNumber &&
                string.Equals(item.Summary, entry.Summary, StringComparison.Ordinal))
            : currentEntries.FirstOrDefault(item =>
                item.Kind == entry.Kind &&
                string.Equals(item.Id, entry.Id, StringComparison.OrdinalIgnoreCase));

        if (currentEntry is null)
        {
            MessageBox.Show(this,
                "La source a changé depuis la recherche. La fiche est ouverte, mais l'entrée " +
                "n'a pas pu être retrouvée avec certitude. Recherchez-la dans le Markdown.",
                "Historique de maintenance", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        MoveCaretToLine(currentEntry.LineNumber);
        StatusText.Text = "Historique · " + currentEntry.KindLabel + " · " +
            currentEntry.SourceLabel;
    }

    /// <summary>Handles the history button via the shared recoverable-error boundary.</summary>
    /// <param name="sender">Button.</param>
    /// <param name="e">Click event.</param>
    private async void OpenMaintenanceHistory_Click(object sender, RoutedEventArgs e)
    {
        await RunUiActionAsync("Historique de maintenance", ShowMaintenanceHistoryAsync);
    }
}
