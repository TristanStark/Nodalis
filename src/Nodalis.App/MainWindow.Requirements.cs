using System.IO;
using System.Windows;
using Nodalis.App.Dialogs;
using Nodalis.App.Navigation;
using Nodalis.Core.Domain;
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
        string selectedProjectPath = project.FullPath;
        // File enumeration, UTF-8 decoding and Markdown validation can all
        // be expensive on real workspaces. Keep them off the WPF Dispatcher.
        ProjectRequirementCatalog catalog = await Task.Run(
            () => service.ListAsync(selectedProjectPath));
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
        ProjectRequirementEntry created = await Task.Run(
            () => service.CreateAsync(selectedProjectPath, title, expected));
        WorkspaceLinkIndexService.NotifyWorkspaceChanged(_root.FullPath);
        // Creation is bound to the explicitly confirmed project, even if the
        // user navigated to another application while the worker ran.
        // Do not steal focus from a different project after publication.
        if (GetArchiveContextProject()?.Id == project.Id)
        {
            await OpenProjectJourneySourceAsync(created.RelativePath);
        }
        StatusText.Text = "Exigence créée · " + created.Card.Reference +
            " · brouillon non vérifié.";
    }

    /// <summary>
    /// Builds a read-only requirement-to-test block matrix from a refreshed
    /// shared index; no separate registry, synchronous file scan or implied
    /// execution/validation is introduced.
    /// </summary>
    /// <returns>Completed user-requested project overview.</returns>
    private async Task ShowProjectRequirementTraceabilityAsync()
    {
        NavigationNodeViewModel? project = GetArchiveContextProject();
        if (project is null)
        {
            MessageBox.Show(this,
                "Sélectionnez un projet ou un document de ce projet.",
                "Traçabilité des exigences · Nodalis 2.0",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        int duplicateProjects = _root.DescendantsAndSelf().Count(node =>
            node.Kind == WorkspaceNodeKind.Project && node.Id == project.Id);
        if (duplicateProjects != 1)
        {
            MessageBox.Show(this,
                "L'identifiant de projet est ambigu : aucune matrice " +
                "ne peut être calculée sans identité unique.",
                "Projet ambigu · Nodalis 2.0",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        string projectPath = project.FullPath;
        ProjectRequirementRegistryService service = new(_root.FullPath);

        // The index may need an incremental workspace refresh; perform all
        // enumeration and joins off the WPF Dispatcher, once per request.
        (ProjectRequirementTraceabilityOverview Links,
            ProjectRequirementCampaignOverview History,
            ProjectRequirementCatalog Requirements,
            ProjectCriterionBaselineOverview Baseline) prepared =
            await Task.Run(async () =>
            {
                ProjectRequirementCatalog requirements =
                    await service.ListAsync(projectPath);
                Nodalis.Core.Links.LinkIndexCatalog index =
                    await _linkIndexService.RefreshAsync();
                ProjectRequirementTraceabilityOverview links =
                    ProjectRequirementTraceability.Build(requirements, index);

                // REC sources are read from the SAME selected project. A bad
                // campaign folder must not make the EXI link matrix vanish.
                ProjectTestCampaignCatalog campaigns;
                try
                {
                    ProjectTestCampaignService campaignService = new(
                        _root.FullPath);
                    campaigns = await campaignService.ListAsync(projectPath);
                }
                catch (Exception exception) when (
                    exception is InvalidDataException or IOException or
                    UnauthorizedAccessException)
                {
                    campaigns = new ProjectTestCampaignCatalog(
                        [],
                        ["Historique REC indisponible : " +
                            exception.GetType().Name +
                            ". Les résultats ne peuvent pas être interprétés."],
                        null);
                }

                ProjectRequirementCampaignOverview history =
                    ProjectRequirementCampaignTraceability.Build(
                        links, campaigns);

                ProjectCriterionBaselineOverview baseline;
                try
                {
                    baseline = await new ProjectCriterionBaselineService(
                        _root.FullPath).InspectAsync(projectPath, requirements);
                }
                catch (Exception exception) when (exception is
                    InvalidDataException or IOException or
                    UnauthorizedAccessException or InvalidOperationException)
                {
                    baseline = new ProjectCriterionBaselineOverview(
                        null, [], ["Historique CA indisponible (" +
                            exception.GetType().Name +
                            ") : état de référence indéterminé, " +
                            "enregistrement désactivé."]);
                }

                return (links, history, requirements, baseline);
            });

        if (GetArchiveContextProject()?.Id != project.Id)
        {
            StatusText.Text =
                "Le projet a changé pendant la lecture : relancez la matrice.";
            return;
        }

        bool archived = string.Equals(
            Path.GetRelativePath(_root.FullPath, projectPath)
                .Split(Path.DirectorySeparatorChar)[0],
            WorkspaceLayout.ArchivesDirectoryName,
            StringComparison.OrdinalIgnoreCase);
        ProjectRequirementTraceabilityDialog dialog = new(
            prepared.Links, prepared.History,
            prepared.Requirements, prepared.Baseline,
            _root.FullPath, projectPath, _workspaceReadOnly || archived)
        {
            Owner = this
        };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        if (dialog.SelectedTestBlockId is Guid testId)
        {
            // Navigate by stable GUID with live source revalidation, never by
            // the possibly obsolete line captured in the matrix snapshot.
            await NavigateToMarkdownBlockAsync(testId);
        }
        else if (dialog.SelectedRequirementPath is string relativePath)
        {
            await OpenProjectJourneySourceAsync(relativePath);
        }
    }
}
