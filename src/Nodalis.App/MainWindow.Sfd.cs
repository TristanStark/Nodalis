using System.IO;
using System.Windows;
using Nodalis.App.Dialogs;
using Nodalis.App.Navigation;
using Nodalis.Core.Sfd;
using Nodalis.Infrastructure.Projects;

namespace Nodalis.App;

/// <summary>Project-menu entry point for human-authorized SFD document creation.</summary>
public partial class MainWindow
{
    /// <summary>Opens the SFD journey from the Projet menu.</summary>
    /// <param name="sender">SFD journey menu action.</param>
    /// <param name="e">Click event.</param>
    private async void PilotageSfdJourney_Click(object sender, RoutedEventArgs e) =>
        await RunUiActionAsync("Parcours Projet SFD", () =>
            OpenSfdJourneyAsync(PilotageProject()));

    /// <summary>
    /// Routes the existing generic Parcours action by the authoritative
    /// project kind, without activating the classic journey on SFD projects.
    /// </summary>
    /// <param name="project">Current exact project node, or null.</param>
    /// <returns>Opened correct project journey UI.</returns>
    private async Task OpenSelectedProjectJourneyAsync(NavigationNodeViewModel? project)
    {
        if (project is null)
        {
            ShowPilotageProjectHint("Parcours projet");
            return;
        }

        Nodalis.Core.Domain.ProjectKind projectKind =
            await new SfdJourneyService(_root.FullPath)
                .ReadProjectKindAsync(project.FullPath, project.Id);
        if (projectKind == Nodalis.Core.Domain.ProjectKind.WithSpecifications)
        {
            await OpenSfdJourneyAsync(project);
        }
        else
        {
            await OpenProjectJourneyAsync(project);
        }
    }

    /// <summary>
    /// Opens the SFD-only opt-in journey editor without changing the
    /// selected Markdown tabs, project files or traditional journey data.
    /// </summary>
    /// <param name="project">Selected canonical SFD project.</param>
    /// <returns>Completed explicit session.</returns>
    private async Task OpenSfdJourneyAsync(NavigationNodeViewModel? project)
    {
        if (project is null)
        {
            ShowPilotageProjectHint("Parcours SFD");
            return;
        }

        SfdDocumentCatalog catalog = await new SfdDocumentRegistryService(
            _root.FullPath).ListAsync(project.FullPath);
        if (catalog.ProjectId != project.Id || catalog.Diagnostics.Count > 0)
        {
            throw new InvalidDataException(
                "Projet SFD introuvable ou identité documentaire ambiguë.");
        }

        SfdJourneyDialog dialog = new(_root.FullPath, project.FullPath,
            PilotageReadOnly(project))
        {
            Owner = this
        };
        dialog.ShowDialog();
        if (dialog.Changed)
        {
            StatusText.Text =
                "Parcours SFD personnalisé ; statuts déclaratifs uniquement.";
        }
    }

    /// <summary>Opens the SFD C/SC/RM authoring and completion dashboard.</summary>
    /// <param name="sender">Project menu item.</param>
    /// <param name="e">Click event.</param>
    private async void PilotageSfdStructure_Click(object sender, RoutedEventArgs e) =>
        await RunUiActionAsync("Complétude SFD", OpenSfdStructureAsync);

    /// <summary>Resolves only the selected SFD project before showing structured editor.</summary>
    /// <returns>Human-reviewed C/SC/RM edits and completion readout.</returns>
    private async Task OpenSfdStructureAsync()
    {
        NavigationNodeViewModel? project = PilotageProject();
        if (project is null)
        {
            ShowPilotageProjectHint("Conditions et règles SFD");
            return;
        }

        SfdDocumentCatalog catalog = await new SfdDocumentRegistryService(
            _root.FullPath).ListAsync(project.FullPath);
        if (catalog.ProjectId != project.Id || catalog.Diagnostics.Count > 0)
        {
            throw new InvalidDataException(
                "Les identités du projet SFD sont ambiguës : vérifiez les documents.");
        }

        SfdStructureDialog dialog = new(_root.FullPath, project.FullPath,
            catalog, PilotageReadOnly(project))
        {
            Owner = this
        };
        dialog.ShowDialog();
        if (dialog.Changed)
        {
            await RefreshNavigationAsync(project.FullPath);
            StatusText.Text = "Brouillons C/SC et références RM SFD mis à jour.";
        }
    }

    /// <summary>Opens explicit project-local SFD activity/functionality linking.</summary>
    /// <param name="sender">Project menu item.</param>
    /// <param name="e">Click event.</param>
    private async void PilotageSfdLinks_Click(object sender, RoutedEventArgs e) =>
        await RunUiActionAsync("Liens activités et fonctionnalités SFD",
            OpenSfdLinksAsync);

    /// <summary>Resolves one SFD project and opens the GUID-based N:N links editor.</summary>
    /// <returns>Completed user-reviewed association editing.</returns>
    private async Task OpenSfdLinksAsync()
    {
        NavigationNodeViewModel? project = PilotageProject();
        if (project is null)
        {
            ShowPilotageProjectHint("Liens ACT/FCT SFD");
            return;
        }

        SfdDocumentRegistryService registry = new(_root.FullPath);
        SfdDocumentCatalog catalog = await registry.ListAsync(project.FullPath);
        if (catalog.ProjectId != project.Id || catalog.Diagnostics.Count > 0)
        {
            throw new InvalidDataException(
                "Identités SFD incohérentes : corrigez les fiches avant de lier.");
        }

        SfdActivityLinksDialog dialog = new(
            new SfdActivityFunctionalityService(_root.FullPath),
            project.FullPath, catalog, PilotageReadOnly(project))
        {
            Owner = this
        };
        dialog.ShowDialog();
        if (dialog.Changed)
        {
            StatusText.Text = "Associations ACT/FCT SFD enregistrées.";
        }
    }

    /// <summary>Opens the SFD draft form under the actual selected project.</summary>
    /// <param name="sender">Project menu item.</param>
    /// <param name="e">Click event.</param>
    private async void PilotageNewSfdDocument_Click(
        object sender, RoutedEventArgs e) =>
        await RunUiActionAsync("Nouvel objet SFD", CreateSfdDocumentAsync);

    /// <summary>
    /// Resolves the current project and creates one reviewed SFD document.
    /// No implicit default objects, IA invocation or 2.0 journey activation.
    /// </summary>
    /// <returns>Completed user-approved creation and navigation.</returns>
    private async Task CreateSfdDocumentAsync()
    {
        NavigationNodeViewModel? project = PilotageProject();
        if (project is null)
        {
            ShowPilotageProjectHint("Nouvel objet SFD");
            return;
        }

        if (PilotageReadOnly(project))
        {
            MessageBox.Show(this, "Le projet est en lecture seule.",
                "Nouvel objet SFD", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        Guid projectId = project.Id;
        string projectPath = project.FullPath;
        SfdDocumentRegistryService service = new(_root.FullPath);
        SfdDocumentCatalog catalog = await service.ListAsync(projectPath);
        if (catalog.ProjectId != projectId || catalog.Diagnostics.Count > 0)
        {
            MessageBox.Show(this,
                catalog.Diagnostics.Count > 0
                    ? "Des fiches SFD présentent des identités incohérentes :\n" +
                      string.Join("\n", catalog.Diagnostics.Take(5))
                    : "Le projet sélectionné a changé d'identité.",
                "Création SFD suspendue", MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        SfdNewDocumentDialog dialog = new(catalog.Entries)
        {
            Owner = this
        };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        NavigationNodeViewModel? stillSelected = PilotageProject();
        if (stillSelected is null || stillSelected.Id != projectId ||
            !string.Equals(stillSelected.FullPath, projectPath,
                StringComparison.OrdinalIgnoreCase) ||
            PilotageReadOnly(stillSelected))
        {
            StatusText.Text = "Projet SFD modifié depuis la sélection : création annulée.";
            return;
        }

        SfdDocumentEntry created = await service.CreateAsync(
            projectPath, dialog.SelectedKind, dialog.DocumentTitle,
            dialog.SelectedParentId, dialog.SelectedMode);
        await RefreshNavigationAsync(created.AbsolutePath);
        StatusText.Text = "Fiche SFD créée · " + created.Card.Reference;
    }
}
