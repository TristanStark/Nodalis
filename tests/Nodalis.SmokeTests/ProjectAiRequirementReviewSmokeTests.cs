using Nodalis.Core.Importing;
using Nodalis.Core.Requirements;
using Nodalis.Infrastructure.Projects;

/// <summary>
/// Regression checks for conservative, project-scoped identity resolution.
/// No filesystem writes or AI service are involved.
/// </summary>
internal static class ProjectAiRequirementReviewSmokeTests
{
    /// <summary>Tests GUID precedence, exact references, collisions and three-way diffs.</summary>
    public static void Run()
    {
        ProjectRequirementEntry first = Requirement(
            Guid.NewGuid(), "EXI-0001", "Titre local modifié");
        ProjectRequirementEntry second = Requirement(
            Guid.NewGuid(), "EXI-0002", "Exigence distincte");
        ProjectRequirementCatalog catalog = new(
            [first, second], [], "EXI-0003");

        ProjectAiImportCandidate byGuid = Candidate(
            "update", first.Card.Id, "EXI-0001", "Titre proposé",
            "Titre à la préparation");
        ProjectAiImportCandidate byRef = Candidate(
            "update", null, "EXI-0002", "Nouveau titre",
            "Exigence distincte");
        ProjectAiImportCandidate noBase = Candidate(
            "update", second.Card.Id, null, "Nouveau titre", null);
        ProjectAiImportCandidate conflicting = Candidate(
            "update", first.Card.Id, "EXI-0002", "Nouveau titre", null);
        ProjectAiImportCandidate unknownId = Candidate(
            "update", Guid.NewGuid(), "EXI-0002", "Nouveau titre", null);
        ProjectAiImportCandidate titleOnly = Candidate(
            "update", null, null, "Exigence distincte", null);
        ProjectAiImportCandidate createCollision = Candidate(
            "create", null, "EXI-0001", "Exigence proposée", null);
        ProjectAiImportCandidate createIdentity = Candidate(
            "create", Guid.NewGuid(), null, "Exigence proposée", null);
        ProjectAiImportCandidate newDraft = Candidate(
            "create", null, null, "Exigence nouvelle", null);
        ProjectAiImportCandidate unsupported = Candidate(
            "link", first.Card.Id, "EXI-0001", "Inutilisé", null);
        ProjectAiImportCandidate unknownField = Candidate(
            "update", first.Card.Id, "EXI-0001", "Titre local modifié", null)
            with
            {
                ProposedFields = new Dictionary<string, string>
                {
                    ["title"] = "Titre local modifié",
                    ["description"] = "Texte IA non lisible comme champ canonique"
                }
            };
        ProjectAiResponsePreview preview = Preview(
            byGuid, byRef, noBase, conflicting, unknownId, titleOnly,
            createCollision, createIdentity, newDraft, unsupported,
            unknownField);
        IReadOnlyList<ProjectAiRequirementReview> reviews =
            ProjectAiRequirementReviewService.Compare(preview, catalog);
        Assert(reviews.Count == 11, "Every proposal must remain independently reviewable.");

        Assert(reviews[0].Status == ProjectAiRequirementMatchStatus.GuidMatch &&
            reviews[0].Requirement?.Card.Id == first.Card.Id &&
            reviews[0].Fields.Single().Kind ==
                ProjectAiFieldDifferenceKind.ConcurrentConflict,
            "A stable GUID must bind the canonical current value and reveal conflicts.");
        Assert(reviews[1].Status == ProjectAiRequirementMatchStatus.ReferenceMatch &&
            reviews[1].Requirement?.Card.Id == second.Card.Id &&
            reviews[1].Fields.Single().Kind ==
                ProjectAiFieldDifferenceKind.CandidateChange,
            "An exact EXI reference may be displayed but never automatically committed.");
        Assert(reviews[2].Fields.Single().Kind ==
                ProjectAiFieldDifferenceKind.UnknownBase,
            "An absent prompt baseline must not imply permission to overwrite.");
        Assert(reviews[3].Status ==
                ProjectAiRequirementMatchStatus.ConflictingHints &&
            reviews[3].Requirement is null,
            "Contradictory GUID and EXI reference must block matching.");
        Assert(reviews[4].Status == ProjectAiRequirementMatchStatus.NotFound &&
            reviews[4].Requirement is null,
            "An unknown GUID cannot silently fall back to a matching reference.");
        Assert(reviews[5].Status == ProjectAiRequirementMatchStatus.NotFound,
            "A title cannot be used as an official identity.");
        Assert(reviews[6].Status == ProjectAiRequirementMatchStatus.CreateCollision &&
            reviews[7].Status == ProjectAiRequirementMatchStatus.CreateCollision,
            "An IA create cannot reuse an EXI or assign an official GUID.");
        Assert(reviews[8].Status == ProjectAiRequirementMatchStatus.CreationDraft &&
            reviews[8].Requirement is null,
            "A new create is only an uncommitted draft.");
        Assert(reviews[9].Status == ProjectAiRequirementMatchStatus.Unsupported,
            "Link decisions require a separate, explicitly qualified resolver.");
        Assert(reviews[10].Fields.Count == 1 &&
            reviews[10].Warnings.Any(item => item.Contains("description",
                StringComparison.Ordinal)),
            "Unsupported fields must never receive a fabricated local diff.");

        ProjectRequirementCatalog duplicates = new(
            [first, first], ["GUID dupliqué."], null);
        ProjectAiRequirementReview duplicate =
            ProjectAiRequirementReviewService.Compare(
                Preview(byGuid), duplicates).Single();
        Assert(duplicate.Status == ProjectAiRequirementMatchStatus.Ambiguous &&
            duplicate.Requirement is null,
            "Duplicate canonical identities must never be silently selected.");

        IReadOnlyList<ProjectAiRequirementReview> repeated =
            ProjectAiRequirementReviewService.Compare(preview, catalog);
        Assert(reviews.Select(item => item.Status)
                .SequenceEqual(repeated.Select(item => item.Status)),
            "Repeated read-only review must be deterministic.");
    }

    /// <summary>Creates a synthetic, project-owned canonical catalog entry.</summary>
    /// <param name="id">Stable persisted identity.</param>
    /// <param name="reference">Exact persisted EXI reference.</param>
    /// <param name="title">Current canonical title.</param>
    /// <returns>Project-local test snapshot.</returns>
    private static ProjectRequirementEntry Requirement(
        Guid id, string reference, string title) => new(
        new ProjectRequirementCard
        {
            Id = id,
            ProjectId = Guid.Parse("aaaaaaaa-aaaa-4aaa-aaaa-aaaaaaaaaaaa"),
            Reference = reference,
            Title = title,
            CreatedUtc = new DateTimeOffset(2026, 11, 3, 0, 0, 0,
                TimeSpan.Zero)
        },
        "Applications/Exemple/Projets/Fictif/Documentation/Exigences/" +
            reference + ".md",
        new string('A', 64));

    /// <summary>Creates an untrusted, bounded single-title AI proposal.</summary>
    /// <param name="operation">Proposed action.</param>
    /// <param name="id">Existing object identity hint.</param>
    /// <param name="reference">Optional exact EXI hint.</param>
    /// <param name="title">Untrusted proposed title.</param>
    /// <param name="baseline">Optional declared prompt-time title.</param>
    /// <returns>Unapproved proposal.</returns>
    private static ProjectAiImportCandidate Candidate(string operation,
        Guid? id, string? reference, string title, string? baseline) => new()
        {
            ProposalId = Guid.NewGuid(),
            Kind = "requirement",
            Operation = operation,
            Certainty = "derived",
            TargetId = id,
            CandidateReference = reference,
            ProposedFields = new Dictionary<string, string>
            {
                ["title"] = title
            },
            BaseFields = baseline is null
                ? []
                : new Dictionary<string, string>
                {
                    ["title"] = baseline
                }
        };

    /// <summary>Builds a parser-equivalent preview without disk or network.</summary>
    /// <param name="candidates">Distinct validated proposals.</param>
    /// <returns>Read-only envelope.</returns>
    private static ProjectAiResponsePreview Preview(
        params ProjectAiImportCandidate[] candidates) => new(
        new ProjectAiResponseEnvelope
        {
            Schema = "nodalis.import.v1",
            ResponseId = Guid.NewGuid(),
            Proposals = candidates.ToList()
        },
        new string('B', 64));

    /// <summary>Rejects a failed regression invariant.</summary>
    /// <param name="condition">Expected truth value.</param>
    /// <param name="message">Failure details.</param>
    private static void Assert(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
