using Nodalis.Core.Importing;
using Nodalis.Core.Requirements;
using Nodalis.Infrastructure.Projects;

/// <summary>Pure contract tests for explicit human review of external AI fields.</summary>
internal static class ProjectAiImportDecisionSmokeTests
{
    /// <summary>Exercises partial decisions, conflicts and optimistic fingerprints.</summary>
    public static void Run()
    {
        Guid project = Guid.NewGuid();
        ProjectRequirementEntry first = Entry(
            project, "EXI-0001", "Titre local");
        ProjectRequirementEntry second = Entry(
            project, "EXI-0002", "Autre titre");
        ProjectAiImportCandidate one = Candidate(
            first.Card.Id, "EXI-0001", "Titre IA", "Titre local");
        ProjectAiImportCandidate two = Candidate(
            second.Card.Id, "EXI-0002", "Autre IA", null);
        ProjectAiImportCandidate unsupported = new()
        {
            ProposalId = Guid.NewGuid(),
            Kind = "risk",
            Operation = "create",
            Certainty = "explicit",
            ProposedFields = new Dictionary<string, string>
            {
                ["title"] = "Risque informatif"
            }
        };
        ProjectAiResponsePreview preview = new(
            new ProjectAiResponseEnvelope
            {
                Schema = "nodalis.import.v1",
                ResponseId = Guid.NewGuid(),
                Proposals = [one, two, unsupported]
            },
            new string('C', 64));
        ProjectRequirementCatalog catalog = new(
            [first, second], [], "EXI-0003");
        IReadOnlyList<ProjectAiRequirementReview> reviews =
            ProjectAiRequirementReviewService.Compare(preview, catalog);
        ProjectAiPreparedReviewPlan empty =
            ProjectAiImportDecisionPlanner.Prepare(preview, reviews, []);
        Assert(empty.Changes.Count == 0 && empty.PendingFields == 3,
            "No automatic accept must occur, regardless of AI certainty.");

        ProjectAiPreparedReviewPlan accepted =
            ProjectAiImportDecisionPlanner.Prepare(preview, reviews,
            [
                new ProjectAiFieldDecision(one.ProposalId, "title",
                    ProjectAiFieldDecisionKind.Accept),
                new ProjectAiFieldDecision(two.ProposalId, "title",
                    ProjectAiFieldDecisionKind.Review),
                new ProjectAiFieldDecision(unsupported.ProposalId, "title",
                    ProjectAiFieldDecisionKind.Ignore)
            ]);
        Assert(accepted.RawResponseSha256 == preview.RawSha256 &&
            accepted.Changes.Count == 1 &&
            accepted.Changes[0].ProjectId == project &&
            accepted.Changes[0].RequirementId == first.Card.Id &&
            accepted.Changes[0].ExpectedSha256 == first.SourceSha256 &&
            accepted.Changes[0].CurrentTitle == "Titre local" &&
            accepted.Changes[0].ReviewedTitle == "Titre IA" &&
            accepted.PendingFields == 1 &&
            accepted.IgnoredFields == 1,
            "Human choices must produce only the approved, snapshot-bound title.");

        Reject(() => ProjectAiImportDecisionPlanner.Prepare(preview, reviews,
        [
            new ProjectAiFieldDecision(two.ProposalId, "title",
                ProjectAiFieldDecisionKind.Accept)
        ]), "Unknown base requires a nonempty human reason.");
        ProjectAiPreparedReviewPlan rebased =
            ProjectAiImportDecisionPlanner.Prepare(preview, reviews,
            [
                new ProjectAiFieldDecision(two.ProposalId, "title",
                    ProjectAiFieldDecisionKind.Edit,
                    "Titre corrigé humainement", "Base absente vérifiée manuellement.")
            ]);
        Assert(rebased.Changes.Single().ReviewedTitle ==
            "Titre corrigé humainement", "Explicit edit must override the AI suggestion.");

        ProjectRequirementEntry concurrent = Entry(
            project, "EXI-0001", "Changement externe", first.Card.Id);
        ProjectRequirementCatalog changedCatalog = new(
            [concurrent, second], [], "EXI-0003");
        IReadOnlyList<ProjectAiRequirementReview> conflicted =
            ProjectAiRequirementReviewService.Compare(preview, changedCatalog);
        Reject(() => ProjectAiImportDecisionPlanner.Prepare(preview, conflicted,
        [
            new ProjectAiFieldDecision(one.ProposalId, "title",
                ProjectAiFieldDecisionKind.Accept)
        ]), "Concurrent changes must demand an explicit reason.");
        ProjectAiPreparedReviewPlan justified =
            ProjectAiImportDecisionPlanner.Prepare(preview, conflicted,
            [
                new ProjectAiFieldDecision(one.ProposalId, "title",
                    ProjectAiFieldDecisionKind.Accept, Reason:
                    "Modification locale revue par l'utilisateur.")
            ]);
        Assert(justified.Changes[0].CurrentTitle == "Changement externe",
            "A conflict can only target the actual reviewed local revision.");

        Reject(() => ProjectAiImportDecisionPlanner.Prepare(preview, reviews,
        [
            new ProjectAiFieldDecision(one.ProposalId, "title",
                ProjectAiFieldDecisionKind.Accept),
            new ProjectAiFieldDecision(one.ProposalId, "title",
                ProjectAiFieldDecisionKind.Ignore)
        ]), "Duplicate field decisions are forbidden.");
        Reject(() => ProjectAiImportDecisionPlanner.Prepare(preview, reviews,
        [
            new ProjectAiFieldDecision(one.ProposalId, "title",
                ProjectAiFieldDecisionKind.Edit, "Titre\nmalveillant")
        ]), "An edited title cannot contain line breaks.");
        Reject(() => ProjectAiImportDecisionPlanner.Prepare(preview, reviews,
        [
            new ProjectAiFieldDecision(unsupported.ProposalId, "title",
                ProjectAiFieldDecisionKind.Accept)
        ]), "Unsupported types cannot be accepted.");
        Reject(() => ProjectAiImportDecisionPlanner.Prepare(preview, reviews,
        [
            new ProjectAiFieldDecision(Guid.NewGuid(), "title",
                ProjectAiFieldDecisionKind.Accept)
        ]), "Unknown proposals cannot enter a plan.");
        Reject(() => ProjectAiImportDecisionPlanner.Prepare(preview, reviews,
        [
            new ProjectAiFieldDecision(one.ProposalId, "description",
                ProjectAiFieldDecisionKind.Accept)
        ]), "Invented fields cannot be introduced through the review UI.");

        ProjectAiImportCandidate descriptionOnly = one with
        {
            ProposalId = Guid.NewGuid(),
            ProposedFields = new Dictionary<string, string>
            {
                ["description"] = "Texte IA"
            },
            BaseFields = new Dictionary<string, string>
            {
                ["description"] = "Texte local"
            }
        };
        ProjectAiResponsePreview descriptionPreview = preview with
        {
            Document = preview.Document with
            {
                Proposals = [descriptionOnly]
            }
        };
        ProjectRequirementCatalog descriptionCatalog = new(
            [first with { Description = "Texte local" }, second], [],
            "EXI-0003");
        IReadOnlyList<ProjectAiRequirementReview> descriptionReviews =
            ProjectAiRequirementReviewService.Compare(
                descriptionPreview, descriptionCatalog);
        Assert(descriptionReviews.Single().Fields.Single().Field == "description",
            "The local description comparison must be visible for review.");
        Reject(() => ProjectAiImportDecisionPlanner.Prepare(
            descriptionPreview, descriptionReviews,
            [
                new ProjectAiFieldDecision(descriptionOnly.ProposalId,
                    "description", ProjectAiFieldDecisionKind.Accept)
            ]), "A read-only description comparison cannot authorize publication.");

        ProjectAiImportCandidate sameTarget = one with
        {
            ProposalId = Guid.NewGuid(),
            ProposedFields = new Dictionary<string, string>
            {
                ["title"] = "Second competing title"
            }
        };
        ProjectAiResponsePreview duplicatedTarget = preview with
        {
            Document = preview.Document with
            {
                Proposals = [one, sameTarget]
            }
        };
        IReadOnlyList<ProjectAiRequirementReview> duplicateReviews =
            ProjectAiRequirementReviewService.Compare(
                duplicatedTarget, catalog);
        Reject(() => ProjectAiImportDecisionPlanner.Prepare(
            duplicatedTarget, duplicateReviews,
            [
                new ProjectAiFieldDecision(one.ProposalId, "title",
                    ProjectAiFieldDecisionKind.Accept),
                new ProjectAiFieldDecision(sameTarget.ProposalId, "title",
                    ProjectAiFieldDecisionKind.Accept, Reason:
                    "Arbitrage requis.")
            ]), "A single canonical field cannot receive two approved proposals.");

        IReadOnlyList<ProjectAiRequirementReview> truncatedReviews =
            reviews.Take(1).ToArray();
        Reject(() => ProjectAiImportDecisionPlanner.Prepare(
            preview, truncatedReviews, []),
            "Incomplete or stale comparison must be rejected.");

        ProjectAiPreparedReviewPlan again =
            ProjectAiImportDecisionPlanner.Prepare(preview, reviews,
            [
                new ProjectAiFieldDecision(one.ProposalId, "title",
                    ProjectAiFieldDecisionKind.Accept)
            ]);
        Assert(again.Changes.Single() == accepted.Changes.Single(),
            "Repeated preparation must yield the same non-executable plan.");
    }

    /// <summary>Constructs a canonical requirement fixture without a file.</summary>
    private static ProjectRequirementEntry Entry(
        Guid projectId, string reference, string title, Guid? id = null)
    {
        return new ProjectRequirementEntry(
            new ProjectRequirementCard
            {
                Id = id ?? Guid.NewGuid(),
                ProjectId = projectId,
                Reference = reference,
                Title = title,
                CreatedUtc = DateTimeOffset.UtcNow
            },
            "Applications/Exemple/Projets/Fictif/Documentation/Exigences/" +
                reference + ".md",
            new string('A', 64));
    }

    /// <summary>Constructs a safe external update candidate.</summary>
    private static ProjectAiImportCandidate Candidate(
        Guid id, string reference, string proposed, string? previous) => new()
        {
            ProposalId = Guid.NewGuid(),
            Kind = "requirement",
            Operation = "update",
            Certainty = "derived",
            TargetId = id,
            CandidateReference = reference,
            ProposedFields = new Dictionary<string, string>
            {
                ["title"] = proposed
            },
            BaseFields = previous is null ? [] :
                new Dictionary<string, string>
                {
                    ["title"] = previous
                }
        };

    /// <summary>Asserts that invalid review decisions are rejected.</summary>
    private static void Reject(Action action, string message)
    {
        try
        {
            action();
        }
        catch (InvalidDataException)
        {
            return;
        }

        throw new InvalidOperationException(message);
    }

    /// <summary>Asserts one test invariant.</summary>
    private static void Assert(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
