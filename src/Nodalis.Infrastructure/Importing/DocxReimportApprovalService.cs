using Nodalis.Core.Importing;

namespace Nodalis.Infrastructure.Importing;

/// <summary>
/// Coordinates an explicitly prepared DOCX reimport without trusting a stale
/// WPF comparison. Each approval is tied to one project, immutable historical
/// import, staged Word SHA-256 and fully resolved decision-plan fingerprint.
/// </summary>
public sealed class DocxReimportApprovalService
{
    private readonly DocxImportManifestStore _store;
    private readonly DocxReimportReviewService _reviews;
    private readonly DocxReimportProvenanceBuilder _provenance;
    private readonly DocxReimportTransactionService _transactions;

    /// <summary>Initializes local-only review and guarded reimport services.</summary>
    public DocxReimportApprovalService(string workspaceRoot)
    {
        _store = new DocxImportManifestStore(workspaceRoot);
        _reviews = new DocxReimportReviewService(workspaceRoot);
        _provenance = new DocxReimportProvenanceBuilder(workspaceRoot);
        _transactions = new DocxReimportTransactionService(workspaceRoot);
    }

    /// <summary>
    /// Reopens the actual immutable historical manifest, reads the current
    /// Nodalis files, resolves explicit section identities and creates a
    /// preview-bound approval. Never writes workspace files.
    /// </summary>
    public async Task<DocxReimportApproval> PrepareAsync(
        string projectDirectory,
        Guid previousImportId,
        DocxImportPreview latest,
        IReadOnlyCollection<DocxReimportSectionMatchOverride> identities,
        IReadOnlyCollection<DocxReimportResolution> decisions,
        IReadOnlyCollection<int> excludedNewSections,
        CancellationToken cancellationToken = default,
        IReadOnlyCollection<DocxReimportNewSectionAssignment>? newSectionAssignments = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectDirectory);
        ArgumentNullException.ThrowIfNull(latest);
        ArgumentNullException.ThrowIfNull(identities);
        ArgumentNullException.ThrowIfNull(decisions);
        ArgumentNullException.ThrowIfNull(excludedNewSections);

        string project = Path.GetFullPath(projectDirectory);
        string manifestPath = _store.GetManifestPath(project, previousImportId);
        DocxImportManifest previous = await _store.LoadAsync(
            manifestPath, cancellationToken);
        DocxReimportSectionMatchOverride[] identitiesCopy = identities.ToArray();
        DocxReimportResolution[] decisionsCopy = decisions.ToArray();
        int[] excludedCopy = excludedNewSections.ToArray();
        DocxReimportNewSectionAssignment[] newAssignments =
            newSectionAssignments?.ToArray() ?? [];
        DocxReimportReview review = await _reviews.BuildWithOverridesAsync(
            project, previous, latest, identitiesCopy, cancellationToken);
        DocxReimportDecisionPlan plan = DocxReimportDecisionPlanner.Build(
            review, previous, decisionsCopy, excludedCopy,
            latest, newAssignments);
        if (!plan.ReviewComplete)
        {
            string diagnostics = string.Join("; ", plan.Lines
                .Where(line => !line.Resolved)
                .Select(line => line.Diagnostic)
                .Distinct().Take(4));
            throw new InvalidDataException(
                "Tous les arbitrages doivent être terminés avant le réimport. " +
                diagnostics +
                (plan.UnmatchedNewSections.Count > 0
                    ? " Les nouvelles sections Word doivent être traitées explicitement." : "") +
                (plan.AmbiguousNewSections.Count > 0
                    ? " Des associations Word restent ambiguës." : "") +
                (plan.Warnings.Count > 0
                    ? " Avertissements de source : " + string.Join("; ", plan.Warnings) : ""));
        }

        foreach (DocxReimportProposedCreation created in plan.NewSectionCreations)
        {
            string target = _store.ResolveWorkspaceRelativePath(
                created.DestinationRelativePath);
            string inside = Path.GetRelativePath(project, target);
            if (Path.IsPathRooted(inside) || inside == ".." ||
                inside.StartsWith(".." + Path.DirectorySeparatorChar,
                    StringComparison.Ordinal) ||
                File.Exists(target) || !Directory.Exists(Path.GetDirectoryName(target)))
            {
                throw new InvalidDataException(
                    "La destination de nouvelle section Word n'est pas dans un dossier existant du projet.");
            }
        }

        if (review.Fragments.All(fragment =>
                fragment.Association.CurrentSectionIndex is null))
        {
            throw new InvalidDataException(
                "Aucune section Word associée : impossible de créer un nouvel historique.");
        }

        Guid operationId = Guid.NewGuid();
        string sourcePath = "Imports/Sources/reimport-" +
            operationId.ToString("N") + ".docx";
        DocxImportManifest next = await _provenance.BuildAsync(
            previous, review, plan, latest, decisionsCopy,
            operationId, sourcePath, cancellationToken);
        string stagedHash = await DocxImportManifestStore.ComputeFileSha256Async(
            latest.StagedImport.StagedCopyPath, cancellationToken);
        if (!string.Equals(next.SourceSha256, stagedHash,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                "La copie Word a changé pendant la préparation du réimport.");
        }

        return new DocxReimportApproval
        {
            ProjectDirectory = project,
            PreviousImportId = previous.ImportId,
            ProjectId = previous.ProjectId,
            OperationId = operationId,
            HistoricalManifestSha256 =
                await DocxImportManifestStore.ComputeFileSha256Async(
                    manifestPath, cancellationToken),
            StagedSourcePath = Path.GetFullPath(latest.StagedImport.StagedCopyPath),
            StagedSourceSha256 = stagedHash,
            ApprovedPlanFingerprint = DocxReimportTransactionService.Fingerprint(plan),
            PreparedManifest = next,
            Plan = plan,
            Identities = identitiesCopy,
            Decisions = decisionsCopy,
            ExcludedNewSections = excludedCopy,
            NewSectionAssignments = newAssignments
        };
    }

    /// <summary>
    /// Rebuilds every file-backed comparison after user approval. A modified
    /// source, changed historical record, different project, changed Markdown
    /// or altered decisions invalidates approval before any filesystem writes.
    /// </summary>
    public async Task<DocxReimportTransactionResult> ApplyAsync(
        DocxReimportApproval approved,
        DocxImportPreview latest,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(approved);
        ArgumentNullException.ThrowIfNull(latest);

        if (!string.Equals(DocxReimportTransactionService.Fingerprint(
                    approved.Plan), approved.ApprovedPlanFingerprint,
                StringComparison.OrdinalIgnoreCase) ||
            approved.PreparedManifest.ImportId != approved.OperationId ||
            approved.PreparedManifest.ProjectId != approved.ProjectId ||
            approved.PreparedManifest.PreviousImportId != approved.PreviousImportId ||
            !string.Equals(approved.PreparedManifest.ApprovedPlanFingerprint,
                approved.ApprovedPlanFingerprint, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                "Le plan DOCX approuvé est invalide ou a été modifié.");
        }

        string manifestPath = _store.GetManifestPath(
            approved.ProjectDirectory, approved.PreviousImportId);
        if (!string.Equals(await DocxImportManifestStore.ComputeFileSha256Async(
                manifestPath, cancellationToken),
            approved.HistoricalManifestSha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                "L'historique DOCX a changé depuis la validation : comparaison requise.");
        }

        // A completed transaction is replayed against the durable journal
        // and final destination SHA-256, never re-diffed against the old base.
        // Re-diffing would misclassify our own approved Word replacement as
        // a conflicting local edit, defeating idempotence.
        IReadOnlyList<DocxReimportPendingOperation> prior =
            await _transactions.InspectAsync(approved.ProjectDirectory,
                cancellationToken);
        if (prior.Any(operation =>
                operation.OperationId == approved.OperationId &&
                operation.State == DocxReimportTransactionState.Completed))
        {
            return await _transactions.CommitWithProvenanceAsync(
                approved.ProjectDirectory, approved.Plan,
                approved.OperationId, approved.ApprovedPlanFingerprint,
                new DocxReimportProvenancePackage
                {
                    Manifest = approved.PreparedManifest,
                    StagedDocxPath = approved.StagedSourcePath
                }, cancellationToken);
        }

        if (!string.Equals(Path.GetFullPath(latest.StagedImport.StagedCopyPath),
                approved.StagedSourcePath, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(
                await DocxImportManifestStore.ComputeFileSha256Async(
                    latest.StagedImport.StagedCopyPath, cancellationToken),
                approved.StagedSourceSha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                "Le Word a changé depuis la validation : préparez à nouveau le réimport.");
        }

        DocxImportManifest previous = await _store.LoadAsync(
            manifestPath, cancellationToken);
        if (previous.ProjectId != approved.ProjectId)
        {
            throw new InvalidDataException(
                "Le projet sélectionné ne correspond plus à l'approbation.");
        }

        DocxReimportReview fresh = await _reviews.BuildWithOverridesAsync(
            approved.ProjectDirectory, previous, latest, approved.Identities,
            cancellationToken);
        DocxReimportDecisionPlan plan = DocxReimportDecisionPlanner.Build(
            fresh, previous, approved.Decisions, approved.ExcludedNewSections,
            latest, approved.NewSectionAssignments);
        string fingerprint = DocxReimportTransactionService.Fingerprint(plan);
        if (!plan.ReviewComplete ||
            !string.Equals(fingerprint, approved.ApprovedPlanFingerprint,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                "Le projet a changé depuis l'approbation du plan DOCX. " +
                "Aucune écriture : refaites la comparaison et la confirmation.");
        }

        string sourcePath = "Imports/Sources/reimport-" +
            approved.OperationId.ToString("N") + ".docx";
        DocxImportManifest next = await _provenance.BuildAsync(
            previous, fresh, plan, latest, approved.Decisions,
            approved.OperationId, sourcePath, cancellationToken);
        if (next.ProjectId != approved.ProjectId ||
            !string.Equals(next.SourceSha256,
                approved.StagedSourceSha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                "Le nouveau manifeste DOCX ne correspond plus aux fichiers approuvés.");
        }

        if (next.Fragments.Count != approved.PreparedManifest.Fragments.Count ||
            next.Fragments.Any(current =>
                !approved.PreparedManifest.Fragments.Any(original =>
                    original.FragmentId == current.FragmentId &&
                    string.Equals(original.FragmentSha256,
                        current.FragmentSha256, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(original.DestinationSha256,
                        current.DestinationSha256, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(original.LatestWordSnapshotSha256,
                        current.LatestWordSnapshotSha256, StringComparison.OrdinalIgnoreCase))))
        {
            throw new InvalidDataException(
                "La provenance calculée diffère de celle du plan approuvé.");
        }

        return await _transactions.CommitWithProvenanceAsync(
            approved.ProjectDirectory, plan, approved.OperationId,
            approved.ApprovedPlanFingerprint,
            new DocxReimportProvenancePackage
            {
                Manifest = approved.PreparedManifest,
                StagedDocxPath = latest.StagedImport.StagedCopyPath
            }, cancellationToken);
    }
}

/// <summary>
/// One session-scoped reviewed approval. Do not persist; after reloading the
/// workspace the user must explicitly prepare and approve a new transaction.
/// </summary>
public sealed record DocxReimportApproval
{
    public required string ProjectDirectory { get; init; }

    public required Guid ProjectId { get; init; }

    public required Guid PreviousImportId { get; init; }

    public required Guid OperationId { get; init; }

    public required string HistoricalManifestSha256 { get; init; }

    public required string StagedSourcePath { get; init; }

    public required string StagedSourceSha256 { get; init; }

    public required string ApprovedPlanFingerprint { get; init; }

    /// <summary>Exactly the immutable provenance proposed at approval time.</summary>
    public required DocxImportManifest PreparedManifest { get; init; }

    public required DocxReimportDecisionPlan Plan { get; init; }

    public required DocxReimportSectionMatchOverride[] Identities { get; init; }

    public required DocxReimportResolution[] Decisions { get; init; }

    public required int[] ExcludedNewSections { get; init; }

    /// <summary>Approved explicit placements for newly introduced Word sections.</summary>
    public DocxReimportNewSectionAssignment[] NewSectionAssignments { get; init; } = [];
}
