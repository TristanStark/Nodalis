using System.Security.Cryptography;
using System.Text;
using Nodalis.Core.Projects;
using Nodalis.Core.Requirements;

namespace Nodalis.Infrastructure.Projects;

/// <summary>
/// Read-only proposed EXI card transfer. A future executor must additionally
/// handle source deletion, backlinks and transactional restoration.
/// </summary>
public sealed record ProjectRequirementHandoverPreview
{
    /// <summary>Exactly approved original human intention.</summary>
    public required ProjectHandoverIntent Intent { get; init; }

    /// <summary>Canonical current workspace-relative source path.</summary>
    public required string SourceRelativePath { get; init; }

    /// <summary>Potential canonical new EXI source path in the recipient.</summary>
    public string? ProposedTargetRelativePath { get; init; }

    /// <summary>Next unreserved destination EXI code, not yet allocated.</summary>
    public string? ProposedReference { get; init; }

    /// <summary>Reviewed new destination EXI GUID.</summary>
    public Guid ProposedId { get; init; }

    /// <summary>SHA-256 of the exact original source Markdown bytes.</summary>
    public string SourceSha256 { get; init; } = string.Empty;

    /// <summary>Prospective complete Markdown, never written by this service.</summary>
    public string? ProposedMarkdown { get; init; }

    /// <summary>Reasons preventing preparation of an unambiguous card.</summary>
    public IReadOnlyList<string> Blockers { get; init; } = [];

    /// <summary>Links, references and other future execution requirements.</summary>
    public IReadOnlyList<string> Warnings { get; init; } = [];

    /// <summary>Indicates only in-memory EXI preparation, not executability.</summary>
    public bool CanPrepare => Blockers.Count == 0 &&
        ProposedMarkdown is not null;

    /// <summary>Displays the plan without implying any EXI was transferred.</summary>
    /// <returns>Bounded French summary of proposed identifiers and blockers.</returns>
    public string FormatReview()
    {
        StringBuilder text = new();
        text.AppendLine("Passation EXI — prévisualisation sans écriture")
            .Append("Origine : ").AppendLine(SourceRelativePath)
            .Append("Destination envisagée : ")
            .AppendLine(ProposedTargetRelativePath ?? "(référence indisponible)")
            .Append("Référence envisagée : ")
            .AppendLine(ProposedReference ?? "(aucune)")
            .Append("GUID envisagé : ")
            .AppendLine(ProposedId.ToString("D"))
            .Append("Stratégie : ").AppendLine(Intent.Method.ToString())
            .AppendLine();
        foreach (string reason in Blockers.Take(7))
        {
            text.AppendLine("BLOCAGE : " + reason);
        }

        foreach (string warning in Warnings.Take(7))
        {
            text.AppendLine("Vigilance : " + warning);
        }

        text.AppendLine()
            .AppendLine("Aucun fichier EXI n'a été créé, modifié ou déplacé. " +
                "Le transfert réel, la gestion des backlinks et le rollback " +
                "EXI restent à implémenter.");
        return text.ToString();
    }
}

/// <summary>
/// Human-on-demand EXI A/B dry-run over real source and target project
/// registries; no destination folder is created and no source is changed.
/// </summary>
public sealed class ProjectRequirementHandoverPreviewService
{
    private const int MaxBytes = 1024 * 1024;
    private readonly string _root;
    private readonly ProjectJourneySourceService _sources;
    private readonly ProjectHandoverPreflightService _preflight;
    private readonly ProjectHandoverIntentService _intents;

    /// <summary>Creates a workspace-confined preview reader.</summary>
    /// <param name="workspaceRoot">Single Nodalis workspace.</param>
    public ProjectRequirementHandoverPreviewService(string workspaceRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRoot);
        _root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(workspaceRoot));
        _sources = new ProjectJourneySourceService(_root);
        _preflight = new ProjectHandoverPreflightService(_root);
        _intents = new ProjectHandoverIntentService(_root);
    }

    /// <summary>
    /// Validates the signed source intention, both live project inventories,
    /// the exact Markdown source and a not-yet-reserved destination EXI code.
    /// </summary>
    /// <param name="sourceProjectDirectory">Original active owner.</param>
    /// <param name="intentId">Approved immutable human intention.</param>
    /// <param name="cancellationToken">Explicit read cancellation.</param>
    /// <returns>Prospective EXI content, target path and visible blockers.</returns>
    public async Task<ProjectRequirementHandoverPreview> PrepareAsync(
        string sourceProjectDirectory, Guid intentId,
        CancellationToken cancellationToken = default)
    {
        if (intentId == Guid.Empty)
        {
            throw new InvalidDataException("Intention EXI non identifiée.");
        }

        (string source, string sourceRelative,
            Nodalis.Core.Domain.ProjectManifest sourceOwner) =
            await _preflight.ResolveActiveProjectAsync(
                sourceProjectDirectory, cancellationToken);
        ProjectHandoverIntentSnapshot history =
            await _intents.ReadAsync(source, cancellationToken);
        ProjectHandoverIntent intent = history.Ledger.Intents.SingleOrDefault(
            item => item.Id == intentId) ??
            throw new InvalidDataException("Intention EXI non retrouvée.");
        if (intent.SourceObjectKind != "Exigences" ||
            intent.SourceProjectId != sourceOwner.Id ||
            !intent.SourceRelativePath.StartsWith(
                sourceRelative + "/Documentation/Exigences/",
                StringComparison.Ordinal) ||
            !intent.SourceRelativePath.EndsWith(
                "/" + intent.SourceReference + ".md",
                StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                "Seules les fiches EXI v2 du propriétaire peuvent être préparées.");
        }

        string target = Path.GetFullPath(Path.Combine(_root,
            intent.TargetProjectRelativePath.Replace('/',
                Path.DirectorySeparatorChar)));
        ProjectHandoverPreview basic = await _preflight.PrepareAsync(
            source, target, intent.SourceObjectId, intent.Method,
            intent.ProposedTargetObjectId, cancellationToken);
        if (!basic.CanRecord ||
            basic.SourceProjectId != intent.SourceProjectId ||
            basic.TargetProjectId != intent.TargetProjectId ||
            basic.SourceRelativePath != intent.SourceRelativePath ||
            basic.SourceObjectKind != intent.SourceObjectKind ||
            basic.SourceReference != intent.SourceReference ||
            basic.TargetProjectRelativePath != intent.TargetProjectRelativePath ||
            basic.SourceFingerprint != intent.SourceFingerprint ||
            basic.TargetFingerprint != intent.TargetFingerprint ||
            basic.ProposedTargetObjectId != intent.ProposedTargetObjectId)
        {
            throw new InvalidDataException(
                "L'intention EXI et ses sources ont changé : nouvelle revue requise.");
        }

        ProjectRequirementRegistryService registry = new(_root);
        ProjectRequirementCatalog originalCatalog =
            await registry.ListAsync(source, cancellationToken);
        ProjectRequirementEntry original = originalCatalog.Requirements
            .SingleOrDefault(item => item.Card.Id == intent.SourceObjectId) ??
            throw new InvalidDataException(
                "Exigence originale absente ou identité ambiguë.");
        if (original.RelativePath != intent.SourceRelativePath ||
            original.Card.Reference != intent.SourceReference)
        {
            throw new InvalidDataException(
                "L'exigence a été déplacée ou renumérotée.");
        }

        ProjectRequirementCatalog targetCatalog =
            await registry.ListAsync(target, cancellationToken);
        List<string> blockers = [];
        List<string> warnings = basic.Warnings.ToList();
        if (originalCatalog.Diagnostics.Count > 0 ||
            targetCatalog.Diagnostics.Count > 0)
        {
            blockers.Add("Registres EXI comportant des documents ambigus : " +
                "aucune préparation fiable avant correction.");
        }

        string? next = targetCatalog.NextReference;
        if (next is null)
        {
            blockers.Add("Toutes les références EXI sont réservées " +
                "dans le projet destinataire.");
        }

        string? targetRelative = next is null ? null :
            basic.TargetProjectRelativePath +
            "/Documentation/Exigences/" + next + ".md";
        if (targetRelative is not null)
        {
            string candidate = Path.GetFullPath(Path.Combine(_root,
                targetRelative.Replace('/', Path.DirectorySeparatorChar)));
            if (Path.Exists(candidate))
            {
                blockers.Add("Le fichier destinataire est déjà occupé.");
            }
        }

        string originalPath = _sources.ResolveExistingFile(
            intent.SourceRelativePath);
        FileInfo info = new(originalPath);
        if (info.Length > MaxBytes)
        {
            throw new InvalidDataException(
                "Exigence trop volumineuse pour une préparation sûre.");
        }

        byte[] bytes = await File.ReadAllBytesAsync(
            originalPath, cancellationToken);
        string sha = Convert.ToHexString(SHA256.HashData(bytes));
        if (!string.Equals(sha, original.SourceSha256,
            StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                "Source EXI modifiée durant sa lecture.");
        }

        string markdown = new UTF8Encoding(false, true).GetString(bytes);
        if (bytes.Length != Encoding.UTF8.GetByteCount(markdown) ||
            markdown.StartsWith('\uFEFF') || markdown.Contains('\r'))
        {
            blockers.Add("Markdown source non canonique UTF-8 LF.");
        }

        if (original.Criteria.Count > 0 || original.CriterionDiagnostics.Count > 0)
        {
            if (intent.Method == ProjectHandoverMethod.LinkedSuccessor)
            {
                blockers.Add("Successeur B d'une EXI avec critères " +
                    "d'acceptation : nouveaux identifiants CA à concevoir.");
            }
            else
            {
                warnings.Add("L'exigence possède des critères CA liés : " +
                    "liens et résultats devront suivre le GUID original.");
            }
        }

        warnings.Add("Une EXI contient potentiellement des liens " +
            "par référence EXI-xxxx, GUID ou critères CA. La migration A " +
            "requiert encore la réécriture et le journal des backlinks.");
        warnings.Add("Ce résultat est uniquement un aperçu mémoire ; " +
            "le transfert transactionnel EXI n'est pas activé.");

        string? proposed = null;
        if (blockers.Count == 0 && next is not null)
        {
            try
            {
                proposed = ProjectRequirementHandoverCodec.Prepare(
                    markdown, original.Card, basic.TargetProjectId,
                    next, intent.ProposedTargetObjectId, intent.Id,
                    intent.Method);
            }
            catch (InvalidDataException exception)
            {
                blockers.Add(exception.Message);
            }
        }

        return new ProjectRequirementHandoverPreview
        {
            Intent = intent,
            SourceRelativePath = intent.SourceRelativePath,
            ProposedTargetRelativePath = targetRelative,
            ProposedReference = next,
            ProposedId = intent.ProposedTargetObjectId,
            SourceSha256 = sha,
            ProposedMarkdown = proposed,
            Blockers = blockers,
            Warnings = warnings
        };
    }
}
