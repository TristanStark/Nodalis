using System.IO;
using System.Windows;
using Nodalis.App.Dialogs;
using Nodalis.App.Navigation;
using Nodalis.Core.Navigation;
using Nodalis.Infrastructure.Persistence;

namespace Nodalis.App;

/// <summary>WPF entry point for explicit, project-local REC campaigns.</summary>
public partial class MainWindow
{
    /// <summary>
    /// Opens a single project-scoped campaign manager; its own file operations
    /// are backgrounded and the Worktree is refreshed once on dialog exit.
    /// </summary>
    /// <returns>Completed user-driven REC workflow.</returns>
    private async Task OpenProjectTestCampaignsAsync()
    {
        NavigationNodeViewModel? project = GetArchiveContextProject();
        if (project is null)
        {
            MessageBox.Show(this,
                "Sélectionnez un projet ou un de ses documents.",
                "Campagnes de tests · Nodalis 2.0",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        int matches = _root.DescendantsAndSelf().Count(node =>
            node.Kind == WorkspaceNodeKind.Project &&
            node.Id == project.Id);
        if (matches != 1)
        {
            MessageBox.Show(this,
                "L'identifiant du projet est ambigu : les résultats ne " +
                "peuvent pas être associés à une source unique.",
                "Projet ambigu · Nodalis 2.0",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        string relative = Path.GetRelativePath(
            _root.FullPath, project.FullPath);
        string rootFolder = relative.Split(Path.DirectorySeparatorChar)[0];
        bool archived = string.Equals(rootFolder,
            WorkspaceLayout.ArchivesDirectoryName,
            StringComparison.OrdinalIgnoreCase);
        ProjectTestCampaignsDialog dialog = new(
            _root.FullPath, project.FullPath,
            _workspaceReadOnly || archived)
        {
            Owner = this
        };
        bool? opened = dialog.ShowDialog();
        if (dialog.Changed)
        {
            await RefreshNavigationAsync(project.FullPath);
        }

        if (opened == true &&
            dialog.SelectedCampaignPath is string source)
        {
            await OpenProjectJourneySourceAsync(source);
        }
        else if (dialog.Changed)
        {
            StatusText.Text =
                "Campagnes REC actualisées · sources de tests inchangées.";
        }
    }
}
