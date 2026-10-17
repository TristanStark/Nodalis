using System.IO;
using System.Windows;
using Nodalis.App.Dialogs;
using Nodalis.Core.Links;
using Nodalis.Core.Maintenance;
using Nodalis.Core.Navigation;
using Nodalis.Infrastructure.Links;

namespace Nodalis.App;

/// <summary>Connects maintenance records to the existing GUID-based typed relation system.</summary>
public partial class MainWindow
{
    /// <summary>
    /// Adds a relation to one selected incident or intervention, after confirming
    /// both the stable registry target and the unmodified source-block revision.
    /// </summary>
    /// <param name="record">Snapshot selected from maintenance history.</param>
    /// <returns>A safe UI and atomic Markdown write task.</returns>
    private async Task AddMaintenanceDocumentRelationAsync(MaintenanceHistoryItem record)
    {
        if (_workspaceReadOnly)
        {
            MessageBox.Show(this, "Le workspace est ouvert en lecture seule.",
                "Relations de maintenance", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        await RefreshLinkIndexAsync();
        LinkTargetEntry[] targets = _linkIndex.Targets.Where(target =>
            target.Kind == LinkTargetKind.Document &&
            !string.Equals(
                Path.GetFullPath(Path.Combine(_root.FullPath, target.RelativePath)),
                Path.GetFullPath(record.SourcePath),
                StringComparison.OrdinalIgnoreCase)).ToArray();

        if (targets.Length == 0)
        {
            MessageBox.Show(this,
                "Aucun autre document indexé dans ce workspace. Créez d'abord une spécification, " +
                "une décision ou une note technique.",
                "Relations de maintenance", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        // Reuse the exact typed-relation picker used by all Nodalis documents.
        // The neutral default avoids asserting a dependency the user did not choose.
        RelationDialog picker = new RelationDialog(targets, "est lié à")
        {
            Owner = this
        };
        if (picker.ShowDialog() != true || picker.SelectedTarget is not LinkTargetEntry selected)
        {
            return;
        }

        if (!await FlushMaintenanceTabAsync(record.SourcePath))
        {
            return;
        }

        // A document can be renamed or removed while the picker is open.
        // Never silently retarget a relation by its mutable display label.
        await RefreshLinkIndexAsync();
        LinkTargetEntry? currentTarget = _linkIndex.Targets.FirstOrDefault(target =>
            target.Id == selected.Id && target.Kind == LinkTargetKind.Document);
        if (currentTarget is null || !File.Exists(
                Path.Combine(_root.FullPath, currentTarget.RelativePath)))
        {
            MessageBox.Show(this,
                "Ce document a été déplacé hors du workspace ou supprimé pendant la sélection. " +
                "Actualisez les références avant de créer le lien.",
                "Relations de maintenance", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        string saved = await _maintenanceService.AddRelationAsync(
            record, picker.RelationType, currentTarget.Id, currentTarget.DisplayName);
        MaintenanceRecord? source = MaintenanceMarkdownCodec.Parse(saved).Records
            .FirstOrDefault(item =>
                string.Equals(item.Id, record.Id, StringComparison.OrdinalIgnoreCase));
        if (source is null)
        {
            throw new InvalidDataException(
                "La relation a été enregistrée mais son bloc source ne peut pas être retrouvé.");
        }

        await NavigateToSavedMaintenanceRecordAsync(record.SourcePath, saved, source);
        await RefreshLinkIndexAndContextAsync();
        StatusText.Text = "Relation de maintenance ajoutée · " + picker.RelationType +
            " → " + currentTarget.DisplayName + " (GUID conservé)";
    }

    /// <summary>
    /// Browses explicitly declared relations of one record. Incident view also includes
    /// references from interventions with that exact IncidentId in the same file.
    /// </summary>
    /// <param name="record">Source history record.</param>
    /// <returns>Target navigation, or a readable broken-link warning.</returns>
    private async Task BrowseMaintenanceDocumentReferencesAsync(MaintenanceHistoryItem record)
    {
        if (!File.Exists(record.SourcePath))
        {
            MessageBox.Show(this, "La fiche de maintenance n'existe plus.",
                "Références de maintenance", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        await RefreshLinkIndexAsync();
        string markdown = await File.ReadAllTextAsync(record.SourcePath);
        IReadOnlyList<MaintenanceDocumentReference> projected =
            MaintenanceRelationIndex.Parse(record.SourcePath, markdown, _linkIndex);
        IReadOnlyList<MaintenanceDocumentReference> references =
            record.Kind == MaintenanceRecordKind.Incident
                ? MaintenanceRelationIndex.ForIncident(record, projected)
                : MaintenanceRelationIndex.ForRecord(record, projected);

        MaintenanceReferencesDialog dialog = new MaintenanceReferencesDialog(
            references, record.KindLabel + " · " + record.Summary)
        {
            Owner = this
        };
        if (dialog.ShowDialog() != true ||
            dialog.SelectedReference?.TargetId is not Guid selectedId)
        {
            return;
        }

        // Resolve again on demand; registry snapshots may have been invalidated
        // by a rename, a move, a soft deletion or a restoration while viewing.
        await RefreshLinkIndexAsync();
        LinkTargetEntry? current = _linkIndex.Targets.FirstOrDefault(target =>
            target.Id == selectedId && target.Kind == LinkTargetKind.Document);
        if (current is null)
        {
            MessageBox.Show(this,
                "La cible de la relation est introuvable dans le registre actuel. " +
                "Elle est peut-être supprimée ; restaurez-la avant de réessayer.",
                "Référence cassée", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        string targetPath = Path.GetFullPath(Path.Combine(
            _root.FullPath, current.RelativePath));
        string workspacePrefix = Path.TrimEndingDirectorySeparator(_root.FullPath) +
            Path.DirectorySeparatorChar;
        if (!targetPath.StartsWith(workspacePrefix, StringComparison.OrdinalIgnoreCase) ||
            !File.Exists(targetPath))
        {
            MessageBox.Show(this,
                "Le document correspondant au GUID de la relation est absent. " +
                "Aucun autre document homonyme ne sera ouvert.",
                "Référence cassée", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        NavigationNodeViewModel? node = FindAndExpand(_root, targetPath);
        if (node is null)
        {
            await RefreshNavigationAsync();
            node = FindAndExpand(_root, targetPath);
        }

        if (node is null || node.Kind != WorkspaceNodeKind.Document)
        {
            MessageBox.Show(this,
                "La cible a été retrouvée dans le registre, mais pas dans l'explorateur. " +
                "Actualisez l'arborescence.",
                "Références de maintenance", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        SetFocusedEditorPane(EditorPaneSlot.Primary);
        await OpenOrActivateDocumentTabAsync(node);
        StatusText.Text = "Document référencé · " + current.QualifiedName;
    }
}
