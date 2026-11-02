using System.IO;
using System.Windows;
using Nodalis.App.Dialogs;
using Nodalis.App.Navigation;
using Nodalis.Core.Navigation;
using Nodalis.Infrastructure.Links;
using Nodalis.Infrastructure.Projects;

namespace Nodalis.App;

public partial class MainWindow
{
    /// <summary>
    /// Creates one project-local EXI Markdown draft only after an explicit
    /// title and a confirmation of its next human-readable reference.
    /// </summary>
    /// <returns>Completed human-driven creation workflow.</returns>
    private async Task CreateProjectRequirementAsync()
    {
        if (_workspaceReadOnly)
        {
            StatusText.Text = "Workspace en lecture seule : création d'exigence refusée.";
            return;
        }

        NavigationNodeViewModel? project = GetArchiveContextProject();
        if (project is null)
        {
            MessageBox.Show(this,
                "Sélectionnez un projet ou l'un de ses documents.",
                "Nouvelle exigence · Nodalis 2.0",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        // Never act on a project context whose identity is ambiguous.
        int projectsWithSameId = _root.DescendantsAndSelf().Count(node =>
            node.Kind == WorkspaceNodeKind.Project &&
            node.Id == project.Id);
        if (projectsWithSameId != 1)
        {
            MessageBox.Show(this,
                "Plusieurs projets utilisent cet identifiant : " +
                "résolvez la collision avant de créer une exigence.",
                "Identifiant de projet ambigu",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        TextPromptDialog titleDialog = new(
            "Créer une exigence · Nodalis 2.0",
            "Titre vérifiable de l'exigence (240 caractères maximum) :")
        {
            Owner = this
        };
        if (titleDialog.ShowDialog() != true)
        {
            return;
        }

        // The title is never used as a filename. The canonical path is EXI-xxxx.
        string title = titleDialog.Value;
        if (title.Length > 240 || title.Any(char.IsControl))
        {
            MessageBox.Show(this,
                "Utilisez un titre sur une seule ligne (240 caractères maximum).",
                "Titre d'exigence invalide",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        ProjectRequirementRegistryService service = new(_root.FullPath);
        ProjectRequirementCatalog catalog = await service.ListAsync(
            project.FullPath);
        if (catalog.NextReference is null)
        {
            MessageBox.Show(this,
                "Toutes les références EXI disponibles sont utilisées.",
                "Références épuisées",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        string expected = catalog.NextReference;
        string notice = catalog.Diagnostics.Count == 0
            ? string.Empty
            : "\n\nAttention : " + catalog.Diagnostics.Count +
              " document(s) du registre ne sont pas interprétables ; " +
              "ils resteront inchangés.";
        if (MessageBox.Show(this,
                "Créer une exigence dans le projet « " +
                project.DisplayName + " » ?\n\n" +
                "Référence : " + expected + "\n" +
                "Titre : " + title + "\n\n" +
                "État : brouillon · non réalisée · non vérifiée\n" +
                "Fichier : Documentation/Exigences/" + expected + ".md" +
                notice + "\n\nConfirmer la création ?",
                "Nouvelle exigence · confirmation",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question) != MessageBoxResult.Yes)
        {
            return;
        }

        // No overwrite and no unreviewed reallocation if another instance
        // created the displayed EXI reference in the meantime.
        ProjectRequirementEntry created = await service.CreateAsync(
            project.FullPath, title, expected);
        WorkspaceLinkIndexService.NotifyWorkspaceChanged(_root.FullPath);
        await OpenProjectJourneySourceAsync(created.RelativePath);
        StatusText.Text = "Exigence créée · " + created.Card.Reference +
            " · brouillon non vérifié.";
    }
}
