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
        ProjectRequirementMoveTransactionService moveTransactions =
            new ProjectRequirementMoveTransactionService(_root.FullPath);
        ProjectRiskSuccessorTransactionService riskTransactions =
            new ProjectRiskSuccessorTransactionService(_root.FullPath);
        ProjectRiskMoveTransactionService riskMoves =
            new ProjectRiskMoveTransactionService(_root.FullPath);
        (ProjectClosureAuditSnapshot? Audit,
            ProjectHandoverIntentSnapshot History,
            IReadOnlyList<ProjectHandoverTaskJournal> Transactions,
            IReadOnlyList<ProjectRequirementSuccessorJournal> ExiTransactions,
            IReadOnlyList<ProjectRequirementMoveJournal> MoveTransactions,
            IReadOnlyList<ProjectRiskSuccessorJournal> RiskTransactions,
            IReadOnlyList<ProjectRiskMoveJournal> RiskMoves)
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
                IReadOnlyList<ProjectRequirementMoveJournal> moveHistory =
                    await moveTransactions.ListHistoryAsync();
                IReadOnlyList<ProjectRiskSuccessorJournal> riskHistory =
                    await riskTransactions.ListHistoryAsync();
                IReadOnlyList<ProjectRiskMoveJournal> riskMoveHistory =
                    await riskMoves.ListHistoryAsync();
                return (audit, history, transactionHistory, exiHistory,
                    moveHistory, riskHistory, riskMoveHistory);
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
            loaded.Transactions, loaded.ExiTransactions,
            loaded.MoveTransactions, loaded.RiskTransactions,
            loaded.RiskMoves, readOnly)
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

        if (dialog.RequestedMoveRecovery)
        {
            await RecoverRequirementMovesAsync(moveTransactions, sourcePath);
            return;
        }

        if (dialog.RequestedMoveRollbackId is Guid moveRollbackId)
        {
            await RollbackRequirementMoveAsync(moveTransactions,
                loaded.MoveTransactions, moveRollbackId, sourcePath);
            return;
        }

        if (dialog.RequestedRiskRecovery)
        {
            await RecoverRiskSuccessorsAsync(riskTransactions, sourcePath);
            return;
        }

        if (dialog.RequestedRiskRollbackId is Guid riskRollbackId)
        {
            await RollbackRiskSuccessorAsync(riskTransactions,
                loaded.RiskTransactions, riskRollbackId, sourcePath);
            return;
        }

        if (dialog.RequestedRiskMoveRecovery)
        {
            await RecoverRiskMovesAsync(riskMoves);
            return;
        }

        if (dialog.RequestedRiskMoveRollbackId is Guid riskMoveRollbackId)
        {
            await RollbackRiskMoveAsync(riskMoves,
                loaded.RiskMoves, riskMoveRollbackId);
            return;
        }

        if (dialog.RequestedRiskIntentId is Guid riskIntentId)
        {
            ProjectRiskHandoverPreviewService riskPreviews =
                new ProjectRiskHandoverPreviewService(_root.FullPath);
            ProjectRiskHandoverPreview riskPlan = await Task.Run(() =>
                riskPreviews.PrepareAsync(sourcePath, riskIntentId));
            if (riskPlan.Intent.Method ==
                Nodalis.Core.Projects.ProjectHandoverMethod.MoveOriginal)
            {
                if (!riskPlan.CanPreview || riskPlan.MoveMarkdown is null)
                {
                    MessageBox.Show(this, riskPlan.FormatReview(),
                        "Déplacement RIS A non préparable",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                string riskMoveDetails = riskPlan.FormatReview() + "\n\n" +
                    "DÉPLACEMENT RÉEL A : le GUID, les évaluations et " +
                    "l'historique du risque seront conservés. Le code RIS " +
                    "et le projet propriétaire changeront. L'original sera " +
                    "mis en sûreté avant la publication de la cible.\n" +
                    "Tout backlink non requalifié interdit le déplacement, " +
                    "qui possède un journal et un rollback explicite.\n\n" +
                    "Exécuter le déplacement de ce risque ?";
                if (MessageBox.Show(this, riskMoveDetails,
                    "Confirmer le déplacement réel RIS A",
                    MessageBoxButton.YesNo, MessageBoxImage.Warning) !=
                    MessageBoxResult.Yes)
                {
                    return;
                }

                ProjectRiskMoveJournal moved = await Task.Run(() =>
                    riskMoves.ExecuteAsync(riskPlan, approved: true));
                string recipient = moved.TargetRelativePath[
                    ..moved.TargetRelativePath.IndexOf(
                        "/Analyse/Risques/", StringComparison.Ordinal)];
                await RefreshNavigationAsync(sourcePath);
                await RefreshNavigationAsync(Path.GetFullPath(Path.Combine(
                    _root.FullPath, recipient.Replace('/',
                        Path.DirectorySeparatorChar))));
                StatusText.Text = "Risque RIS " + moved.SourceReference +
                    " déplacé vers " + moved.TargetReference +
                    " · GUID et historiques conservés · rollback disponible.";
                return;
            }

            if (!riskPlan.CanPreview || riskPlan.SuccessorMarkdown is null)
            {
                MessageBox.Show(this, riskPlan.FormatReview(),
                    "Passation RIS à corriger", MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            string details = riskPlan.FormatReview() + "\n\n" +
                "CRÉATION RÉELLE DU MODE B : un nouveau RIS sera publié " +
                "dans le projet destinataire, avec un GUID distinct. " +
                "L'original et ses preuves resteront intacts. " +
                "Ni cotation, ni traitement, ni approbation n'est repris.\n" +
                "Un journal privé permet la reprise après interruption " +
                "et le rollback contrôlé.\n\n" +
                "Créer maintenant ce successeur non évalué ?";
            if (MessageBox.Show(this, details,
                "Confirmer la création RIS B",
                MessageBoxButton.YesNo, MessageBoxImage.Warning) !=
                MessageBoxResult.Yes)
            {
                return;
            }

            ProjectRiskSuccessorJournal created = await Task.Run(() =>
                riskTransactions.ExecuteAsync(riskPlan, approved: true));
            string targetProject = Path.GetFullPath(Path.Combine(
                _root.FullPath, created.TargetRelativePath[
                    ..created.TargetRelativePath.IndexOf(
                        "/Analyse/Risques/",
                        StringComparison.Ordinal)].Replace('/',
                    Path.DirectorySeparatorChar)));
            await RefreshNavigationAsync(targetProject);
            StatusText.Text = "Successeur RIS " + created.TargetReference +
                " créé, sans résultat hérité · rollback explicite disponible.";
            return;
        }

        if (dialog.RequestedRequirementIntentId is Guid requirementIntentId)
        {
            ProjectRequirementHandoverPreviewService requirementPreviews =
                new ProjectRequirementHandoverPreviewService(_root.FullPath);
            ProjectRequirementHandoverPreview requirementPlan =
                await Task.Run(() => requirementPreviews.PrepareAsync(
                    sourcePath, requirementIntentId));
            // Refresh the shared index only on an explicit EXI passation
            // request. All source, REC and relation reads run off Dispatcher;
            // this is a review, not an authorization to change any links.
            ProjectRequirementRelationReview relationReview =
                await Task.Run(async () =>
                {
                    ProjectRequirementCatalog requirements =
                        await new ProjectRequirementRegistryService(
                            _root.FullPath).ListAsync(sourcePath);
                    Nodalis.Core.Links.LinkIndexCatalog index =
                        await _linkIndexService.RefreshAsync();
                    ProjectTestCampaignCatalog campaigns =
                        await new ProjectTestCampaignService(_root.FullPath)
                            .ListAsync(sourcePath);
                    return ProjectRequirementHandoverRelationReview.Build(
                        requirementPlan, requirements, index, campaigns);
                });
            ProjectRequirementHandoverRelationsDialog linkDialog = new(
                relationReview)
            {
                Owner = this
            };
            if (linkDialog.ShowDialog() != true)
            {
                return;
            }

            if (!requirementPlan.CanPrepare)
            {
                MessageBox.Show(this, requirementPlan.FormatReview(),
                    "Passation EXI bloquée · relations à requalifier",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (requirementPlan.Intent.Method ==
                Nodalis.Core.Projects.ProjectHandoverMethod.MoveOriginal)
            {
                IReadOnlyList<Guid> approvedCriterionIds = [];
                string criterionRationale = string.Empty;
                if (requirementPlan.CriteriaForReview.Count > 0)
                {
                    ProjectRequirementCriteriaReviewDialog criterionReview = new(
                        requirementPlan.CriteriaForReview)
                    {
                        Owner = this
                    };
                    if (criterionReview.ShowDialog() != true)
                    {
                        return;
                    }

                    approvedCriterionIds =
                        criterionReview.ApprovedCriteriaIds;
                    criterionRationale = criterionReview.Rationale;
                }

                string moveDetails = requirementPlan.FormatReview() +
                    "\n\nDÉPLACEMENT RÉEL A : le GUID d'origine est conservé. " +
                    "La référence lisible changera pour " +
                    requirementPlan.ProposedReference + ".\n" +
                    "Le fichier original est d'abord mis en sûreté dans le " +
                    "journal. Les liens GUID et EXI-xxxx non résolus " +
                    "empêchent la transaction.\n\nExécuter ce déplacement ?";
                if (MessageBox.Show(this, moveDetails,
                    "Confirmer le déplacement réel EXI A",
                    MessageBoxButton.YesNo, MessageBoxImage.Warning) !=
                    MessageBoxResult.Yes)
                {
                    return;
                }

                ProjectRequirementMoveJournal moved = await Task.Run(() =>
                    moveTransactions.ExecuteAsync(
                        requirementPlan, approved: true,
                        reviewedCriteriaIds: approvedCriterionIds,
                        criteriaReviewRationale: criterionRationale));
                string destinationProject = Path.GetFullPath(Path.Combine(
                    _root.FullPath, moved.TargetRelativePath[
                        ..moved.TargetRelativePath.IndexOf(
                            "/Documentation/Exigences/",
                            StringComparison.Ordinal)].Replace('/',
                        Path.DirectorySeparatorChar)));
                await RefreshNavigationAsync(sourcePath);
                await RefreshNavigationAsync(destinationProject);
                StatusText.Text = "Exigence EXI " + moved.SourceReference +
                    " déplacée vers " + moved.TargetReference +
                    " · GUID conservé · rollback explicite disponible.";
                return;
            }

            IReadOnlyList<Guid> reviewedSourceCriteria = [];
            string successorRationale = string.Empty;
            if (requirementPlan.ProposedCriterionSuccession.Count > 0)
            {
                ProjectRequirementCriteriaReviewDialog review = new(
                    requirementPlan.ProposedCriterionSuccession)
                {
                    Owner = this
                };
                if (review.ShowDialog() != true)
                {
                    return;
                }

                reviewedSourceCriteria = review.ApprovedCriteriaIds;
                successorRationale = review.Rationale;
            }

            string details = requirementPlan.FormatReview() + "\n\n" +
                "EXÉCUTION RÉELLE DU MODE B : créer la fiche " +
                requirementPlan.ProposedReference + " avec son nouveau GUID " +
                requirementPlan.ProposedId.ToString("D") + ".\n" +
                "L'exigence source restera intacte. Pour les CA, seuls " +
                "les références et intitulés seront repris avec de NOUVEAUX " +
                "GUID : AUCUN résultat, preuve ou verdict n'est copié. " +
                "Un journal permettra la reprise et le retrait explicites.\n\n" +
                "Créer maintenant ce successeur non vérifié ?";
            if (MessageBox.Show(this, details,
                "Confirmer la création réelle du successeur EXI B",
                MessageBoxButton.YesNo, MessageBoxImage.Warning) !=
                MessageBoxResult.Yes)
            {
                return;
            }

            ProjectRequirementSuccessorJournal created = await Task.Run(() =>
                exiTransactions.ExecuteAsync(requirementPlan, approved: true,
                    approvedCriteriaIds: reviewedSourceCriteria,
                    criteriaReviewRationale: successorRationale));
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

    /// <summary>Explicit restart of unfinished RIS B journal operations.</summary>
    /// <param name="transactions">Workspace RIS B transaction engine.</param>
    /// <param name="sourcePath">Current selected project for UI refresh.</param>
    /// <returns>Approved replay count or an explicit refusal.</returns>
    private async Task RecoverRiskSuccessorsAsync(
        ProjectRiskSuccessorTransactionService transactions, string sourcePath)
    {
        IReadOnlyList<ProjectRiskSuccessorJournal> pending =
            await Task.Run(() => transactions.ListPendingAsync());
        if (pending.Count == 0)
        {
            StatusText.Text = "Aucune succession RIS interrompue.";
            return;
        }

        string details = pending.Count + " transaction(s) RIS inachevée(s).\n" +
            string.Join("\n", pending.Take(8).Select(item =>
                item.TargetReference + " · " + item.Phase + " · " +
                item.TransactionId.ToString("D"))) +
            "\n\nReprendre uniquement les snapshots intacts, " +
            "sans écrasement ni validation métier ?";
        if (MessageBox.Show(this, details,
            "Confirmer la reprise RIS B",
            MessageBoxButton.YesNo, MessageBoxImage.Warning) !=
            MessageBoxResult.Yes)
        {
            return;
        }

        int count = await Task.Run(() =>
            transactions.RecoverPendingAsync(approved: true));
        // The operation may belong to a different application. Refresh each
        // actual recipient, not only the currently selected source project.
        foreach (string recipient in pending.Select(item =>
            item.TargetRelativePath[
                ..item.TargetRelativePath.IndexOf("/Analyse/Risques/",
                    StringComparison.Ordinal)])
            .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            await RefreshNavigationAsync(Path.GetFullPath(Path.Combine(
                _root.FullPath, recipient.Replace('/',
                    Path.DirectorySeparatorChar))));
        }

        StatusText.Text = count + " succession(s) RIS récupérée(s).";
    }

    /// <summary>Withdraws exactly one human-approved unmodified RIS B.</summary>
    /// <param name="transactions">Workspace RIS B transaction engine.</param>
    /// <param name="history">Previously shown, project-scoped journals.</param>
    /// <param name="transactionId">Human-selected completed B.</param>
    /// <param name="sourcePath">Selected project for UI refresh.</param>
    /// <returns>Journaled safe withdrawal, or an explicit refusal.</returns>
    private async Task RollbackRiskSuccessorAsync(
        ProjectRiskSuccessorTransactionService transactions,
        IReadOnlyList<ProjectRiskSuccessorJournal> history,
        Guid transactionId, string sourcePath)
    {
        ProjectRiskSuccessorJournal? item = history.SingleOrDefault(
            journal => journal.TransactionId == transactionId &&
                journal.Phase == "completed");
        if (item is null)
        {
            throw new InvalidDataException(
                "Succession RIS absente ou non terminée.");
        }

        string details = "Retirer le successeur RIS " +
            item.TargetReference + " ?\n\n" + item.TargetRelativePath +
            "\n\nLe fichier exact sera conservé dans le journal. " +
            "Toute édition ou référence entrante bloque cette action. " +
            "L'original reste intact.";
        if (MessageBox.Show(this, details,
            "Confirmer le rollback RIS B",
            MessageBoxButton.YesNo, MessageBoxImage.Warning) !=
            MessageBoxResult.Yes)
        {
            return;
        }

        ProjectRiskSuccessorJournal rolledBack = await Task.Run(() =>
            transactions.RollbackAsync(transactionId, approved: true));
        string recipient = rolledBack.TargetRelativePath[
            ..rolledBack.TargetRelativePath.IndexOf(
                "/Analyse/Risques/", StringComparison.Ordinal)];
        await RefreshNavigationAsync(Path.GetFullPath(Path.Combine(
            _root.FullPath, recipient.Replace('/',
                Path.DirectorySeparatorChar))));
        StatusText.Text = "Successeur RIS " + rolledBack.TargetReference +
            " retiré · sauvegarde conservée dans le journal.";
    }

    /// <summary>Recovers interrupted RIS A with human reconfirmation.</summary>
    /// <param name="transactions">Workspace-wide RIS A journal engine.</param>
    /// <returns>Safe replay and refresh of actual source/target owners.</returns>
    private async Task RecoverRiskMovesAsync(
        ProjectRiskMoveTransactionService transactions)
    {
        IReadOnlyList<ProjectRiskMoveJournal> pending = await Task.Run(() =>
            transactions.ListPendingAsync());
        if (pending.Count == 0)
        {
            StatusText.Text = "Aucun déplacement RIS A interrompu.";
            return;
        }

        string details = pending.Count + " déplacement(s) RIS A " +
            "interrompu(s).\n" +
            string.Join("\n", pending.Take(8).Select(item =>
                item.SourceReference + " → " + item.TargetReference +
                " · " + item.Phase)) +
            "\n\nReprendre les snapshots exacts sans jamais créer " +
            "deux propriétaires du même GUID ?";
        if (MessageBox.Show(this, details,
            "Confirmer la reprise RIS A",
            MessageBoxButton.YesNo, MessageBoxImage.Warning) !=
            MessageBoxResult.Yes)
        {
            return;
        }

        int recovered = await Task.Run(() =>
            transactions.RecoverPendingAsync(approved: true));
        await RefreshRiskMoveOwnersAsync(pending);
        StatusText.Text = recovered + " déplacement(s) RIS A récupéré(s).";
    }

    /// <summary>Restores the exact original RIS A only after human approval.</summary>
    /// <param name="transactions">Workspace RIS A journal engine.</param>
    /// <param name="history">Previously displayed completed transactions.</param>
    /// <param name="transactionId">Selected completed A journal.</param>
    /// <returns>Journaled original restoration, or safe refusal.</returns>
    private async Task RollbackRiskMoveAsync(
        ProjectRiskMoveTransactionService transactions,
        IReadOnlyList<ProjectRiskMoveJournal> history, Guid transactionId)
    {
        ProjectRiskMoveJournal? item = history.SingleOrDefault(entry =>
            entry.TransactionId == transactionId &&
            entry.Phase == "completed");
        if (item is null)
        {
            throw new InvalidDataException(
                "Aucun déplacement RIS A terminé pour cette transaction.");
        }

        string details = "Restaurer le risque RIS " +
            item.SourceReference + " au projet source ?\n\n" +
            item.SourceRelativePath + "\n" + item.TargetRelativePath +
            "\n\nLe GUID et tous les historiques seront restaurés " +
            "à l'octet près. Toute édition ou référence entrante bloque " +
            "la restauration. La fiche retirée restera dans le journal.";
        if (MessageBox.Show(this, details,
            "Confirmer le rollback RIS A",
            MessageBoxButton.YesNo, MessageBoxImage.Warning) !=
            MessageBoxResult.Yes)
        {
            return;
        }

        ProjectRiskMoveJournal restored = await Task.Run(() =>
            transactions.RollbackAsync(transactionId, approved: true));
        await RefreshRiskMoveOwnersAsync([restored]);
        StatusText.Text = "Risque RIS " + restored.SourceReference +
            " restauré à l'identique dans son projet d'origine.";
    }

    /// <summary>Refreshes only exact RIS A projects after a human mutation.</summary>
    /// <param name="journals">Approved completed or resumed journals.</param>
    /// <returns>Updated Worktree for source and recipient.</returns>
    private async Task RefreshRiskMoveOwnersAsync(
        IReadOnlyList<ProjectRiskMoveJournal> journals)
    {
        foreach (string projectRelative in journals.SelectMany(journal =>
            new[] { journal.SourceRelativePath, journal.TargetRelativePath })
            .Select(relative => relative[
                ..relative.IndexOf("/Analyse/Risques/", StringComparison.Ordinal)])
            .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            await RefreshNavigationAsync(Path.GetFullPath(Path.Combine(
                _root.FullPath, projectRelative.Replace('/',
                    Path.DirectorySeparatorChar))));
        }
    }

    /// <summary>Recovers EXI A parking/publication under distinct approval.</summary>
    /// <param name="transactions">Workspace EXI A journal owner.</param>
    /// <param name="sourcePath">Current source project.</param>
    /// <returns>Completed, explicit transaction recovery.</returns>
    private async Task RecoverRequirementMovesAsync(
        ProjectRequirementMoveTransactionService transactions,
        string sourcePath)
    {
        IReadOnlyList<ProjectRequirementMoveJournal> pending =
            await Task.Run(() => transactions.ListPendingAsync());
        if (pending.Count == 0)
        {
            StatusText.Text = "Aucun déplacement EXI A interrompu.";
            return;
        }

        string details = pending.Count + " déplacement(s) EXI A " +
            "interrompu(s) :\n" +
            string.Join("\n", pending.Take(8).Select(item =>
                item.SourceReference + " vers " + item.TargetReference +
                " · " + item.Phase + " · " +
                item.TransactionId.ToString("D"))) +
            "\n\nReprendre uniquement les snapshots intacts ?";
        if (MessageBox.Show(this, details,
            "Confirmer la reprise des EXI A",
            MessageBoxButton.YesNo, MessageBoxImage.Warning) !=
            MessageBoxResult.Yes)
        {
            return;
        }

        int recovered = await Task.Run(() =>
            transactions.RecoverPendingAsync(approved: true));
        await RefreshNavigationAsync(sourcePath);
        StatusText.Text = recovered +
            " déplacement(s) EXI A récupéré(s).";
    }

    /// <summary>Restores the original EXI owner after human confirmation.</summary>
    /// <param name="transactions">Workspace A transaction owner.</param>
    /// <param name="history">Frozen journal history shown in the UI.</param>
    /// <param name="transactionId">Selected completed A transaction.</param>
    /// <param name="sourcePath">Current source project.</param>
    /// <returns>Byte-exact restoration or explicit safe refusal.</returns>
    private async Task RollbackRequirementMoveAsync(
        ProjectRequirementMoveTransactionService transactions,
        IReadOnlyList<ProjectRequirementMoveJournal> history,
        Guid transactionId, string sourcePath)
    {
        ProjectRequirementMoveJournal? selected = history.SingleOrDefault(
            item => item.TransactionId == transactionId &&
                item.Phase == "completed");
        if (selected is null)
        {
            throw new InvalidDataException(
                "Aucun déplacement EXI A terminé avec cet identifiant.");
        }

        string details = "Restaurer EXI " + selected.SourceReference +
            " avec son GUID original dans son projet d'origine ?\n\n" +
            selected.SourceRelativePath + "\n" +
            selected.TargetRelativePath + "\n\n" +
            "La fiche publiée sera retirée et conservée dans le journal. " +
            "Une édition humaine ou une référence entrante bloque la restauration.";
        if (MessageBox.Show(this, details,
            "Confirmer la restauration EXI A",
            MessageBoxButton.YesNo, MessageBoxImage.Warning) !=
            MessageBoxResult.Yes)
        {
            return;
        }

        ProjectRequirementMoveJournal restored = await Task.Run(() =>
            transactions.RollbackAsync(transactionId, approved: true));
        await RefreshNavigationAsync(sourcePath);
        StatusText.Text = "EXI " + restored.SourceReference +
            " restaurée exactement dans son projet d'origine.";
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
