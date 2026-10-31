using System.IO;
using System.Windows;
using Nodalis.App.Dialogs;
using Nodalis.Infrastructure.Projects;

namespace Nodalis.App;

public partial class MainWindow
{
    /// <summary>
    /// Opens a read-only, non-executable v2 Complex migration inventory from
    /// the project containing the currently selected document or folder.
    /// </summary>
    /// <returns>Preview and display task; no workspace mutations.</returns>
    private async Task ShowComplexMigrationPreviewAsync()
    {
        WorkspaceProjectStructureService structure =
            new WorkspaceProjectStructureService(_root.FullPath);

        string? projectDirectory = structure.GetProjectDirectoryForContext(
            _selectedNode?.FullPath);

        if (projectDirectory is null)
        {
            StatusText.Text =
                "Sélectionnez un projet Complexe ou un document qu'il contient.";
            return;
        }

        ComplexProjectMigrationPreviewService service =
            new ComplexProjectMigrationPreviewService(_root.FullPath);

        ComplexProjectMigrationPreview preview;
        try
        {
            preview = await service.PreviewAsync(projectDirectory);
        }
        catch (InvalidOperationException exception)
        {
            StatusText.Text = exception.Message;
            return;
        }

        ComplexMigrationPreviewDialog dialog =
            new ComplexMigrationPreviewDialog(preview)
            {
                Owner = this
            };

        dialog.ShowDialog();
        StatusText.Text =
            "Inventaire Complexe 2.0 consulté · aucune modification effectuée.";
    }
    /// <summary>
    /// Offers a checked, explicitly approved migration only after all project
    /// editors are closed and no earlier interrupted migration is pending.
    /// </summary>
    /// <returns>Preflight and optional durable migration task.</returns>
    private async Task MigrateComplexProjectAsync()
    {
        if (_workspaceReadOnly)
        {
            StatusText.Text = "Workspace en lecture seule : migration interdite.";
            return;
        }

        WorkspaceProjectStructureService structure =
            new WorkspaceProjectStructureService(_root.FullPath);
        string? projectDirectory = structure.GetProjectDirectoryForContext(
            _selectedNode?.FullPath);
        if (projectDirectory is null)
        {
            StatusText.Text =
                "Sélectionnez un projet Complexe avant de lancer sa migration.";
            return;
        }

        if (_documentTabs.Any(tab => IsProjectPathAncestorOrEqual(
                projectDirectory, tab.FullPath)))
        {
            MessageBox.Show(this,
                "Enregistrez et fermez tous les documents du projet et de ses sous-projets " +
                "avant de préparer une migration.",
                "Migration Complexe 2.0", MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        ComplexProjectMigrationTransactionService transaction = new(
            _root.FullPath);
        try
        {
            if ((await transaction.ListPendingProjectsAsync()).Count > 0)
            {
                MessageBox.Show(this,
                    "Une migration précédente doit être récupérée avant une nouvelle migration. " +
                    "Relancez Nodalis pour ouvrir le contrôle de récupération.",
                    "Migration Complexe 2.0", MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            StatusText.Text = "Migration Complexe · calcul des empreintes et risques…";
            ComplexProjectMigrationPlanService planner = new(_root.FullPath);
            ComplexProjectMigrationPlan plan = await Task.Run(
                () => planner.PrepareAsync(projectDirectory));

            HashSet<string> externalPaths = plan.ExternalLinkRewrites
                .Select(item => Path.GetFullPath(Path.Combine(_root.FullPath,
                    item.WorkspaceRelativePath.Replace('/',
                        Path.DirectorySeparatorChar))))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (_documentTabs.Any(tab => externalPaths.Contains(
                    Path.GetFullPath(tab.FullPath))))
            {
                MessageBox.Show(this,
                    "Enregistrez et fermez les documents externes concernés par " +
                    "des backlinks avant de préparer une migration.",
                    "Migration Complexe 2.0", MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            ComplexMigrationApprovalDialog dialog = new(plan)
            {
                Owner = this
            };
            if (dialog.ShowDialog() != true)
            {
                StatusText.Text =
                    "Migration Complexe · aucune modification effectuée.";
                return;
            }

            if (MessageBox.Show(this,
                "Vous confirmez la migration du projet vers la structure Complexe 2.0 ?\n\n" +
                "Empreinte vérifiée : " + plan.SourceFingerprint + "\n\n" +
                "L'original sera conservé pour restauration. " +
                plan.LinkRewrites.Count + " lien(s) local(aux) et " +
                plan.ExternalLinkRewrites.Count + " backlink(s) externe(s) seront " +
                "réécrits avec des sauvegardes individuelles et un journal récupérable.",
                "Confirmer la migration Complexe", MessageBoxButton.YesNo,
                MessageBoxImage.Warning) != MessageBoxResult.Yes)
            {
                return;
            }

            StatusText.Text = "Migration Complexe · staging et sauvegarde…";
            ComplexProjectMigrationResult result = await Task.Run(
                () => transaction.ApplyAsync(plan, plan.SourceFingerprint,
                    approved: true));
            await RefreshNavigationAsync(result.ProjectDirectory);
            await RefreshLinkIndexAndContextAsync();

            StatusText.Text =
                $"Migration Complexe réussie · {result.MovedSections} déplacement(s).";
            MessageBox.Show(this,
                "Le projet a été réorganisé.\n\n" +
                "Original conservé : " + result.BackupDirectory + "\n\n" +
                "Journal : " + result.JournalPath +
                "\n\nConservez cette sauvegarde jusqu'à qualification visuelle.",
                "Migration Complexe 2.0", MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception exception) when (exception is IOException or
            UnauthorizedAccessException or InvalidDataException or
            InvalidOperationException)
        {
            MessageBox.Show(this,
                "Aucune modification implicite ne sera tentée.\n\n" +
                exception.Message +
                "\n\nEn cas d'interruption pendant la bascule, la récupération " +
                "sera proposée au prochain démarrage.",
                "Migration Complexe 2.0", MessageBoxButton.OK,
                MessageBoxImage.Error);
            StatusText.Text = "Migration Complexe · refus ou récupération nécessaire.";
        }
    }

    /// <summary>
    /// Restores the most recent completed project migration only while the
    /// project and retained original are still fingerprint-identical to the
    /// approved transaction journal. Refuses open document tabs.
    /// </summary>
    /// <returns>Rollback approval, transaction and navigation refresh task.</returns>
    private async Task RollbackComplexProjectMigrationAsync()
    {
        if (_workspaceReadOnly)
        {
            StatusText.Text = "Workspace en lecture seule : restauration interdite.";
            return;
        }

        WorkspaceProjectStructureService structure = new(_root.FullPath);
        string? projectDirectory = structure.GetProjectDirectoryForContext(
            _selectedNode?.FullPath);
        if (projectDirectory is null)
        {
            StatusText.Text =
                "Sélectionnez un projet Complexe précédemment migré.";
            return;
        }

        if (_documentTabs.Any(tab => IsProjectPathAncestorOrEqual(
                projectDirectory, tab.FullPath)))
        {
            MessageBox.Show(this,
                "Enregistrez et fermez les documents ouverts dans ce projet " +
                "avant de restaurer sa structure précédente.",
                "Restauration Complexe 2.0", MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        ComplexProjectMigrationTransactionService executor = new(_root.FullPath);
        try
        {
            IReadOnlyList<ComplexProjectMigrationJournal> choices =
                await executor.GetRollbackCandidatesAsync(projectDirectory);
            if (choices.Count == 0)
            {
                MessageBox.Show(this,
                    "Aucune sauvegarde transactionnelle disponible pour ce projet.",
                    "Restauration Complexe 2.0", MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            ComplexProjectMigrationJournal candidate = choices[0];
            HashSet<string> externalPaths = candidate.ExternalLinkFiles
                .Select(item => Path.GetFullPath(Path.Combine(_root.FullPath,
                    item.WorkspaceRelativePath.Replace('/',
                        Path.DirectorySeparatorChar))))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (_documentTabs.Any(tab => externalPaths.Contains(
                    Path.GetFullPath(tab.FullPath))))
            {
                MessageBox.Show(this,
                    "Fermez les documents portant des backlinks avant de restaurer " +
                    "le projet. Ils seront restaurés avec lui.",
                    "Restauration Complexe 2.0", MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            if (MessageBox.Show(this,
                "Restaurer le projet dans son état antérieur à la migration ?\n\n" +
                "Transaction : " + candidate.TransactionId.ToString("D") + "\n" +
                "Empreinte originale : " + candidate.OriginalFingerprint + "\n\n" +
                "Nodalis refusera si le projet ou sa sauvegarde a changé. " +
                "La version 2.0 actuelle sera conservée pour inspection.",
                "Confirmer la restauration Complexe",
                MessageBoxButton.YesNo, MessageBoxImage.Warning) !=
                MessageBoxResult.Yes)
            {
                return;
            }

            StatusText.Text = "Restauration Complexe · vérification des empreintes…";
            string restored = await Task.Run(
                () => executor.RollbackAsync(candidate.TransactionId,
                    approved: true));
            await RefreshNavigationAsync(restored);
            await RefreshLinkIndexAndContextAsync();
            StatusText.Text =
                "Structure Complexe antérieure restaurée ; copie publiée conservée.";
        }
        catch (Exception exception) when (exception is IOException or
            UnauthorizedAccessException or InvalidDataException or
            InvalidOperationException)
        {
            MessageBox.Show(this,
                exception.Message +
                "\n\nAucun projet modifié ne sera écrasé automatiquement.",
                "Restauration Complexe 2.0", MessageBoxButton.OK,
                MessageBoxImage.Error);
            StatusText.Text = "Restauration Complexe · opération refusée.";
        }
    }

}
