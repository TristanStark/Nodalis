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
                "L'original sera conservé pour restauration. Cette opération ne " +
                "réécrit pas les liens Markdown : tout lien détecté bloque déjà le plan.",
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

}
