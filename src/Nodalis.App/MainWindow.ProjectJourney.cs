using System.Windows;
using Nodalis.App.Dialogs;
using Nodalis.Infrastructure.Projects;

namespace Nodalis.App;

/// <summary>Opt-in project journey commands, independent from the document tabs.</summary>
public partial class MainWindow
{
    /// <summary>
    /// Opens the project-local journey editor without creating a journey or
    /// modifying any Markdown during navigation or preview.
    /// </summary>
    /// <returns>Completed UI action.</returns>
    private Task OpenProjectJourneyAsync()
    {
        WorkspaceProjectStructureService structure = new(_root.FullPath);
        string? projectDirectory = structure.GetProjectDirectoryForContext(
            _selectedNode?.FullPath);
        if (projectDirectory is null)
        {
            MessageBox.Show(this,
                "Sélectionnez un projet ou un document appartenant à un projet.",
                "Parcours projet · Nodalis 2.0",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return Task.CompletedTask;
        }

        ProjectJourneyDialog dialog = new(
            _root.FullPath, projectDirectory, _workspaceReadOnly)
        {
            Owner = this
        };
        dialog.ShowDialog();
        StatusText.Text = dialog.Changed
            ? "Parcours projet · changements confirmés et enregistrés."
            : "Parcours projet · aucune modification effectuée.";
        return Task.CompletedTask;
    }
}
