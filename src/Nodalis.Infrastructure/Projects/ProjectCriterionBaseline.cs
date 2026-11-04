using System.Security.Cryptography;
using System.Text;

namespace Nodalis.Infrastructure.Projects;

/// <summary>
/// One immutable portable CA observation, not proof that any linked test was
/// reviewed, run, verified or approved.
/// </summary>
/// <param name="CriterionId">Stable existing CA block GUID.</param>
/// <param name="RequirementId">Authoritative owning EXI GUID at observation.</param>
/// <param name="RequirementReference">Readable EXI reference at observation.</param>
/// <param name="CriterionReference">Readable locally scoped CA reference.</param>
/// <param name="Title">Exact title at observation; no matching by title.</param>
/// <param name="RelativePath">Canonical source at observation.</param>
/// <param name="DocumentSha256">Full source fingerprint for audit only.</param>
/// <param name="TitleSha256">Exact title fingerprint.</param>
/// <param name="BodySha256">LF-only body fingerprint, or null if unresolved.</param>
public sealed record ProjectCriterionBaselineEntry(
    Guid CriterionId, Guid RequirementId, string RequirementReference,
    string CriterionReference, string Title, string RelativePath,
    string DocumentSha256, string TitleSha256, string? BodySha256);

/// <summary>
/// One appended human-confirmed CA reference observation. The previous SHA
/// links the sequence; old references are never rewritten or treated as REC.
/// </summary>
public sealed record ProjectCriterionBaselineSnapshot
{
    /// <summary>Portable version discriminator.</summary>
    public required string Schema { get; init; }
    /// <summary>Verified owning project manifest identity.</summary>
    public required Guid ProjectId { get; init; }
    /// <summary>Gap-free one-based snapshot sequence.</summary>
    public required int Sequence { get; init; }
    /// <summary>UTC time when the person confirmed an observation.</summary>
    public required DateTimeOffset RecordedUtc { get; init; }
    /// <summary>Human-supplied reason; never a test approval.</summary>
    public required string Reason { get; init; }
    /// <summary>SHA of the preceding immutable snapshot, null for the first.</summary>
    public string? PreviousSha256 { get; init; }
    /// <summary>Canonical project-local requirement/criterion observations.</summary>
    public required IReadOnlyList<ProjectCriterionBaselineEntry> Entries
    { get; init; }
}

/// <summary>Read-only comparison with the latest explicitly recorded baseline.</summary>
public enum ProjectCriterionBaselineState
{
    NotTracked,
    Unchanged,
    Changed,
    Added,
    Missing,
    Unresolved
}

/// <summary>
/// One current or removed criterion in the derived baseline comparison.
/// Missing/unresolved never imply a passing test, approved EXI or lack of links.
/// </summary>
/// <param name="CriterionId">GUID, not local CA-xx reference.</param>
/// <param name="RequirementId">Latest or original owning EXI GUID.</param>
/// <param name="Label">Human label for UI only.</param>
/// <param name="State">Conservative source observation state.</param>
/// <param name="Reason">Why this observation needs human attention.</param>
public sealed record ProjectCriterionBaselineRow(
    Guid CriterionId, Guid RequirementId, string Label,
    ProjectCriterionBaselineState State, string Reason);

/// <summary>
/// Durable observation overview computed from a canonical EXI catalog; no
/// campaign history is changed when the criterion Markdown changes.
/// </summary>
/// <param name="Snapshot">Latest human baseline or null when not activated.</param>
/// <param name="Rows">One result per current or formerly known CA.</param>
/// <param name="Diagnostics">Source or baseline consistency limitations.</param>
public sealed record ProjectCriterionBaselineOverview(
    ProjectCriterionBaselineSnapshot? Snapshot,
    IReadOnlyList<ProjectCriterionBaselineRow> Rows,
    IReadOnlyList<string> Diagnostics)
{
    /// <summary>Changed, added or missing CA, not automatic test outcomes.</summary>
    public int ChangedCount => Rows.Count(row => row.State is
        ProjectCriterionBaselineState.Changed or
        ProjectCriterionBaselineState.Added or
        ProjectCriterionBaselineState.Missing);

    /// <summary>Sources whose state cannot safely be inferred.</summary>
    public int UnresolvedCount => Rows.Count(row => row.State is
        ProjectCriterionBaselineState.Unresolved or
        ProjectCriterionBaselineState.NotTracked);
}

/// <summary>
/// Pure identity- and hash-based CA observation projection. Changes to EXI
/// metadata or unrelated Markdown do not masquerade as a changed CA body.
/// </summary>
public static class ProjectCriterionBaselineProjection
{
    private const int MaxCriteria = 20000;

    /// <summary>Fingerprints all safely readable CA without writing.</summary>
    /// <param name="catalog">One project-owned canonical Markdown catalog.</param>
    /// <returns>Unique, stable CA entries or a conservative exception.</returns>
    public static IReadOnlyList<ProjectCriterionBaselineEntry> Capture(
        ProjectRequirementCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        if (catalog.ProjectId == Guid.Empty ||
            catalog.Diagnostics.Count != 0 ||
            catalog.Requirements.Any(requirement =>
                requirement.Card.Id == Guid.Empty ||
                requirement.Card.ProjectId != catalog.ProjectId ||
                !IsSha(requirement.SourceSha256)))
        {
            throw new InvalidDataException(
                "Registre EXI ambigu : référence CA impossible à enregistrer.");
        }

        HashSet<Guid> criterionIds = [];
        List<ProjectCriterionBaselineEntry> records = [];
        foreach (ProjectRequirementEntry requirement in catalog.Requirements)
        {
            if (requirement.CriterionDiagnostics.Count != 0)
            {
                throw new InvalidDataException(
                    "Critères CA ambigus dans " + requirement.Card.Reference +
                    " : corriger avant la référence.");
            }

            foreach (Nodalis.Core.Requirements.ProjectRequirementCriterion ca
                in requirement.Criteria)
            {
                if (ca.Id is not Guid id || id == Guid.Empty ||
                    !ca.LocallyIdentified || !criterionIds.Add(id) ||
                    records.Count >= MaxCriteria)
                {
                    throw new InvalidDataException(
                        "Identité CA répétée, absente ou capacité dépassée.");
                }

                records.Add(new ProjectCriterionBaselineEntry(id,
                    requirement.Card.Id, requirement.Card.Reference,
                    ca.Reference, ca.Title, requirement.RelativePath,
                    requirement.SourceSha256, Sha(ca.Title),
                    ca.Body is string body ? Sha(body) : null));
            }
        }

        return records.OrderBy(item => item.CriterionId).ToArray();
    }

    /// <summary>
    /// Compares a current EXI catalog to a previously human-approved
    /// observation, including disappeared CAs and changes made in any editor.
    /// </summary>
    /// <param name="catalog">Current canonical EXI catalog.</param>
    /// <param name="previous">Latest durable reference, or null.</param>
    /// <returns>Derived statuses only; never adjusts a business status.</returns>
    public static ProjectCriterionBaselineOverview Compare(
        ProjectRequirementCatalog catalog,
        ProjectCriterionBaselineSnapshot? previous)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        if (previous is not null && previous.ProjectId != catalog.ProjectId)
        {
            throw new InvalidDataException(
                "La référence CA appartient à un autre projet.");
        }

        List<string> diagnostics = [.. catalog.Diagnostics];
        Dictionary<Guid, ProjectCriterionBaselineEntry> current = [];
        HashSet<Guid> unsafeIds = [];
        foreach (ProjectRequirementEntry requirement in catalog.Requirements)
        {
            diagnostics.AddRange(requirement.CriterionDiagnostics.Select(item =>
                requirement.Card.Reference + " : " + item));
            foreach (Nodalis.Core.Requirements.ProjectRequirementCriterion ca
                in requirement.Criteria)
            {
                if (ca.Id is not Guid id || id == Guid.Empty)
                {
                    diagnostics.Add(requirement.Card.Reference +
                        " : CA sans GUID stable, impossible à suivre.");
                    continue;
                }

                ProjectCriterionBaselineEntry item = new(id,
                    requirement.Card.Id, requirement.Card.Reference,
                    ca.Reference, ca.Title, requirement.RelativePath,
                    requirement.SourceSha256, Sha(ca.Title),
                    ca.LocallyIdentified && ca.Body is string body
                        ? Sha(body) : null);
                if (!ca.LocallyIdentified || !current.TryAdd(id, item))
                {
                    unsafeIds.Add(id);
                }
            }
        }

        if (previous is null)
        {
            return new ProjectCriterionBaselineOverview(null,
                current.Values.OrderBy(item => item.RequirementReference)
                    .ThenBy(item => item.CriterionReference)
                    .Select(item => new ProjectCriterionBaselineRow(
                        item.CriterionId, item.RequirementId,
                        Label(item), ProjectCriterionBaselineState.NotTracked,
                        "Aucun instantané CA de référence encore enregistré."))
                    .ToArray(), diagnostics);
        }

        Dictionary<Guid, ProjectCriterionBaselineEntry> original =
            previous.Entries.ToDictionary(item => item.CriterionId);
        List<ProjectCriterionBaselineRow> rows = [];
        foreach (ProjectCriterionBaselineEntry item in current.Values
            .OrderBy(item => item.RequirementReference)
            .ThenBy(item => item.CriterionReference))
        {
            ProjectCriterionBaselineState state;
            string reason;
            if (unsafeIds.Contains(item.CriterionId) ||
                !IsSha(item.DocumentSha256) ||
                item.BodySha256 is null)
            {
                state = ProjectCriterionBaselineState.Unresolved;
                reason = "Source CA ambiguë ou corps Markdown non lisible.";
            }
            else if (!original.TryGetValue(item.CriterionId,
                out ProjectCriterionBaselineEntry? baseline))
            {
                state = ProjectCriterionBaselineState.Added;
                reason = "Nouveau GUID CA depuis la référence enregistrée.";
            }
            else if (baseline.BodySha256 is null ||
                !IsSha(baseline.BodySha256))
            {
                state = ProjectCriterionBaselineState.Unresolved;
                reason = "Ancien corps CA non comparable sans ambiguïté.";
            }
            else if (baseline.RequirementId != item.RequirementId ||
                baseline.TitleSha256 != item.TitleSha256 ||
                baseline.BodySha256 != item.BodySha256 ||
                !string.Equals(baseline.RelativePath, item.RelativePath,
                    StringComparison.OrdinalIgnoreCase))
            {
                state = ProjectCriterionBaselineState.Changed;
                reason = "Titre, contenu, parent EXI ou source CA modifié " +
                    "depuis l'observation humaine.";
            }
            else
            {
                state = ProjectCriterionBaselineState.Unchanged;
                reason = "Titre et corps CA identiques à l'instantané.";
            }

            rows.Add(new ProjectCriterionBaselineRow(item.CriterionId,
                item.RequirementId, Label(item), state, reason));
        }

        bool incompleteCatalog = catalog.Diagnostics.Count != 0 ||
            catalog.Requirements.Any(requirement =>
                requirement.CriterionDiagnostics.Count != 0);
        HashSet<Guid> readableRequirements = catalog.Requirements
            .Select(requirement => requirement.Card.Id).ToHashSet();
        foreach (ProjectCriterionBaselineEntry item in previous.Entries)
        {
            if (current.ContainsKey(item.CriterionId))
            {
                continue;
            }

            bool unknown = incompleteCatalog ||
                !readableRequirements.Contains(item.RequirementId);
            rows.Add(new ProjectCriterionBaselineRow(item.CriterionId,
                item.RequirementId, Label(item),
                unknown ? ProjectCriterionBaselineState.Unresolved :
                    ProjectCriterionBaselineState.Missing,
                unknown
                    ? "EXI absente, illisible ou registre incomplet : " +
                      "suppression du CA non démontrée."
                    : "GUID CA disparu de sa source depuis la référence."));
        }

        if (unsafeIds.Count > 0)
        {
            diagnostics.Add(unsafeIds.Count +
                " GUID CA en collision ou sans identité locale fiable.");
        }

        return new ProjectCriterionBaselineOverview(previous, rows,
            diagnostics);
    }

    /// <summary>Rejects malformed source fingerprints.</summary>
    /// <param name="hash">Expected SHA-256 hex.</param>
    /// <returns>True for uppercase/lowercase 64-digit hashes.</returns>
    public static bool IsSha(string? hash) =>
        hash is { Length: 64 } && hash.All(Uri.IsHexDigit);

    /// <summary>Hashes one non-secret UTF-8 field locally.</summary>
    /// <param name="value">Exact canonical text.</param>
    /// <returns>SHA-256 hex, no document body storage.</returns>
    public static string Sha(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    /// <summary>Human label only; never a matching identity.</summary>
    /// <param name="item">Stored CA reference.</param>
    /// <returns>EXI/CA caption.</returns>
    private static string Label(ProjectCriterionBaselineEntry item) =>
        item.RequirementReference + "/" + item.CriterionReference +
        " — " + item.Title;
}
