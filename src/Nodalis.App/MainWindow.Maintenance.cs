using System.IO;
using System.Windows;
using Nodalis.App.Dialogs;
using Nodalis.App.Editor;
using Nodalis.App.Navigation;
using Nodalis.Core.Maintenance;
using Nodalis.Core.Navigation;
using Nodalis.Infrastructure.Maintenance;

namespace Nodalis.App;

/// <summary>Maintenance intervention actions shared by the editor toolbar and command palette.</summary>
public partial class MainWindow
{
    private readonly WorkspaceMaintenanceService _maintenanceService = new WorkspaceMaintenanceService();

    /// <summary>Shows maintenance buttons only for an open maintenance document in editable mode.</summary>
    private void UpdateMaintenanceActionVisibility()
    {
        DocumentTabViewModel? focused = GetFocusedDocumentTab();
        bool visible = !_workspaceReadOnly &&
            focused is not null &&
            IsMaintenanceDocument(focused.FullPath);
        NewMaintenanceInterventionButton.Visibility =
            visible ? Visibility.Visible : Visibility.Collapsed;
        EditMaintenanceInterventionButton.Visibility =
            visible ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>Recognizes a project's existing Markdown maintenance sheet.</summary>
    /// <param name="path">Document path.</param>
    /// <returns>Whether the basename denotes the maintenance sheet.</returns>
    private static bool IsMaintenanceDocument(string path)
    {
        return Path.GetFileName(path).Equals(
            "Fiche de Maintenance.md", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Finds the selected maintenance sheet without creating extra project files.</summary>
    /// <returns>Existing sheet path, or null if no relevant project is selected.</returns>
    private string? ResolveMaintenanceFilePath()
    {
        DocumentTabViewModel? focused = GetFocusedDocumentTab();
        if (focused is not null && IsMaintenanceDocument(focused.FullPath))
        {
            return focused.FullPath;
        }

        NavigationNodeViewModel? project = ResolveSelectedProjectForHealthCheck();
        if (project is null)
        {
            return null;
        }

        string path = Path.Combine(project.FullPath, "Fiche de Maintenance.md");
        return File.Exists(path) ? path : null;
    }

    /// <summary>Opens the compact new-intervention form from the toolbar or palette.</summary>
    /// <returns>The form and safe-persistence operation.</returns>
    private Task CreateMaintenanceInterventionAsync()
    {
        return EditMaintenanceHistoryAsync(editExisting: false);
    }

    /// <summary>Opens intervention selection and the same form with existing values.</summary>
    /// <returns>The validated edit operation.</returns>
    private Task EditMaintenanceInterventionAsync()
    {
        return EditMaintenanceHistoryAsync(editExisting: true);
    }

    /// <summary>Creates or edits a structured block without ever bypassing autosave or conflicts.</summary>
    /// <param name="editExisting">Whether to select a current intervention first.</param>
    /// <param name="selected">Optional exact history entry chosen from the timeline.</param>
    /// <returns>A task representing the user action.</returns>
    private async Task EditMaintenanceHistoryAsync(
        bool editExisting, MaintenanceHistoryItem? selected = null)
    {
        if (_workspaceReadOnly)
        {
            MessageBox.Show(this, "Ce workspace est ouvert en lecture seule.",
                "Maintenance", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        string? path = selected?.SourcePath ?? ResolveMaintenanceFilePath();
        if (path is null || !File.Exists(path))
        {
            MessageBox.Show(this,
                "Sélectionnez un projet possédant une fiche « Fiche de Maintenance.md », ou ouvrez cette fiche.",
                "Maintenance", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (!await FlushMaintenanceTabAsync(path))
        {
            return;
        }

        MaintenanceParseResult history = await _maintenanceService.ReadAsync(path);
        if (history.Diagnostics.Count > 0)
        {
            MessageBox.Show(this,
                "La fiche contient des blocs d'historique incohérents. Corrigez-les dans le Markdown avant de poursuivre.\n\n" +
                string.Join("\n", history.Diagnostics.Take(4).Select(item => item.Message)),
                "Maintenance", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        MaintenanceRecord? original = null;
        if (editExisting)
        {
            IReadOnlyList<MaintenanceRecord> interventions = history.Records
                .Where(item => item.Kind == MaintenanceRecordKind.Intervention)
                .ToArray();
            if (interventions.Count == 0)
            {
                MessageBox.Show(this, "Aucune intervention à modifier dans cette fiche.",
                    "Maintenance", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            if (selected is not null)
            {
                original = interventions.FirstOrDefault(item =>
                    string.Equals(item.Id, selected.Id, StringComparison.OrdinalIgnoreCase));
                if (original is null)
                {
                    MessageBox.Show(this,
                        "L'intervention a été modifiée ou supprimée depuis la consultation de l'historique. " +
                        "Rechargez l'historique ou ouvrez la source Markdown.",
                        "Maintenance", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
            }
            else
            {
                MaintenanceInterventionPickerDialog picker =
                    new MaintenanceInterventionPickerDialog(interventions) { Owner = this };
                if (picker.ShowDialog() != true || picker.SelectedIntervention is null)
                {
                    return;
                }

                original = picker.SelectedIntervention;
            }
        }

        IReadOnlyList<MaintenanceRecord> incidents = history.Records
            .Where(item => item.Kind == MaintenanceRecordKind.Incident)
            .ToArray();
        MaintenanceInterventionDialog dialog = new MaintenanceInterventionDialog(incidents, original)
        {
            Owner = this
        };
        if (dialog.ShowDialog() != true || dialog.Result is not MaintenanceRecord entry)
        {
            return;
        }

        // A slow form must never race unsaved changes in this document's editor tab.
        if (!await FlushMaintenanceTabAsync(path))
        {
            return;
        }

        string saved = original is null
            ? await _maintenanceService.AddAsync(path, entry)
            : await _maintenanceService.EditAsync(path, original, entry);
        await NavigateToSavedMaintenanceRecordAsync(path, saved, entry);
        StatusText.Text = original is null
            ? "Intervention ajoutée dans la fiche de maintenance"
            : "Intervention mise à jour dans la fiche de maintenance";
    }

    /// <summary>
    /// Flushes in-memory primary/secondary tab edits, rejecting unsaved conflicts rather
    /// than overwriting the same file through a second writer.
    /// </summary>
    /// <param name="path">Maintenance sheet path.</param>
    /// <returns>False if a conflict or pending unsaved change blocks a safe write.</returns>
    private async Task<bool> FlushMaintenanceTabAsync(string path)
    {
        StoreActiveDocumentTabState();
        StoreSecondaryDocumentTabState();
        DocumentTabViewModel? tab = _documentTabs.FirstOrDefault(candidate =>
            string.Equals(candidate.FullPath, path, StringComparison.OrdinalIgnoreCase));
        if (tab is null)
        {
            return true;
        }

        await tab.Autosave.FlushAsync();
        tab.IsDirty = !string.Equals(tab.Content, tab.Session.Content,
            StringComparison.Ordinal);
        if (!tab.IsDirty && !tab.HasConflict)
        {
            return true;
        }

        MessageBox.Show(this,
            "Cette fiche contient des modifications non enregistrées ou un conflit externe. " +
            "Enregistrez ou résolvez le conflit dans l'éditeur avant d'ajouter une intervention.",
            "Maintenance", MessageBoxButton.OK, MessageBoxImage.Warning);
        return false;
    }

    /// <summary>Reloads the saved source in its tab and navigates directly to the new block.</summary>
    /// <param name="path">Existing maintenance document path.</param>
    /// <param name="saved">Confirmed persisted contents.</param>
    /// <param name="entry">Inserted or modified record.</param>
    /// <returns>Editor navigation and refresh task.</returns>
    private async Task NavigateToSavedMaintenanceRecordAsync(
        string path, string saved, MaintenanceRecord entry)
    {
        DocumentTabViewModel? tab = _documentTabs.FirstOrDefault(candidate =>
            string.Equals(candidate.FullPath, path, StringComparison.OrdinalIgnoreCase));
        if (tab is not null)
        {
            await tab.Session.ReloadAsync();
            tab.Content = tab.Session.Content;
            tab.IsDirty = false;
            tab.HasConflict = false;
            tab.ConflictWarningShown = false;
        }

        NavigationNodeViewModel? node = FindAndExpand(_root, Path.GetFullPath(path));
        if (node is null)
        {
            await RefreshNavigationAsync(path);
            node = FindAndExpand(_root, Path.GetFullPath(path));
        }

        if (node is null || node.Kind != WorkspaceNodeKind.Document)
        {
            throw new InvalidOperationException("Intervention sauvegardée, mais sa fiche est introuvable dans l'explorateur.");
        }

        // The existing caret navigator targets the primary editor pane.
        // Move the target there first, even when maintenance was invoked from a split pane.
        SetFocusedEditorPane(EditorPaneSlot.Primary);
        await OpenOrActivateDocumentTabAsync(node);
        int position = saved.IndexOf("- **Identifiant :** " + entry.Id,
            StringComparison.OrdinalIgnoreCase);
        if (position >= 0)
        {
            int lineNumber = 1;
            for (int i = 0; i < position; i++)
            {
                if (saved[i] == '\n')
                {
                    lineNumber++;
                }
            }

            MoveCaretToLine(Math.Max(1, lineNumber - 1));
        }

        await RefreshLinkIndexAndContextAsync();
    }

    /// <summary>Handles the new-intervention editor button through the shared UI exception boundary.</summary>
    /// <param name="sender">Button.</param>
    /// <param name="e">Click event.</param>
    private async void NewMaintenanceIntervention_Click(object sender, RoutedEventArgs e)
    {
        await RunUiActionAsync("Nouvelle intervention de maintenance",
            CreateMaintenanceInterventionAsync);
    }

    /// <summary>Handles the existing-intervention editor button through the UI exception boundary.</summary>
    /// <param name="sender">Button.</param>
    /// <param name="e">Click event.</param>
    private async void EditMaintenanceIntervention_Click(object sender, RoutedEventArgs e)
    {
        await RunUiActionAsync("Modifier une intervention de maintenance",
            EditMaintenanceInterventionAsync);
    }
}
