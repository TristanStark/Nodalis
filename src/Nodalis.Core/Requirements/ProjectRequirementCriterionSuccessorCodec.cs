using System.Security.Cryptography;
using System.Text;
using Nodalis.Core.Markdown;
using Nodalis.Core.Projects;
using Nodalis.Core.Tasks;

namespace Nodalis.Core.Requirements;

/// <summary>
/// Stable, source-to-successor mapping for one acceptance criterion.
/// Neither a test verdict nor a claim that evidence is transferred.
/// </summary>
/// <param name="Reference">Source and successor local CA-xx code.</param>
/// <param name="Title">Explicit criterion title only; bodies are discarded.</param>
/// <param name="OriginalId">Persistent source criterion GUID.</param>
/// <param name="SuccessorId">New independently derived successor GUID.</param>
public sealed record ProjectRequirementCriterionSuccession(
    string Reference, string Title, Guid OriginalId, Guid SuccessorId);

/// <summary>
/// Deterministic in-memory successor draft and ordered CA provenance.
/// </summary>
/// <param name="Markdown">Never-written proposed UTF-8 Markdown.</param>
/// <param name="Criteria">Exact mapping requiring human requalification.</param>
public sealed record ProjectRequirementCriterionSuccessorDraft(
    string Markdown, IReadOnlyList<ProjectRequirementCriterionSuccession> Criteria);

/// <summary>
/// Produces safe EXI B successors with new CA identities. In contrast to
/// mode A, none of the original criterion bodies, reports, verdicts, links,
/// proof sections or implementation state is copied into the new draft.
/// </summary>
public static class ProjectRequirementCriterionSuccessorCodec
{
    /// <summary>
    /// Creates deterministic candidate Markdown and unique CA mapping from
    /// an approved immutable B intent; source bytes are never modified.
    /// </summary>
    /// <param name="markdown">SHA-pinned existing source Markdown.</param>
    /// <param name="source">Original canonical EXI metadata.</param>
    /// <param name="targetProjectId">Active recipient project.</param>
    /// <param name="targetReference">Reviewed unreserved EXI-xxxx.</param>
    /// <param name="newRequirementId">New ID from the B intention.</param>
    /// <param name="intentId">Source-owned human B intent ID.</param>
    /// <param name="recordedUtc">Immutable B intent timestamp.</param>
    /// <returns>Pure new draft and old/new CA GUID association.</returns>
    public static ProjectRequirementCriterionSuccessorDraft Prepare(
        string markdown, ProjectRequirementCard source,
        Guid targetProjectId, string targetReference, Guid newRequirementId,
        Guid intentId, DateTimeOffset recordedUtc)
    {
        ArgumentNullException.ThrowIfNull(markdown);
        ProjectRequirementMarkdownCodec.Validate(source);
        if (targetProjectId == Guid.Empty ||
            targetProjectId == source.ProjectId ||
            newRequirementId == Guid.Empty ||
            newRequirementId == source.Id || intentId == Guid.Empty ||
            recordedUtc == default ||
            !ProjectRequirementMarkdownCodec.IsReference(targetReference) ||
            markdown.StartsWith('\uFEFF') ||
            markdown.Replace("\r\n", "", StringComparison.Ordinal)
                .Contains('\r'))
        {
            throw new InvalidDataException(
                "Projet, intention ou encodage EXI B avec CA invalide.");
        }

        ProjectRequirementCard actual =
            ProjectRequirementMarkdownCodec.Parse(markdown);
        if (actual != source)
        {
            throw new InvalidDataException(
                "L'exigence CA source a changé avant sa succession.");
        }

        ProjectRequirementCriterionScan criteria =
            ProjectRequirementCriterionCodec.Inspect(markdown);
        MarkdownBlockIdentityScan blocks =
            MarkdownBlockIdentityCodec.Inspect(markdown);
        TaskPersistentIdentityScan tasks =
            TaskPersistentIdentityCodec.Inspect(
                markdown.Replace("\r\n", "\n",
                    StringComparison.Ordinal).Split('\n'));
        if (criteria.Criteria.Count == 0 ||
            criteria.Criteria.Count > 99 ||
            criteria.Diagnostics.Count > 0 ||
            blocks.Issues.Count > 0 ||
            tasks.Issues.Count > 0 ||
            criteria.Criteria.Any(item =>
                item.Id is null || item.Id == Guid.Empty ||
                !item.LocallyIdentified ||
                string.IsNullOrWhiteSpace(item.Title) ||
                item.Title.Contains("[[", StringComparison.Ordinal) ||
                item.Title.Contains("](", StringComparison.Ordinal)) ||
            blocks.Blocks.Where(item => item.Id.HasValue).Any(block =>
                !criteria.Criteria.Any(criterion =>
                    criterion.Id == block.Id &&
                    criterion.HeadingLine == block.HeadingLine)) ||
            System.Text.RegularExpressions.Regex.Matches(markdown,
                "<!-- nodalis:block:",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase).Count !=
                criteria.Criteria.Count)
        {
            throw new InvalidDataException(
                "Succession CA impossible : GUID, titres, marqueurs " +
                "ou structure d'acceptation non fiables.");
        }

        // Source evidence and even free-form source prose are NOT copied.
        // Only the verified CA reference/title survive, under NEW identities.
        ProjectRequirementCriterionSuccession[] mapping =
            criteria.Criteria.Select(item => new
                ProjectRequirementCriterionSuccession(item.Reference, item.Title,
                    item.Id!.Value, DeriveId(intentId, item.Id.Value))).ToArray();
        if (mapping.Select(item => item.SuccessorId).Distinct().Count() !=
                mapping.Length ||
            mapping.Any(item =>
                item.SuccessorId == item.OriginalId ||
                item.SuccessorId == newRequirementId ||
                item.SuccessorId == source.Id))
        {
            throw new InvalidDataException(
                "Collision de GUID CA sur la nouvelle EXI.");
        }

        ProjectRequirementCard successor = source with
        {
            Id = newRequirementId,
            ProjectId = targetProjectId,
            Reference = targetReference,
            CreatedUtc = recordedUtc,
            Validation = ProjectRequirementValidation.Draft,
            Implementation = ProjectRequirementImplementation.NotStarted,
            Verification = ProjectRequirementVerification.NotVerified
        };
        string newline = markdown.Contains("\r\n",
            StringComparison.Ordinal) ? "\r\n" : "\n";
        string baseMarkdown = ProjectRequirementMarkdownCodec.Format(successor)
            .Replace("\r\n", "\n", StringComparison.Ordinal);
        StringBuilder criteriaText = new();
        foreach (ProjectRequirementCriterionSuccession criterion in mapping)
        {
            criteriaText.Append("### ").Append(criterion.Reference)
                .Append(" — ").Append(criterion.Title).Append('\n')
                .Append(MarkdownBlockIdentityCodec.FormatMarker(
                    criterion.SuccessorId)).Append('\n')
                .Append('\n');
        }

        const string heading = "## Critères d'acceptation\n\n";
        if (!baseMarkdown.Contains(heading, StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                "Modèle de fiche EXI canonique modifié.");
        }

        baseMarkdown = baseMarkdown.Replace(heading,
            heading + criteriaText, StringComparison.Ordinal);
        StringBuilder sourceProvenance = new();
        sourceProvenance.Append("\n## Filiation et requalification")
            .Append("\n\n> Successeur B de l'exigence ")
            .Append(source.Id.ToString("D")).Append(" (")
            .Append(source.Reference).Append("), projet ")
            .Append(source.ProjectId.ToString("D"))
            .Append(". Intention humaine ")
            .Append(intentId.ToString("D"))
            .Append(". Aucun verdict, résultat de campagne ni preuve " +
                "de la source n'a été repris.\n\n");
        foreach (ProjectRequirementCriterionSuccession item in mapping)
        {
            sourceProvenance.Append("- ").Append(item.Reference)
                .Append(" : CA source ").Append(item.OriginalId.ToString("D"))
                .Append(" → CA successeur ")
                .Append(item.SuccessorId.ToString("D"))
                .Append(" · À requalifier.\n");
        }

        string result = (baseMarkdown.TrimEnd('\n') + "\n" +
            sourceProvenance.ToString().TrimEnd('\n') + "\n")
            .Replace("\n", newline, StringComparison.Ordinal);
        ProjectRequirementCard parsed =
            ProjectRequirementMarkdownCodec.Parse(result);
        ProjectRequirementCriterionScan newCriteria =
            ProjectRequirementCriterionCodec.Inspect(result);
        if (parsed != successor ||
            newCriteria.Diagnostics.Count != 0 ||
            !newCriteria.Criteria.Select(item => item.Id!.Value)
                .SequenceEqual(mapping.Select(item => item.SuccessorId)))
        {
            throw new InvalidDataException(
                "Fiche de succession EXI B avec CA invalide.");
        }

        return new ProjectRequirementCriterionSuccessorDraft(result, mapping);
    }

    /// <summary>Derives a distinct stable GUID from intent and source CA ID.</summary>
    /// <param name="intentId">Persisted human intent.</param>
    /// <param name="sourceId">Old stable CA ID.</param>
    /// <returns>Deterministic, GUID-compatible independent CA identity.</returns>
    public static Guid DeriveId(Guid intentId, Guid sourceId)
    {
        if (intentId == Guid.Empty || sourceId == Guid.Empty)
        {
            throw new ArgumentException("Intention ou critère CA sans GUID.");
        }

        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(
            "nodalis:exi-b:ca:v1:" + intentId.ToString("D") + ":" +
            sourceId.ToString("D")));
        byte[] bytes = hash[..16];
        bytes[7] = (byte)((bytes[7] & 0x0F) | 0x40);
        bytes[8] = (byte)((bytes[8] & 0x3F) | 0x80);
        return new Guid(bytes);
    }
}
