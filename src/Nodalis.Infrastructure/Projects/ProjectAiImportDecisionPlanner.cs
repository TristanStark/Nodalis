using Nodalis.Core.Importing;
using Nodalis.Core.Markdown;
using Nodalis.Core.Requirements;

namespace Nodalis.Infrastructure.Projects;

/// <summary>
/// A human-controlled disposition. "Review" and "Ignore" never produce writes.
/// Neither "Accept" nor "Edit" is inferred from an AI certainty label.
/// </summary>
public enum ProjectAiFieldDecisionKind
{
    Review,
    Ignore,
    Accept,
    Edit
}

/// <summary>One explicit, in-memory human choice for a proposal field.</summary>
/// <param name="ProposalId">Stable ID of the untrusted proposal.</param>
/// <param name="Field">A field present in that proposal.</param>
/// <param name="Kind">Human-selected disposition.</param>
/// <param name="EditedValue">Mandatory for Edit, forbidden otherwise.</param>
/// <param name="Reason">Human justification when overriding uncertainty.</param>
public sealed record ProjectAiFieldDecision(
    Guid ProposalId, string Field, ProjectAiFieldDecisionKind Kind,
    string? EditedValue = null, string? Reason = null);

/// <summary>
/// Snapshot-bound proposed title change, NOT an executable transaction.
/// The source hash is mandatory for later optimistic commit revalidation.
/// </summary>
/// <param name="ProposalId">External proposal's idempotency key.</param>
/// <param name="RequirementId">Real project-local, existing stable GUID.</param>
/// <param name="ProjectId">Canonical owner project GUID.</param>
/// <param name="Reference">Existing canonical EXI reference.</param>
/// <param name="RelativePath">Verified source file path from catalog.</param>
/// <param name="ExpectedSha256">Exact current-source hash when compared.</param>
/// <param name="CurrentTitle">Human-visible value at comparison time.</param>
/// <param name="ReviewedTitle">Explicitly accepted or edited title.</param>
/// <param name="Reason">Justification for an uncertain baseline, if relevant.</param>
public sealed record ProjectAiPreparedTitleChange(
    Guid ProposalId, Guid RequirementId, Guid ProjectId, string Reference,
    string RelativePath, string ExpectedSha256, string CurrentTitle,
    string ReviewedTitle, string? Reason);

/// <summary>
/// One precisely scoped, non-executable description update tied to a
/// project-owned EXI source SHA. The candidate may not define an identity.
/// </summary>
/// <param name="ProposalId">External candidate key only.</param>
/// <param name="RequirementId">Existing, unique Nodalis requirement GUID.</param>
/// <param name="ProjectId">Authoritative project GUID.</param>
/// <param name="Reference">Canonical EXI reference.</param>
/// <param name="RelativePath">Verified canonical source path.</param>
/// <param name="ExpectedSha256">Exact SHA-256 of the reviewed Markdown.</param>
/// <param name="CurrentDescription">Extracted local text at comparison time.</param>
/// <param name="ReviewedDescription">Human-approved replacement text.</param>
/// <param name="Reason">Human justification for uncertain provenance.</param>
public sealed record ProjectAiPreparedDescriptionChange(
    Guid ProposalId, Guid RequirementId, Guid ProjectId, string Reference,
    string RelativePath, string ExpectedSha256, string CurrentDescription,
    string ReviewedDescription, string? Reason);

/// <summary>
/// Inert, human-approved update of an existing stable CA block title.
/// Local CA codes are not identities and no status or verdict is imported.
/// </summary>
/// <param name="ProposalId">External candidate ID only.</param>
/// <param name="RequirementId">Canonical containing EXI GUID.</param>
/// <param name="ProjectId">Owning project manifest GUID.</param>
/// <param name="Reference">Canonical EXI reference.</param>
/// <param name="CriterionId">Unique existing Markdown block GUID.</param>
/// <param name="CriterionReference">Local CA-xx code within the EXI.</param>
/// <param name="RelativePath">Canonical workspace-relative EXI Markdown source.</param>
/// <param name="ExpectedSha256">Reviewed source byte SHA-256.</param>
/// <param name="CurrentTitle">Exact local CA heading title when reviewed.</param>
/// <param name="ReviewedTitle">Explicitly accepted or human edited title.</param>
/// <param name="Reason">Justification for unknown or conflicting baselines.</param>
public sealed record ProjectAiPreparedCriterionTitleChange(
    Guid ProposalId, Guid RequirementId, Guid ProjectId, string Reference,
    Guid CriterionId, string CriterionReference, string RelativePath,
    string ExpectedSha256, string CurrentTitle, string ReviewedTitle,
    string? Reason);

/// <summary>
/// One inert, SHA-bound proposal to change the Markdown body of a stable CA.
/// This contains no verdict, approval status, link creation or model-assigned ID.
/// </summary>
/// <param name="ProposalId">External candidate key for audit only.</param>
/// <param name="RequirementId">Canonical parent EXI GUID.</param>
/// <param name="ProjectId">Authoritative project manifest GUID.</param>
/// <param name="Reference">Canonical EXI code.</param>
/// <param name="CriterionId">Existing stable block GUID.</param>
/// <param name="CriterionReference">Local CA-xx reference.</param>
/// <param name="RelativePath">Canonical source path.</param>
/// <param name="ExpectedSha256">Exact Markdown SHA from the review.</param>
/// <param name="CurrentBody">Bounded local field text reviewed by the human.</param>
/// <param name="ReviewedBody">Human-approved replacement body.</param>
/// <param name="Reason">Human justification for unknown/concurrent base.</param>
public sealed record ProjectAiPreparedCriterionBodyChange(
    Guid ProposalId, Guid RequirementId, Guid ProjectId, string Reference,
    Guid CriterionId, string CriterionReference, string RelativePath,
    string ExpectedSha256, string CurrentBody, string ReviewedBody,
    string? Reason);

/// <summary>
/// Inert, human-approved request to append one new CA to an existing EXI.
/// The CA reference and persistent block GUID are allocated only by Nodalis
/// after a separate preflight, never accepted from the external model.
/// </summary>
/// <param name="ProposalId">External proposal ID for idempotency only.</param>
/// <param name="ProjectId">Manifest ID of the current project.</param>
/// <param name="RequirementId">Existing parent EXI GUID.</param>
/// <param name="Reference">Existing parent EXI reference.</param>
/// <param name="RelativePath">Canonical SHA-pinned Markdown source.</param>
/// <param name="ExpectedSha256">Current source fingerprint at human review.</param>
/// <param name="ReviewedTitle">One human-accepted CA title.</param>
/// <param name="Reason">Optional human provenance reason.</param>
public sealed record ProjectAiPreparedCriterionCreation(
    Guid ProposalId, Guid ProjectId, Guid RequirementId, string Reference,
    string RelativePath, string ExpectedSha256, string ReviewedTitle,
    string? Reason);

/// <summary>
/// A human-approved request for one NEW requirement. Neither project GUID,
/// requirement GUID nor EXI reference can be supplied by the external AI.
/// They are assigned solely in the guarded publication service.
/// </summary>
/// <param name="ProposalId">External response's idempotency key.</param>
/// <param name="ProjectId">Verified owner project from the canonical catalog.</param>
/// <param name="ReviewedTitle">Explicitly approved or edited one-line title.</param>
/// <param name="Reason">Optional human provenance note.</param>
public sealed record ProjectAiPreparedRequirementCreation(
    Guid ProposalId, Guid ProjectId, string ReviewedTitle, string? Reason);

/// <summary>
/// Entire in-memory dry-run: no persistent decision, approval, file write or
/// authorization to run an external AI model.
/// </summary>
/// <param name="RawResponseSha256">Fingerprint of the exact pasted response.</param>
/// <param name="Changes">Explicit, validated title changes only.</param>
/// <param name="ReviewedFields">Fields with an explicit human disposition.</param>
/// <param name="IgnoredFields">Fields explicitly ignored.</param>
/// <param name="PendingFields">Unreviewed fields plus fields explicitly left for review.</param>
public sealed record ProjectAiPreparedReviewPlan(
    string RawResponseSha256,
    IReadOnlyList<ProjectAiPreparedTitleChange> Changes,
    int ReviewedFields, int IgnoredFields, int PendingFields)
{
    /// <summary>
    /// Creation requests are inert until a human approves a fresh,
    /// Nodalis-assigned EXI reference in the separate publication dialog.
    /// </summary>
    public IReadOnlyList<ProjectAiPreparedRequirementCreation> Creations
    { get; init; } = [];

    /// <summary>
    /// Approved description edits are independent of title transactions.
    /// No existing publisher may silently apply or ignore these edits.
    /// </summary>
    public IReadOnlyList<ProjectAiPreparedDescriptionChange> Descriptions
    { get; init; } = [];

    /// <summary>
    /// Criterion title approvals cannot be silently published by existing
    /// requirement-title, description, creation or mixed batch engines.
    /// </summary>
    public IReadOnlyList<ProjectAiPreparedCriterionTitleChange> CriterionTitles
    { get; init; } = [];

    /// <summary>
    /// CA Markdown-body edits are independent of both CA-title and
    /// requirement-field publications until specifically confirmed.
    /// </summary>
    public IReadOnlyList<ProjectAiPreparedCriterionBodyChange> CriterionBodies
    { get; init; } = [];

    /// <summary>
    /// Explicit proposals for CA creation are never applied by any existing
    /// title/description/requirement transaction without a dedicated journal.
    /// </summary>
    public IReadOnlyList<ProjectAiPreparedCriterionCreation> CriterionCreations
    { get; init; } = [];
}

/// <summary>
/// Pure planner for human decisions. No files can be created or modified here.
/// Each accepted field is tied to the EXACT source revision observed during
/// local comparison; unsupported fields and ambiguous targets fail closed.
/// </summary>
public static class ProjectAiImportDecisionPlanner
{
    /// <summary>Builds a non-executable, field-by-field approved preview.</summary>
    /// <param name="preview">Bounded, validated external JSON response.</param>
    /// <param name="reviews">Current-project canonical comparison.</param>
    /// <param name="decisions">Human choices, never generated by an AI.</param>
    /// <returns>One conflict-checked in-memory plan.</returns>
    public static ProjectAiPreparedReviewPlan Prepare(
        ProjectAiResponsePreview preview,
        IReadOnlyList<ProjectAiRequirementReview> reviews,
        IReadOnlyList<ProjectAiFieldDecision> decisions)
    {
        ArgumentNullException.ThrowIfNull(preview);
        ArgumentNullException.ThrowIfNull(reviews);
        ArgumentNullException.ThrowIfNull(decisions);
        ProjectAiResponseImport.Validate(preview.Document);
        if (preview.RawSha256.Length != 64 ||
            !preview.RawSha256.All(Uri.IsHexDigit))
        {
            throw new InvalidDataException("Empreinte de réponse IA invalide.");
        }

        Dictionary<Guid, ProjectAiImportCandidate> candidates =
            preview.Document.Proposals.ToDictionary(item => item.ProposalId);
        Dictionary<Guid, ProjectAiRequirementReview> matches = [];
        foreach (ProjectAiRequirementReview review in reviews)
        {
            if (!candidates.TryGetValue(review.Candidate.ProposalId,
                    out ProjectAiImportCandidate? original) ||
                !ReferenceEquals(original, review.Candidate) ||
                !matches.TryAdd(review.Candidate.ProposalId, review))
            {
                throw new InvalidDataException(
                    "Comparaison incohérente avec la réponse analysée.");
            }
        }

        if (matches.Count != candidates.Count)
        {
            throw new InvalidDataException(
                "Comparaison incomplète : réexaminer la réponse avant la suite.");
        }

        HashSet<(Guid, string)> reviewed = [];
        HashSet<Guid> alreadyTargeted = [];
        HashSet<Guid> targetedCriteria = [];
        HashSet<Guid> targetedBodies = [];
        HashSet<Guid> createdInParents = [];
        List<ProjectAiPreparedCriterionCreation> criterionCreations = [];
        List<ProjectAiPreparedCriterionTitleChange> criterionTitles = [];
        List<ProjectAiPreparedCriterionBodyChange> criterionBodies = [];
        List<ProjectAiPreparedTitleChange> changes = [];
        List<ProjectAiPreparedDescriptionChange> descriptions = [];
        List<ProjectAiPreparedRequirementCreation> creations = [];
        int ignored = 0;
        foreach (ProjectAiFieldDecision decision in decisions)
        {
            if (decision is null ||
                !Enum.IsDefined(decision.Kind) ||
                !candidates.TryGetValue(decision.ProposalId,
                    out ProjectAiImportCandidate? proposal) ||
                !proposal.ProposedFields.ContainsKey(decision.Field) ||
                !reviewed.Add((decision.ProposalId, decision.Field)) ||
                decision.EditedValue is not null &&
                    decision.Kind != ProjectAiFieldDecisionKind.Edit ||
                decision.Kind == ProjectAiFieldDecisionKind.Edit &&
                    decision.EditedValue is null ||
                decision.Reason is { Length: > 1200 } ||
                decision.Reason?.Any(character => character == '\0') == true)
            {
                throw new InvalidDataException(
                    "Choix humain absent, ambigu, dupliqué ou hors contrat.");
            }

            if (decision.Kind == ProjectAiFieldDecisionKind.Ignore)
            {
                ignored++;
                continue;
            }

            if (decision.Kind == ProjectAiFieldDecisionKind.Review)
            {
                continue;
            }

            ProjectAiRequirementReview match = matches[decision.ProposalId];
            if (proposal.Kind == "criterion" &&
                proposal.Operation == "create")
            {
                if (decision.Field != "title" ||
                    match.Status != ProjectAiRequirementMatchStatus
                        .CriterionCreationDraft ||
                    match.Requirement is not ProjectRequirementEntry parent ||
                    match.Criterion is not null ||
                    match.ProjectId == Guid.Empty ||
                    parent.Card.ProjectId != match.ProjectId ||
                    proposal.TargetId != parent.Card.Id ||
                    !string.IsNullOrWhiteSpace(proposal.CandidateReference) ||
                    parent.SourceSha256 is not { Length: 64 } ||
                    !parent.SourceSha256.All(Uri.IsHexDigit) ||
                    !ProjectRequirementMarkdownCodec.IsReference(
                        parent.Card.Reference))
                {
                    throw new InvalidDataException(
                        "Parent EXI non résolu ou code CA fourni par l'IA.");
                }

                string title = decision.Kind == ProjectAiFieldDecisionKind.Edit
                    ? decision.EditedValue!
                    : proposal.ProposedFields["title"];
                if (string.IsNullOrWhiteSpace(title) ||
                    title != title.Trim() || title.Length > 240 ||
                    title.Any(char.IsControl) ||
                    title.StartsWith('#') || title.EndsWith('#') ||
                    title.Contains("<!--", StringComparison.Ordinal) ||
                    !createdInParents.Add(parent.Card.Id))
                {
                    throw new InvalidDataException(
                        "Titre CA non canonique ou plusieurs créations " +
                        "visant le même parent EXI.");
                }

                criterionCreations.Add(new ProjectAiPreparedCriterionCreation(
                    proposal.ProposalId, match.ProjectId, parent.Card.Id,
                    parent.Card.Reference, parent.RelativePath,
                    parent.SourceSha256, title, decision.Reason?.Trim()));
                continue;
            }

            if (proposal.Kind == "criterion")
            {
                if (proposal.Operation != "update" ||
                    decision.Field is not ("title" or "description") ||
                    match.Status != ProjectAiRequirementMatchStatus
                        .CriterionGuidMatch ||
                    match.Requirement is not ProjectRequirementEntry parent ||
                    match.Criterion is not ProjectRequirementCriterion criterion ||
                    criterion.Id is not Guid criterionId ||
                    proposal.TargetId != criterionId ||
                    !criterion.LocallyIdentified ||
                    parent.Card.ProjectId != match.ProjectId ||
                    !ProjectRequirementCriterionCodec.IsReference(
                        criterion.Reference) ||
                    !ProjectRequirementMarkdownCodec.IsReference(
                        parent.Card.Reference) ||
                    parent.SourceSha256 is not { Length: 64 } ||
                    !parent.SourceSha256.All(Uri.IsHexDigit))
                {
                    throw new InvalidDataException(
                        "Critère CA sans identité canonique : validation refusée.");
                }

                ProjectAiFieldDifference? criterionDiff =
                    match.Fields.SingleOrDefault(item =>
                        item.Field == decision.Field);
                string? criterionCurrent = decision.Field == "title"
                    ? criterion.Title : criterion.Body;
                if (criterionDiff is null || criterionCurrent is null ||
                    !string.Equals(criterionDiff.Current, criterionCurrent,
                        StringComparison.Ordinal))
                {
                    throw new InvalidDataException(
                        "Comparaison CA absente ou périmée.");
                }

                string approved = decision.Kind == ProjectAiFieldDecisionKind.Edit
                    ? decision.EditedValue!
                    : proposal.ProposedFields[decision.Field];
                if (decision.Field == "title")
                {
                    if (string.IsNullOrWhiteSpace(approved) ||
                        approved != approved.Trim() || approved.Length > 240 ||
                        approved.Any(char.IsControl) ||
                        approved.StartsWith('#') || approved.EndsWith('#') ||
                        approved.Contains("<!--", StringComparison.Ordinal))
                    {
                        throw new InvalidDataException(
                            "Titre CA approuvé invalide : 240 caractères, une ligne.");
                    }
                }
                else if (approved.Length > 8000 ||
                    approved != approved.Trim('\n') ||
                    approved.Contains('\r') || approved.Contains('\0') ||
                    approved.Contains('\uFEFF') ||
                    approved.Contains("<!-- nodalis:block:",
                        StringComparison.OrdinalIgnoreCase) ||
                    MarkdownOutlineParser.Parse(approved).Any(item =>
                        item.Level <= 3))
                {
                    throw new InvalidDataException(
                        "Corps CA approuvé invalide : 8000 caractères, " +
                        "LF et titres Markdown H4+ uniquement.");
                }

                if (criterionDiff.Kind is
                    ProjectAiFieldDifferenceKind.ConcurrentConflict or
                    ProjectAiFieldDifferenceKind.UnknownBase &&
                    string.IsNullOrWhiteSpace(decision.Reason))
                {
                    throw new InvalidDataException(
                        "Titre CA concurrent ou base inconnue : justification requise.");
                }

                if (decision.Field == "description")
                {
                    if (!targetedBodies.Add(criterionId))
                    {
                        throw new InvalidDataException(
                            "Plusieurs descriptions IA ciblent le même bloc CA.");
                    }

                    if (!string.Equals(approved, criterionCurrent, StringComparison.Ordinal))
                    {
                        criterionBodies.Add(new ProjectAiPreparedCriterionBodyChange(
                            proposal.ProposalId, parent.Card.Id,
                            parent.Card.ProjectId, parent.Card.Reference,
                            criterionId, criterion.Reference,
                            parent.RelativePath, parent.SourceSha256,
                            criterionCurrent, approved, decision.Reason?.Trim()));
                    }
                }
                else
                {
                    if (!targetedCriteria.Add(criterionId))
                    {
                        throw new InvalidDataException(
                            "Plusieurs titres IA ciblent le même bloc CA.");
                    }

                    if (!string.Equals(approved, criterionCurrent, StringComparison.Ordinal))
                    {
                        criterionTitles.Add(new ProjectAiPreparedCriterionTitleChange(
                            proposal.ProposalId, parent.Card.Id, parent.Card.ProjectId,
                            parent.Card.Reference, criterionId, criterion.Reference,
                            parent.RelativePath, parent.SourceSha256,
                            criterionCurrent, approved, decision.Reason?.Trim()));
                    }
                }

                continue;
            }

            if (proposal.Kind == "requirement" &&
                proposal.Operation == "create")
            {
                if (decision.Field != "title" ||
                    match.Status != ProjectAiRequirementMatchStatus.CreationDraft ||
                    match.ProjectId == Guid.Empty ||
                    proposal.TargetId.HasValue ||
                    !string.IsNullOrWhiteSpace(proposal.CandidateReference))
                {
                    throw new InvalidDataException(
                        "Création IA ambiguë : aucun identifiant ou autre champ " +
                        "ne peut être approuvé automatiquement.");
                }

                string newTitle = decision.Kind == ProjectAiFieldDecisionKind.Edit
                    ? decision.EditedValue!
                    : proposal.ProposedFields["title"];
                if (string.IsNullOrWhiteSpace(newTitle) ||
                    newTitle != newTitle.Trim() ||
                    newTitle.Length > 240 ||
                    newTitle.Any(char.IsControl))
                {
                    throw new InvalidDataException(
                        "Titre de création invalide : 240 caractères, une ligne.");
                }

                creations.Add(new ProjectAiPreparedRequirementCreation(
                    proposal.ProposalId, match.ProjectId, newTitle,
                    decision.Reason?.Trim()));
                continue;
            }

            if (proposal.Kind != "requirement" ||
                proposal.Operation != "update" ||
                (decision.Field != "title" && decision.Field != "description") ||
                match.Status is not (
                    ProjectAiRequirementMatchStatus.GuidMatch or
                    ProjectAiRequirementMatchStatus.ReferenceMatch) ||
                match.Requirement is not ProjectRequirementEntry entry)
            {
                throw new InvalidDataException(
                    "Champ non résolu ou non pris en charge : validation interdite.");
            }

            ProjectAiFieldDifference? diff = match.Fields.SingleOrDefault(
                field => field.Field == decision.Field);
            string? actual = decision.Field == "title"
                ? entry.Card.Title : entry.Description;
            if (diff is null || actual is null ||
                !string.Equals(diff.Current, actual,
                    StringComparison.Ordinal) ||
                entry.SourceSha256.Length != 64 ||
                !entry.SourceSha256.All(Uri.IsHexDigit) ||
                entry.Card.Id == Guid.Empty ||
                entry.Card.ProjectId == Guid.Empty ||
                !ProjectRequirementMarkdownCodec.IsReference(entry.Card.Reference))
            {
                throw new InvalidDataException(
                    "État canonique incomplet : relancer la comparaison.");
            }

            string value = decision.Kind == ProjectAiFieldDecisionKind.Edit
                ? decision.EditedValue!
                : proposal.ProposedFields[decision.Field];
            if (decision.Field == "title")
            {
                if (string.IsNullOrWhiteSpace(value) ||
                    value != value.Trim() ||
                    value.Length > 240 ||
                    value.Any(char.IsControl))
                {
                    throw new InvalidDataException(
                        "Titre approuvé invalide : texte sur une ligne, 240 caractères maximum.");
                }
            }
            else if (value.Length > 8000 || value.Contains('\r') ||
                value.Contains('\0') || value.Contains('\uFEFF') ||
                value != value.Trim('\n') ||
                value.Split('\n').Any(line =>
                    line.StartsWith("# ", StringComparison.Ordinal) ||
                    line.StartsWith("## ", StringComparison.Ordinal)))
            {
                throw new InvalidDataException(
                    "Description approuvée invalide : 8000 caractères, retours LF uniquement.");
            }

            // Unknown/conflicting baselines cannot become silent approvals.
            // A human must explicitly explain why the update is still desired.
            if (diff.Kind is
                ProjectAiFieldDifferenceKind.ConcurrentConflict or
                ProjectAiFieldDifferenceKind.UnknownBase &&
                string.IsNullOrWhiteSpace(decision.Reason))
            {
                throw new InvalidDataException(
                    "Base absente ou conflit local : justification humaine requise.");
            }

            if (!alreadyTargeted.Add(entry.Card.Id))
            {
                throw new InvalidDataException(
                    "Deux propositions approuvées touchent la même exigence : arbitrer.");
            }

            if (string.Equals(value, actual, StringComparison.Ordinal))
            {
                continue;
            }

            if (decision.Field == "description")
            {
                descriptions.Add(new ProjectAiPreparedDescriptionChange(
                    proposal.ProposalId, entry.Card.Id, entry.Card.ProjectId,
                    entry.Card.Reference, entry.RelativePath, entry.SourceSha256,
                    actual, value, decision.Reason?.Trim()));
            }
            else
            {
                changes.Add(new ProjectAiPreparedTitleChange(
                    proposal.ProposalId, entry.Card.Id, entry.Card.ProjectId,
                    entry.Card.Reference, entry.RelativePath, entry.SourceSha256,
                    entry.Card.Title, value, decision.Reason?.Trim()));
            }
        }

        int totalFields = candidates.Values.Sum(item => item.ProposedFields.Count);
        return new ProjectAiPreparedReviewPlan(preview.RawSha256, changes,
            reviewed.Count, ignored, totalFields - reviewed.Count +
                decisions.Count(item => item.Kind == ProjectAiFieldDecisionKind.Review))
        {
            Creations = creations,
            Descriptions = descriptions,
            CriterionTitles = criterionTitles,
            CriterionBodies = criterionBodies,
            CriterionCreations = criterionCreations
        };
    }
}
