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
        ProjectHandoverTaskTransactionService transactions =
            new ProjectHandoverTaskTransactionService(_root.FullPath);
        ProjectRequirementSuccessorTransactionService exiTransactions =
            new ProjectRequirementSuccessorTransactionService(_root.FullPath);
        (ProjectClosureAuditSnapshot? Audit,
            ProjectHandoverIntentSnapshot History,
            IReadOnlyList<ProjectHandoverTaskJournal> Transactions,
            IReadOnlyList<ProjectRequirementSuccessorJournal> ExiTransactions)
            loaded = await Task.Run(async () =>
            {
                ProjectHandoverIntentSnapshot history =
                    await intentions.ReadAsync(sourcePath);
                ProjectClosureAuditSnapshot? audit = readOnly
                    ? null : await audits.ReadAsync(sourcePath);
                IReadOnlyList<ProjectHandoverTaskJournal> transactionHistory =
                    await transactions.ListHistoryAsync();
                IReadOnlyList<ProjectRequirementSuccessorJournal> exiHistory =
                    await exiTransactions.ListHistoryAsync();
                return (audit, history, transactionHistory, exiHistory);
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
            loaded.Audit, targets, loaded.History,
            loaded.Transactions, loaded.ExiTransactions, readOnly)
        { Owner = this };
        if (dialog.ShowDialog() != true)
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

        if (dialog.RequestedRecovery)
        {
            await RecoverProjectHandoverTasksAsync(transactions, sourcePath);
            return;
        }

        if (dialog.RequestedRollbackTransactionId is Guid transactionId)
        {
            await RollbackProjectHandoverTaskAsync(
                transactions, loaded.Transactions, transactionId, sourcePath);
            return;
        }

        if (dialog.RequestedExiRecovery)
        {
            await RecoverRequirementSuccessorsAsync(exiTransactions,
                sourcePath);
            return;
        }

        if (dialog.RequestedExiRollbackId is Guid exiRollbackId)
        {
            await RollbackRequirementSuccessorAsync(exiTransactions,
                loaded.ExiTransactions, exiRollbackId, sourcePath);
            return;
        }

        if (dialog.RequestedRequirementIntentId is Guid requirementIntentId)
        {
            ProjectRequirementHandoverPreviewService requirementPreviews =
                new ProjectRequirementHandoverPreviewService(_root.FullPath);
            ProjectRequirementHandoverPreview requirementPlan =
                await Task.Run(() => requirementPreviews.PrepareAsync(
                    sourcePath, requirementIntentId));
            if (!requirementPlan.CanPrepare ||
                requirementPlan.Intent.Method ==
                    Nodalis.Core.Projects.ProjectHandoverMethod.MoveOriginal)
            {
                MessageBox.Show(this, requirementPlan.FormatReview(),
                    requirementPlan.CanPrepare
                        ? "Mode A EXI · aperçu uniquement"
                        : "Exigence à requalifier avant passation",
                    MessageBoxButton.OK,
                    requirementPlan.CanPrepare
                        ? MessageBoxImage.Information : MessageBoxImage.Warning);
                return;
            }

            string details = requirementPlan.FormatReview() + "\n\n" +
                "EXÉCUTION RÉELLE DU MODE B : créer la fiche " +
                requirementPlan.ProposedReference + " avec son nouveau GUID " +
                requirementPlan.ProposedId.ToString("D") + ".\n" +
                "L'exigence source restera intacte. Un journal et un " +
                "snapshot permettront la reprise et le retrait explicites.\n\n" +
                "Créer maintenant ce successeur ?";
            if (MessageBox.Show(this, details,
                "Confirmer la création réelle du successeur EXI B",
                MessageBoxButton.YesNo, MessageBoxImage.Warning) !=
                MessageBoxResult.Yes)
            {
                return;
            }

            ProjectRequirementSuccessorJournal created = await Task.Run(() =>
                exiTransactions.ExecuteAsync(requirementPlan, approved: true));
            string targetProject = Path.GetFullPath(Path.Combine(
                _root.FullPath, created.TargetRelativePath[
                    ..created.TargetRelativePath.IndexOf(
                        "/Documentation/Exigences/",
                        StringComparison.Ordinal)].Replace('/',
                    Path.DirectorySeparatorChar)));
            await RefreshNavigationAsync(targetProject);
            StatusText.Text = "Successeur EXI " + created.TargetReference +
                " créé · transaction " + created.TransactionId.ToString("D") +
                " · original conservé, rollback explicite disponible.";
            return;
        }

        if (dialog.RequestedExecutionIntentId is Guid intentId)
        {
            await ExecuteProjectHandoverTaskAsync(
                transactions, sourcePath, intentId);
            return;
        }

        if (dialog.ApprovedRequest is not ProjectHandoverRequest choice)
        {
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

    /// <summary>Runs a separately approved restart of interrupted EXI B journals.</summary>
    /// <param name="transactions">Workspace-wide EXI B journal owner.</param>
    /// <param name="sourcePath">Current selected project for navigation refresh.</param>
    /// <returns>Human-approved recovery outcome.</returns>
    private async Task RecoverRequirementSuccessorsAsync(
        ProjectRequirementSuccessorTransactionService transactions,
        string sourcePath)
    {
        IReadOnlyList<ProjectRequirementSuccessorJournal> pending =
            await Task.Run(() => transactions.ListPendingAsync());
        if (pending.Count == 0)
        {
            StatusText.Text = "Aucune création EXI interrompue.";
            return;
        }

        string details = pending.Count + " opération(s) EXI interrompue(s).\n\n" +
            string.Join("\n", pending.Take(8).Select(item =>
                item.TransactionId.ToString("D") + " · " + item.Phase +
                " · " + item.TargetReference + " · " +
                item.TargetRelativePath)) + "\n\n" +
            "Reprendre les publications ou restaurations sans écraser " +
            "aucun document extérieur modifié ?";
        if (MessageBox.Show(this, details,
            "Confirmer la reprise de journaux EXI",
            MessageBoxButton.YesNo, MessageBoxImage.Warning) !=
            MessageBoxResult.Yes)
        {
            return;
        }

        int recovered = await Task.Run(() =>
            transactions.RecoverPendingAsync(approved: true));
        await RefreshNavigationAsync(sourcePath);
        StatusText.Text = recovered +
            " transaction(s) de succession EXI récupérée(s).";
    }

    /// <summary>Explicitly withdraws a published EXI B without editing original.</summary>
    /// <param name="transactions">Workspace-wide journal owner.</param>
    /// <param name="history">Frozen human-reviewed completed journals.</param>
    /// <param name="transactionId">Selected completed EXI B transaction.</param>
    /// <param name="sourcePath">Current selected project for refresh.</param>
    /// <returns>Explicitly approved withdrawal or safe refusal.</returns>
    private async Task RollbackRequirementSuccessorAsync(
        ProjectRequirementSuccessorTransactionService transactions,
        IReadOnlyList<ProjectRequirementSuccessorJournal> history,
        Guid transactionId, string sourcePath)
    {
        ProjectRequirementSuccessorJournal? item = history.SingleOrDefault(
            journal => journal.TransactionId == transactionId &&
                journal.Phase == "completed");
        if (item is null)
        {
            throw new InvalidDataException(
                "Transaction EXI absente ou non terminée.");
        }

        string details = "Retirer le successeur EXI " +
            item.TargetReference + " du destinataire ?\n\n" +
            item.TargetRelativePath + "\n\n" +
            "Le fichier publié sera déplacé vers la sauvegarde du journal. " +
            "L'original reste inchangé. Si quelqu'un a modifié le " +
            "successeur, aucun retrait n'aura lieu.";
        if (MessageBox.Show(this, details,
            "Confirmer le rollback du successeur EXI B",
            MessageBoxButton.YesNo, MessageBoxImage.Warning) !=
            MessageBoxResult.Yes)
        {
            return;
        }

        ProjectRequirementSuccessorJournal restored = await Task.Run(() =>
            transactions.RollbackAsync(transactionId, approved: true));
        await RefreshNavigationAsync(sourcePath);
        StatusText.Text = "Successeur EXI " + restored.TargetReference +
            " retiré du projet destinataire · sauvegarde conservée.";
    }

    /// <summary>Publishes an approved standalone task after a separate confirmation.</summary>
    /// <param name="transactions">Workspace transaction engine.</param>
    /// <param name="sourcePath">Original project root.</param>
    /// <param name="intentId">Selected pending human intention.</param>
    /// <returns>Completed or recoverable task publication.</returns>
    private async Task ExecuteProjectHandoverTaskAsync(
        ProjectHandoverTaskTransactionService transactions,
        string sourcePath, Guid intentId)
    {
        ProjectHandoverTaskExecutionPlanner planner =
            new ProjectHandoverTaskExecutionPlanner(_root.FullPath);
        ProjectHandoverTaskExecutionPlan plan = await Task.Run(() =>
            planner.PrepareAsync(sourcePath, intentId));
        if (!plan.CanExecute)
        {
            MessageBox.Show(this,
                "Passation non exécutable : " +
                string.Join("\n", plan.Warnings.Take(8)),
                "Liens ou sources à revoir", MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        string details = "Transfert RÉEL de " +
            plan.Intent.SourceReference + "\n" +
            "Mode : " + plan.Intent.Method + "\n" +
            "Source : " + plan.SourceDocumentRelativePath + "\n" +
            "Destination : " + plan.TargetDocumentRelativePath + "\n\n" +
            "Sauvegarde et journal de reprise conservés. " +
            "Deux fichiers Tâches.md seront réellement modifiés.\n\n" +
            string.Join("\n", plan.Warnings.Take(5)) + "\n\n" +
            "Confirmez-vous la publication de cette passation ?";
        if (MessageBox.Show(this, details, "Exécuter la passation de tâche",
            MessageBoxButton.YesNo, MessageBoxImage.Warning) !=
            MessageBoxResult.Yes)
        {
            return;
        }

        ProjectHandoverTaskJournal journal = await Task.Run(() =>
            transactions.ExecuteAsync(plan, approved: true));
        await RefreshNavigationAsync(sourcePath);
        StatusText.Text = "Passation exécutée · transaction " +
            journal.TransactionId.ToString("D") +
            " · snapshots conservés, restauration explicite disponible.";
    }

    /// <summary>Offers human-controlled recovery after interrupted publication.</summary>
    /// <param name="transactions">Durable journal owner.</param>
    /// <param name="sourcePath">Current Worktree project for refresh.</param>
    /// <returns>Explicit recovery result.</returns>
    private async Task RecoverProjectHandoverTasksAsync(
        ProjectHandoverTaskTransactionService transactions,
        string sourcePath)
    {
        IReadOnlyList<ProjectHandoverTaskJournal> pending = await Task.Run(() =>
            transactions.ListPendingAsync());
        if (pending.Count == 0)
        {
            StatusText.Text = "Aucune passation interrompue à reprendre.";
            return;
        }

        string details = pending.Count +
            " transaction(s) interrompue(s) dans le workspace.\n\n" +
            string.Join("\n", pending.Take(8).Select(item =>
                item.TransactionId.ToString("D") + " · " + item.Method +
                " · " + item.Phase + " · " +
                item.SourceProjectRelativePath + " vers " +
                item.TargetProjectRelativePath)) +
            "\n\nLa reprise compare chaque fichier et sa sauvegarde. " +
            "Toute modification inconnue bloque l'écriture. Reprendre ?";
        if (MessageBox.Show(this, details,
            "Confirmer la reprise des passations",
            MessageBoxButton.YesNo, MessageBoxImage.Warning) !=
            MessageBoxResult.Yes)
        {
            return;
        }

        int restored = await Task.Run(() =>
            transactions.RecoverPendingAsync(approved: true));
        await RefreshNavigationAsync(sourcePath);
        StatusText.Text = restored + " transaction(s) récupérée(s).";
    }

    /// <summary>Restores a completed task passation without silently discarding edits.</summary>
    /// <param name="transactions">Durable journal owner.</param>
    /// <param name="history">Frozen transaction selection from the dialog.</param>
    /// <param name="transactionId">Human-selected completed operation.</param>
    /// <param name="sourcePath">Original project for navigation refresh.</param>
    /// <returns>Explicitly approved two-file restoration.</returns>
    private async Task RollbackProjectHandoverTaskAsync(
        ProjectHandoverTaskTransactionService transactions,
        IReadOnlyList<ProjectHandoverTaskJournal> history,
        Guid transactionId, string sourcePath)
    {
        ProjectHandoverTaskJournal? selected = history.SingleOrDefault(item =>
            item.TransactionId == transactionId && item.Phase == "completed");
        if (selected is null)
        {
            throw new InvalidDataException(
                "Transaction non terminée ou différente de la sélection.");
        }

        string details = "Restaurer deux documents Tâches.md depuis leurs " +
            "sauvegardes d'avant transfert ?\n\n" +
            "Transaction : " + transactionId.ToString("D") + "\n" +
            "Source : " + selected.SourceDocumentRelativePath + "\n" +
            "Cible : " + selected.TargetDocumentRelativePath + "\n\n" +
            "Toute édition intervenue depuis la passation entraînera un refus, " +
            "sans écrasement. Cette action est journalisée.";
        if (MessageBox.Show(this, details,
            "Confirmer la restauration de passation",
            MessageBoxButton.YesNo, MessageBoxImage.Warning) !=
            MessageBoxResult.Yes)
        {
            return;
        }

        ProjectHandoverTaskJournal restored = await Task.Run(() =>
            transactions.RollbackAsync(transactionId, approved: true));
        await RefreshNavigationAsync(sourcePath);
        StatusText.Text = "Transaction " + restored.TransactionId.ToString("D") +
            " · deux documents restaurés depuis les snapshots.";
    }
}
