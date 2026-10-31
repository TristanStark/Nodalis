using System.IO;
using System.Windows;
using Nodalis.App.Navigation;
using Nodalis.Core.Navigation;
using Nodalis.Infrastructure.Persistence;
using Nodalis.Infrastructure.Projects;

namespace Nodalis.App;

/// <summary>Archive and restore commands for closed projects.</summary>
public partial class MainWindow
{
    /// <summary>Detects a path physically located under the workspace Archives folder.</summary>
    /// <param name="path">Full path to inspect.</param>
    /// <returns>Whether it lies inside Archives.</returns>
    private bool IsUnderArchives(string path)
    {
        string root = Path.Combine(_root.FullPath, WorkspaceLayout.ArchivesDirectoryName);
        return Path.GetFullPath(path).StartsWith(
            root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Recognizes the archived root project, not one of its nested subprojects.</summary>
    /// <param name="node">A project navigation node.</param>
    /// <returns>Whether the project can be restored directly.</returns>
    private bool IsArchivedTopLevelProject(NavigationNodeViewModel node)
    {
        return node.Kind == WorkspaceNodeKind.Project &&
            string.Equals(Path.GetDirectoryName(node.FullPath),
                Path.Combine(_root.FullPath, WorkspaceLayout.ArchivesDirectoryName),
                StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Resolves the selected project or the project owning a selected file.</summary>
    /// <returns>Closest project in the canonical worktree, or null.</returns>
    private NavigationNodeViewModel? GetArchiveContextProject()
    {
        string? path = _selectedNode?.FullPath ?? GetFocusedDocumentTab()?.FullPath;
        if (path is null)
        {
            return null;
        }

        return _root.DescendantsAndSelf()
            .Where(node => node.Kind == WorkspaceNodeKind.Project &&
                IsProjectPathAncestorOrEqual(node.FullPath, path))
            .OrderByDescending(node => node.FullPath.Length)
            .FirstOrDefault();
    }

    /// <summary>Archives the selected project from the Command Palette.</summary>
    /// <returns>Archive task.</returns>
    private Task ArchiveProjectAsync()
    {
        return ArchiveProjectAsync(GetArchiveContextProject());
    }

    /// <summary>Restores the selected project from the Command Palette.</summary>
    /// <returns>Restore task.</returns>
    private Task RestoreArchivedProjectAsync()
    {
        return RestoreArchivedProjectAsync(GetArchiveContextProject());
    }

    /// <summary>Moves the complete project to Archives after confirmation and tab safety checks.</summary>
    /// <param name="project">Selected project.</param>
    /// <returns>Archive and navigation task.</returns>
    private async Task ArchiveProjectAsync(NavigationNodeViewModel? project)
    {
        if (_workspaceReadOnly)
        {
            MessageBox.Show(this, "Workspace en lecture seule.", "Archives",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (project is null || project.Kind != WorkspaceNodeKind.Project ||
            IsUnderArchives(project.FullPath))
        {
            MessageBox.Show(this, "Sélectionnez un projet actif à clore.",
                "Archives", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (HasOpenProjectTabs(project.FullPath))
        {
            MessageBox.Show(this,
                "Fermez d'abord tous les onglets ouverts de ce projet et de ses sous-projets. " +
                "Vérifiez que leurs modifications sont enregistrées avant l'archivage.",
                "Archives", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (MessageBox.Show(this,
            "Clore le projet « " + project.DisplayName + " » et déplacer tout son contenu " +
            "dans Archives ?\n\nCette action est réversible avec « Réouvrir le projet ». " +
            "Elle ne supprime aucun fichier.",
            "Clore et archiver", MessageBoxButton.YesNo,
            MessageBoxImage.Question) != MessageBoxResult.Yes)
        {
            return;
        }

        ProjectArchiveService service = new ProjectArchiveService(_root.FullPath);
        string archivedPath = await service.ArchiveAsync(project.FullPath);
        await RefreshNavigationAsync(archivedPath);
        StatusText.Text = "Projet clos et archivé · " + project.DisplayName;
    }

    /// <summary>Restores an archived project at its original, collision-free path.</summary>
    /// <param name="project">Selected archived project.</param>
    /// <returns>Restore and navigation task.</returns>
    private async Task RestoreArchivedProjectAsync(NavigationNodeViewModel? project)
    {
        if (_workspaceReadOnly)
        {
            MessageBox.Show(this, "Workspace en lecture seule.", "Archives",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (project is null || !IsArchivedTopLevelProject(project))
        {
            MessageBox.Show(this, "Sélectionnez un projet directement sous Archives.",
                "Archives", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (HasOpenProjectTabs(project.FullPath))
        {
            MessageBox.Show(this, "Fermez les onglets du projet avant de le réouvrir.",
                "Archives", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        ProjectArchiveService service = new ProjectArchiveService(_root.FullPath);
        string restoredPath = await service.RestoreAsync(project.FullPath);
        await RefreshNavigationAsync(restoredPath);
        StatusText.Text = "Projet réouvert · " + project.DisplayName;
    }

    /// <summary>Never move a directory that still has documents open in the tab system.</summary>
    /// <param name="projectDirectory">Project directory.</param>
    /// <returns>True if at least one tab references the project subtree.</returns>
    private bool HasOpenProjectTabs(string projectDirectory)
    {
        StoreActiveDocumentTabState();
        StoreSecondaryDocumentTabState();
        return _documentTabs.Any(tab =>
            IsProjectPathAncestorOrEqual(projectDirectory, tab.FullPath));
    }
}
