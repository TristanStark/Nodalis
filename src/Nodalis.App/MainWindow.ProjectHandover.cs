using System.IO;
using System.Windows;
using Nodalis.App.Dialogs;
using Nodalis.App.Navigation;
using Nodalis.Core.Navigation;
using Nodalis.Infrastructure.Projects;

namespace Nodalis.App;

/// <summary>Read-only preflight and explicit handover intention entry points.</summary>
public partial class MainWindow
{
    /// <summary>Opens the non-executing inter-project handover workflow.</summary>
    /// <param name="sender">Projet menu item.</param>
    /// <param name="e">WPF click event.</param>
    private async void PilotageHandoverIntent_Click(
        object sender, RoutedEventArgs e) =>
        await RunUiActionAsync("Préparer une passation",
            PrepareProjectHandoverAsync);

    /// <summary>
    /// Displays actual source project items and existing destination projects,
    /// then records an intention only after a fresh source/target preflight and
    /// independent human confirmation.
    /// </summary>
    /// <returns>Human-driven preparation without modifying business objects.</returns>
    private async Task PrepareProjectHandoverAsync()
    {
        NavigationNodeViewModel? project = PilotageProject();
        if (project is null)
        {
            ShowPilotageProjectHint("Passation inter-projets");
            return;
        }

        Guid sourceId = project.Id;
        string sourcePath = project.FullPath;
        bool readOnly = PilotageReadOnly(project);
        ProjectClosureAuditService audits =
            new ProjectClosureAuditService(_root.FullPath);
        ProjectHandoverIntentService intentions =
            new ProjectHandoverIntentService(_root.FullPath);
        (ProjectClosureAuditSnapshot? Audit, ProjectHandoverIntentSnapshot History)
            loaded = await Task.Run(async () =>
            {
                ProjectHandoverIntentSnapshot history =
                    await intentions.ReadAsync(sourcePath);
                ProjectClosureAuditSnapshot? audit = readOnly
                    ? null : await audits.ReadAsync(sourcePath);
                return (audit, history);
            });
        NavigationNodeViewModel? selected = PilotageProject();
        if (selected is null || selected.Id != sourceId ||
            !string.Equals(selected.FullPath, sourcePath,
                StringComparison.OrdinalIgnoreCase))
        {
            StatusText.Text = "Projet modifié pendant la lecture des passations.";
            return;
        }

        ProjectHandoverTargetChoice[] targets = _root.DescendantsAndSelf()
            .Where(node => node.Kind == WorkspaceNodeKind.Project &&
                node.Id != sourceId && !IsUnderArchives(node.FullPath) &&
                Directory.Exists(node.FullPath))
            .Select(node => new ProjectHandoverTargetChoice(
                node.Id, node.FullPath,
                node.DisplayName + " · " + Path.GetRelativePath(
                    _root.FullPath, node.FullPath)))
            .OrderBy(node => node.Label, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
        ProjectHandoverIntentDialog dialog = new(
            loaded.Audit, targets, loaded.History, readOnly)
        { Owner = this };
        if (dialog.ShowDialog() != true ||
            dialog.ApprovedRequest is not ProjectHandoverRequest choice)
        {
            return;
        }

        selected = PilotageProject();
        if (selected is null || selected.Id != sourceId ||
            !string.Equals(selected.FullPath, sourcePath,
                StringComparison.OrdinalIgnoreCase) ||
            PilotageReadOnly(selected))
        {
            StatusText.Text =
                "Projet source déplacé ou en lecture seule : préparation annulée.";
            return;
        }

        ProjectHandoverPreflightService preflight =
            new ProjectHandoverPreflightService(_root.FullPath);
        ProjectHandoverPreview preview = await Task.Run(() =>
            preflight.PrepareAsync(sourcePath, choice.TargetDirectory,
                choice.ItemId, choice.Method));
        selected = PilotageProject();
        if (selected is null || selected.Id != sourceId ||
            !string.Equals(selected.FullPath, sourcePath,
                StringComparison.OrdinalIgnoreCase) ||
            PilotageReadOnly(selected))
        {
            StatusText.Text =
                "Sélection modifiée pendant le préflight de passation.";
            return;
        }

        if (!preview.CanRecord)
        {
            MessageBox.Show(this, preview.FormatReview(),
                "Passation bloquée par le contrôle des identités",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (MessageBox.Show(this, preview.FormatReview() + "\n" +
            "Consigner cette intention sans exécuter le transfert ?",
            "Confirmer la préparation de passation",
            MessageBoxButton.YesNo, MessageBoxImage.Question) !=
            MessageBoxResult.Yes)
        {
            return;
        }

        ProjectHandoverIntentSnapshot recorded = await Task.Run(() =>
            intentions.RecordIntentAsync(preview, loaded.History,
                choice.Actor, choice.Rationale));
        await RefreshNavigationAsync(sourcePath);
        StatusText.Text = "Passation préparée · intention n°" +
            recorded.Ledger.Intents.Count +
            " conservée, aucun objet ni lien déplacé.";
    }
}
