using System.Security.Cryptography;
using System.Text;
using Nodalis.Core.Domain;
using Nodalis.Core.Projects;
using Nodalis.Core.Risks;

namespace Nodalis.Infrastructure.Projects;

/// <summary>
/// Non-executing A/B review of a previously recorded RIS handover intention.
/// The source risk, its reviews and its treatment history are never modified.
/// </summary>
public sealed record ProjectRiskHandoverPreview
{
    /// <summary>Exact existing and independently rechecked human intention.</summary>
    public required ProjectHandoverIntent Intent { get; init; }

    /// <summary>Existing risk Markdown path, pinned to its actual byte hash.</summary>
    public required string SourceRelativePath { get; init; }

    /// <summary>SHA-256 of the exact original risk file.</summary>
    public required string SourceSha256 { get; init; }

    /// <summary>Candidate destination path, never reserved by this preview.</summary>
    public string? TargetRelativePath { get; init; }

    /// <summary>Candidate new RIS reference, not an allocation.</summary>
    public string? TargetReference { get; init; }

    /// <summary>Stable target GUID from the reviewed intention.</summary>
    public Guid TargetId { get; init; }

    /// <summary>Source state retained only as historical context.</summary>
    public string OriginalState { get; init; } = string.Empty;

    /// <summary>Fresh B draft with no copied results, proofs, or histories.</summary>
    public string? SuccessorMarkdown { get; init; }

    /// <summary>Reasons this snapshot cannot be trusted as a preview.</summary>
    public IReadOnlyList<string> Blockers { get; init; } = [];

    /// <summary>Manual decisions required before any future execution.</summary>
    public IReadOnlyList<string> Warnings { get; init; } = [];

    /// <summary>True means previewable, NEVER executable.</summary>
    public bool CanPreview => Blockers.Count == 0;

    /// <summary>Formats a read-only human comparison without an execute action.</summary>
    /// <returns>Bounded review text; no original risk contents copied.</returns>
    public string FormatReview()
    {
        StringBuilder text = new();
        text.AppendLine("Passation RIS — APERÇU UNIQUEMENT, AUCUNE ÉCRITURE")
            .Append("Source : ").AppendLine(SourceRelativePath)
            .Append("SHA-256 : ").AppendLine(SourceSha256)
            .Append("État original : ").AppendLine(OriginalState)
            .Append("Destination proposée : ").AppendLine(
                TargetRelativePath ?? "(indisponible)")
            .Append("Référence proposée : ").AppendLine(
                TargetReference ?? "(indisponible)")
            .Append("GUID envisagé : ").AppendLine(TargetId.ToString("D"))
            .Append("Mode : ").AppendLine(Intent.Method ==
                ProjectHandoverMethod.MoveOriginal
                    ? "A · conserver le GUID original"
                    : "B · nouveau GUID, évaluation non reprise")
            .AppendLine();

        foreach (string problem in Blockers.Take(10))
        {
            text.Append("BLOCAGE : ").AppendLine(problem);
        }

        foreach (string warning in Warnings.Take(10))
        {
            text.Append("À requalifier : ").AppendLine(warning);
        }

        text.AppendLine()
            .AppendLine("Aucun transfert RIS n'est encore implémenté. " +
                "Cette fenêtre ne modifie aucune source, n'approuve " +
                "aucune évaluation et ne déclenche aucune transaction.");
        return text.ToString();
    }
}

/// <summary>
/// Only a draft for a B risk successor: same title, new owner and identity,
/// statuses reset, all human assessment/treatment history intentionally absent.
/// </summary>
public static class ProjectRiskHandoverDraft
{
    /// <summary>Builds a clean, deterministic risk document in memory.</summary>
    /// <param name="source">Verified original risk metadata.</param>
    /// <param name="targetProjectId">Distinct, active receiving project.</param>
    /// <param name="targetReference">Unreserved destination RIS reference.</param>
    /// <param name="successorId">Distinct stable GUID from handover intention.</param>
    /// <param name="recordedUtc">Timestamp of the original human intention.</param>
    /// <returns>Fresh Markdown with no copied evidence or ratings.</returns>
    public static string Prepare(
        ProjectRiskCard source, Guid targetProjectId,
        string targetReference, Guid successorId, DateTimeOffset recordedUtc)
    {
        ProjectRiskMarkdownCodec.Validate(source);
        if (targetProjectId == Guid.Empty ||
            targetProjectId == source.ProjectId ||
            successorId == Guid.Empty || successorId == source.Id ||
            !ProjectRiskMarkdownCodec.IsReference(targetReference) ||
            recordedUtc == default)
        {
            throw new InvalidDataException(
                "Succession RIS : identité, projet ou référence invalide.");
        }

        ProjectRiskCard successor = new()
        {
            Id = successorId,
            ProjectId = targetProjectId,
            Reference = targetReference,
            Title = source.Title,
            CreatedUtc = recordedUtc.ToUniversalTime(),
            Qualification = ProjectRiskQualification.ToReview,
            Lifecycle = ProjectRiskLifecycle.Open,
            Initial = new ProjectRiskRating(null, null),
            Residual = new ProjectRiskRating(null, null),
            TreatmentHeadId = null
        };
        return ProjectRiskMarkdownCodec.Format(successor);
    }
}

/// <summary>
/// Workspace-scoped, opt-in RIS preparation rechecking both active projects,
/// preflight inventories and byte-exact source SHA without any disk writes.
/// </summary>
public sealed class ProjectRiskHandoverPreviewService
{
    private const int MaxRiskBytes = 1024 * 1024;
    private readonly string _root;
    private readonly ProjectJourneySourceService _sources;
    private readonly ProjectHandoverPreflightService _preflight;
    private readonly ProjectHandoverIntentService _intents;

    /// <summary>Creates a workspace-scoped preview reader.</summary>
    /// <param name="workspaceRoot">Existing single workspace.</param>
    public ProjectRiskHandoverPreviewService(string workspaceRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRoot);
        _root = Path.TrimEndingDirectorySeparator(
            Path.GetFullPath(workspaceRoot));
        _sources = new ProjectJourneySourceService(_root);
        _preflight = new ProjectHandoverPreflightService(_root);
        _intents = new ProjectHandoverIntentService(_root);
    }

    /// <summary>
    /// Reads and rechecks one recorded RIS intention. The B markdown is only
    /// an in-memory candidate; A cannot relocate any risk at this stage.
    /// </summary>
    /// <param name="sourceProjectDirectory">Exact original active owner.</param>
    /// <param name="intentId">Previously approved RIS intention GUID.</param>
    /// <param name="cancellationToken">Read cancellation.</param>
    /// <returns>Read-only, SHA-pinned proposal and explicit blockers.</returns>
    public async Task<ProjectRiskHandoverPreview> PrepareAsync(
        string sourceProjectDirectory, Guid intentId,
        CancellationToken cancellationToken = default)
    {
        if (intentId == Guid.Empty)
        {
            throw new InvalidDataException("Intention RIS non identifiée.");
        }

        (string source, string sourceRelative, ProjectManifest manifest) =
            await _preflight.ResolveActiveProjectAsync(
                sourceProjectDirectory, cancellationToken);
        ProjectHandoverIntentSnapshot snapshot =
            await _intents.ReadAsync(source, cancellationToken);
        ProjectHandoverIntent intent = snapshot.Ledger.Intents.SingleOrDefault(
            item => item.Id == intentId) ??
            throw new InvalidDataException("Intention RIS introuvable.");
        if (intent.SourceObjectKind != "Risques" ||
            intent.SourceProjectId != manifest.Id ||
            !intent.SourceRelativePath.StartsWith(
                sourceRelative + "/Analyse/Risques/",
                StringComparison.Ordinal) ||
            !intent.SourceRelativePath.EndsWith(
                "/" + intent.SourceReference + ".md",
                StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                "La passation RIS exige une fiche canonique du projet source.");
        }

        string targetDirectory = Path.GetFullPath(Path.Combine(_root,
            intent.TargetProjectRelativePath.Replace('/',
                Path.DirectorySeparatorChar)));
        ProjectHandoverPreview original = await _preflight.PrepareAsync(
            source, targetDirectory, intent.SourceObjectId, intent.Method,
            intent.ProposedTargetObjectId, cancellationToken);
        if (!original.CanRecord ||
            original.SourceProjectId != intent.SourceProjectId ||
            original.TargetProjectId != intent.TargetProjectId ||
            original.SourceObjectId != intent.SourceObjectId ||
            original.SourceObjectKind != intent.SourceObjectKind ||
            original.SourceReference != intent.SourceReference ||
            original.SourceRelativePath != intent.SourceRelativePath ||
            original.TargetProjectRelativePath !=
                intent.TargetProjectRelativePath ||
            original.SourceFingerprint != intent.SourceFingerprint ||
            original.TargetFingerprint != intent.TargetFingerprint ||
            original.ProposedTargetObjectId != intent.ProposedTargetObjectId)
        {
            throw new InvalidDataException(
                "L'intention RIS ou un inventaire a changé : nouvelle revue.");
        }

        ProjectRiskRegistryService risks = new(_root);
        ProjectRiskCatalog sourceCatalog = await risks.ListAsync(
            source, cancellationToken);
        ProjectRiskCatalog targetCatalog = await risks.ListAsync(
            targetDirectory, cancellationToken);
        ProjectRiskEntry entry = sourceCatalog.Risks.SingleOrDefault(item =>
            item.Card.Id == intent.SourceObjectId) ??
            throw new InvalidDataException(
                "Risque source absent ou identité ambiguë.");
        if (entry.Card.ProjectId != manifest.Id ||
            entry.Card.Reference != intent.SourceReference ||
            entry.RelativePath != intent.SourceRelativePath)
        {
            throw new InvalidDataException(
                "Fiche RIS déplacée ou renumérotée depuis le préflight.");
        }

        List<string> blockers = [];
        List<string> warnings = [.. original.Warnings];
        if (sourceCatalog.Diagnostics.Count > 0 ||
            targetCatalog.Diagnostics.Count > 0)
        {
            blockers.Add("Registres RIS ambigus : corriger les diagnostics " +
                "avant toute préparation de transfert.");
        }

        string? next = targetCatalog.NextReference;
        if (next is null)
        {
            blockers.Add("Aucune référence RIS libre dans le destinataire.");
        }

        string? targetRelative = next is null ? null :
            original.TargetProjectRelativePath +
            "/Analyse/Risques/" + next + ".md";
        if (targetRelative is not null)
        {
            string candidate = Path.GetFullPath(Path.Combine(
                _root, targetRelative.Replace('/',
                    Path.DirectorySeparatorChar)));
            if (Path.Exists(candidate))
            {
                blockers.Add("La fiche RIS destinataire existe déjà.");
            }
        }

        string sourceFile = _sources.ResolveExistingFile(
            intent.SourceRelativePath);
        FileInfo info = new(sourceFile);
        if (info.Length > MaxRiskBytes)
        {
            throw new InvalidDataException("Fiche RIS source trop volumineuse.");
        }

        byte[] bytes = await File.ReadAllBytesAsync(
            sourceFile, cancellationToken);
        string sha = Convert.ToHexString(SHA256.HashData(bytes));
        if (bytes.Length > MaxRiskBytes ||
            !string.Equals(sha, entry.SourceSha256,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                "Source RIS modifiée pendant sa lecture.");
        }

        string markdown = new UTF8Encoding(false, true).GetString(bytes);
        if (markdown.StartsWith('\uFEFF') ||
            Encoding.UTF8.GetByteCount(markdown) != bytes.Length ||
            markdown.Replace("\r\n", string.Empty,
                StringComparison.Ordinal).Contains('\r'))
        {
            blockers.Add("Fiche RIS source non canonique UTF-8.");
        }

        ProjectRiskCard checkedCard = ProjectRiskMarkdownCodec.Parse(markdown);
        if (checkedCard.Id != entry.Card.Id ||
            checkedCard.ProjectId != entry.Card.ProjectId ||
            checkedCard.Reference != entry.Card.Reference)
        {
            throw new InvalidDataException(
                "Métadonnées RIS incompatibles avec la fiche lue.");
        }

        warnings.Add("Les revues de risques, évaluations, plans, mesures, " +
            "tâches associées et décisions Go/No-Go sont historiques et " +
            "ne se transfèrent jamais sans requalification humaine.");
        string? proposedMarkdown = null;
        if (intent.Method == ProjectHandoverMethod.LinkedSuccessor &&
            blockers.Count == 0 && next is not null)
        {
            proposedMarkdown = ProjectRiskHandoverDraft.Prepare(
                checkedCard, original.TargetProjectId, next,
                intent.ProposedTargetObjectId, intent.RecordedUtc);
            warnings.Add("Le successeur B reprend uniquement le titre ; " +
                "statuts, cotations et traitements repartent à zéro.");
        }
        else if (intent.Method == ProjectHandoverMethod.MoveOriginal)
        {
            warnings.Add("Le mode A conserverait historique et GUID, mais " +
                "nécessite encore un exécuteur transactionnel avec audit " +
                "des backlinks, sauvegarde, reprise et rollback.");
        }

        return new ProjectRiskHandoverPreview
        {
            Intent = intent,
            SourceRelativePath = entry.RelativePath,
            SourceSha256 = sha,
            TargetRelativePath = targetRelative,
            TargetReference = next,
            TargetId = intent.ProposedTargetObjectId,
            OriginalState = entry.Summary,
            SuccessorMarkdown = proposedMarkdown,
            Blockers = blockers.ToArray(),
            Warnings = warnings.ToArray()
        };
    }
}
