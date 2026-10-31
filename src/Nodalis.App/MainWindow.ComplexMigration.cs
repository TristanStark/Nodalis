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
}
