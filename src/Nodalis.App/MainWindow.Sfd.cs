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
    /// <summary>Opens review-only SFD response comparison from the Projet menu.</summary>
    /// <param name="sender">SFD analysis menu action.</param>
    /// <param name="e">Click event.</param>
    private async void PilotageSfdImportReview_Click(object sender, RoutedEventArgs e) =>
        await RunUiActionAsync("Comparer la réponse IA SFD", OpenSfdImportReviewAsync);

    /// <summary>Verifies exact SFD project identity before opening a volatile review.</summary>
    /// <returns>Completed local-only response comparison window.</returns>
    private async Task OpenSfdImportReviewAsync()
    {
        NavigationNodeViewModel? project = PilotageProject();
        if (project is null)
        {
            ShowPilotageProjectHint("Comparer la réponse IA SFD");
            return;
        }

        SfdDocumentCatalog catalog = await new SfdDocumentRegistryService(
            _root.FullPath).ListAsync(project.FullPath);
        if (catalog.ProjectId != project.Id || catalog.Diagnostics.Count > 0)
        {
            throw new InvalidDataException(
                "Projet SFD incohérent : analyse suspendue.");
        }

        SfdImportReviewDialog dialog = new(_root.FullPath, project.FullPath,
            PilotageReadOnly(project))
        {
            Owner = this
        };
        dialog.ShowDialog();
        if (dialog.Changed)
        {
            await RefreshNavigationAsync(project.FullPath);
            StatusText.Text = "PM SFD publié après validation humaine.";
        }
        else
        {
            StatusText.Text = "Analyse SFD terminée · aucune écriture supplémentaire.";
        }
    }

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
            await RefreshNavigationAsync(project.FullPath);
            StatusText.Text =
                "Parcours SFD ou documents PM/ACT modifiés · navigation actualisée.";
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

    /// <summary>
    /// Converts eligible historical ACT folders into single Markdown files
    /// only after an explicit preview and user decision. Backups are retained.
    /// </summary>
    /// <returns>Completed SFD layout conversion and navigation refresh.</returns>
    private async Task ConvertLegacySfdActivitiesFromPaletteAsync()
    {
        NavigationNodeViewModel? project = PilotageProject();
        if (project is null)
        {
            ShowPilotageProjectHint("Convertir les activités SFD");
            return;
        }

        if (PilotageReadOnly(project))
        {
            MessageBox.Show(this, "Ce projet est en lecture seule.",
                "Conversion SFD", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        SfdFlatActivityMigrationService migration = new(_root.FullPath);
        SfdFlatActivityPreview preview = await migration.PreviewAsync(
            project.FullPath);
        if (preview.Candidates.Count == 0 &&
            !preview.ArchiveDuplicateOverview)
        {
            MessageBox.Show(this,
                "Aucun ancien dossier ACT simple à convertir.\n" +
                string.Join("\n", preview.Warnings.Take(6)),
                "Conversion SFD", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        string summary = "Convertir " + preview.Candidates.Count +
            " dossier(s) ACT simples en fichiers PM001-ACT002.md ?\n" +
            (preview.ArchiveDuplicateOverview
                ? "Le modèle secondaire « presentation projet.md » non modifié " +
                  "sera également archivé.\n" : "") +
            "Les dossiers d'origine seront conservés dans " +
            SfdFlatActivityMigrationService.BackupDirectoryName + ".\n" +
            "Fermez les documents ACT ouverts avant de continuer. " +
            "Les références textuelles externes peuvent nécessiter une vérification.\n\n" +
            string.Join("\n", preview.Warnings.Take(5));
        if (MessageBox.Show(this, summary, "Conversion SFD (avec sauvegarde)",
                MessageBoxButton.YesNo, MessageBoxImage.Warning) !=
            MessageBoxResult.Yes)
        {
            return;
        }

        SfdFlatActivityResult result = await migration.ConvertAsync(
            project.FullPath, preview);
        await RefreshNavigationAsync(project.FullPath);
        StatusText.Text = result.Converted +
            " activité(s) convertie(s) ; anciens dossiers sauvegardés.";
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
    private Task CreateSfdDocumentAsync() =>
        CreateSfdDocumentAsync(null);

    /// <summary>Creates a PM from the command palette, outside the SFD journey.</summary>
    /// <returns>User-reviewed process draft.</returns>
    private Task CreateSfdProcessFromPaletteAsync() =>
        CreateSfdDocumentAsync(SfdDocumentKind.Process);

    /// <summary>Creates an ACT from the command palette with an explicit PM parent.</summary>
    /// <returns>User-reviewed activity Markdown file.</returns>
    private Task CreateSfdActivityFromPaletteAsync() =>
        CreateSfdDocumentAsync(SfdDocumentKind.Activity);

    /// <summary>Opens an SFD-specific authoring dialog with optional fixed role.</summary>
    /// <param name="fixedKind">Explicitly requested PM or ACT, or any type from menu.</param>
    /// <returns>Completed user-reviewed creation.</returns>
    private async Task CreateSfdDocumentAsync(SfdDocumentKind? fixedKind)
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

        if (await new SfdJourneyService(_root.FullPath).ReadProjectKindAsync(
                project.FullPath, project.Id) !=
            Nodalis.Core.Domain.ProjectKind.WithSpecifications)
        {
            MessageBox.Show(this, "La création de PM/ACT exige un projet avec spec.",
                "Projet SFD", MessageBoxButton.OK, MessageBoxImage.Information);
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

        Guid? preferredParent = fixedKind == SfdDocumentKind.Activity
            ? FindSelectedSfdProcessId(catalog)
            : null;
        SfdNewDocumentDialog dialog = new(catalog.Entries,
            fixedKind, preferredParent)
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
    /// <summary>
    /// Suggests the current PM in the palette without assuming directory names
    /// or trusting the selected node as an identity. The dialog still allows
    /// changing the parent before committing the new ACT.
    /// </summary>
    /// <param name="catalog">Verified project-local SFD identities.</param>
    /// <returns>Selected PM GUID when a node belongs to one process.</returns>
    private Guid? FindSelectedSfdProcessId(SfdDocumentCatalog catalog)
    {
        string? selectedPath = _selectedNode?.FullPath;
        if (string.IsNullOrWhiteSpace(selectedPath))
        {
            return null;
        }

        foreach (SfdDocumentEntry process in catalog.Entries.Where(
            item => item.Card.Kind == SfdDocumentKind.Process))
        {
            string directory = Path.GetDirectoryName(process.AbsolutePath)!;
            if (string.Equals(selectedPath, process.AbsolutePath,
                    StringComparison.OrdinalIgnoreCase) ||
                string.Equals(selectedPath, directory,
                    StringComparison.OrdinalIgnoreCase) ||
                selectedPath.StartsWith(
                    Path.TrimEndingDirectorySeparator(directory) +
                        Path.DirectorySeparatorChar,
                    StringComparison.OrdinalIgnoreCase))
            {
                return process.Card.Id;
            }
        }

        return null;
    }

}
