using Nodalis.Core.Links;
using Nodalis.Core.Projects;
using Nodalis.Core.Relations;
using Nodalis.Core.Requirements;
using Nodalis.Core.Tests;
using Nodalis.Infrastructure.Projects;

/// <summary>
/// Pure EXI handover relation tests: stable GUID links, frozen REC outcomes,
/// human-linked incidents and explicit refusal of stale source snapshots.
/// </summary>
internal static class ProjectRequirementHandoverRelationReviewSmokeTests
{
    /// <summary>Runs isolated non-mutating synthetic relation scenarios.</summary>
    public static void Run()
    {
        const string project = "Applications/Synthetic/Projets/Example";
        const string sourcePath =
            project + "/Documentation/Exigences/EXI-0001.md";
        const string testPath = project + "/Tests/Cahier.md";
        const string campaignPath =
            project + "/Tests/Campagnes/REC-0001.md";
        string sha = new('A', 64);
        Guid ownerId = Guid.NewGuid();
        Guid exiId = Guid.NewGuid();
        Guid criterionId = Guid.NewGuid();
        Guid exiDocumentId = Guid.NewGuid();
        Guid testDocumentId = Guid.NewGuid();
        Guid testBlockId = Guid.NewGuid();
        Guid passageId = Guid.NewGuid();
        Guid resultId = Guid.NewGuid();
        string incidentId = "INC-" + Guid.NewGuid().ToString("D");
        DateTimeOffset utc = DateTimeOffset.UtcNow.AddDays(-1);

        ProjectRequirementCard card = new()
        {
            Id = exiId,
            ProjectId = ownerId,
            Reference = "EXI-0001",
            Title = "Requirement without identifying business data",
            CreatedUtc = utc
        };
        ProjectRequirementEntry requirement = new(
            card, sourcePath, sha)
        {
            Criteria =
            [
                new ProjectRequirementCriterion(
                    "CA-01", "Observable behavior", criterionId, 12, true)
            ]
        };
        ProjectRequirementCatalog requirements =
            new([requirement], [], "EXI-0002")
            {
                ProjectId = ownerId
            };
        ProjectHandoverIntent intent = new()
        {
            Id = Guid.NewGuid(),
            SourceProjectId = ownerId,
            TargetProjectId = Guid.NewGuid(),
            SourceObjectId = exiId,
            SourceObjectKind = "Exigences",
            SourceReference = "EXI-0001",
            SourceRelativePath = sourcePath,
            TargetProjectRelativePath =
                "Applications/Synthetic/Projets/Recipient",
            Method = ProjectHandoverMethod.LinkedSuccessor,
            ProposedTargetObjectId = Guid.NewGuid(),
            SourceFingerprint = sha,
            TargetFingerprint = new string('B', 64),
            Actor = "Synthetic reviewer",
            Rationale = "Requalification to be reviewed",
            RecordedUtc = utc
        };
        ProjectRequirementHandoverPreview preview = new()
        {
            Intent = intent,
            SourceRelativePath = sourcePath,
            SourceSha256 = sha
        };
        LinkTargetEntry requirementDocument = Document(
            exiDocumentId, sourcePath, sha);
        LinkTargetEntry testDocument = Document(testDocumentId, testPath,
            new string('C', 64));
        LinkIndexCatalog index = new()
        {
            Targets = [requirementDocument, testDocument],
            Blocks =
            [
                Block(criterionId, exiDocumentId,
                    "CA-01 — Observable behavior", 12),
                Block(testBlockId, testDocumentId,
                    "TEST-0001 — Scenario", 5)
            ],
            Relations =
            [
                new TypedRelationEntry
                {
                    SourceId = testDocumentId,
                    SourceBlockId = testBlockId,
                    TargetId = criterionId,
                    RawTargetId = criterionId.ToString("D"),
                    RelationType = "vérifie",
                    LineNumber = 8,
                    Excerpt = "relation vers CA synthétique"
                }
            ]
        };
        ProjectTestResultEvent outcome = new()
        {
            Id = resultId,
            TestId = testBlockId,
            Outcome = ProjectTestOutcome.Failed,
            Actor = "Synthetic executor",
            Rationale = "Historical failure in old context",
            RecordedUtc = utc.AddMinutes(5)
        };
        ProjectTestPassage passage = new()
        {
            Id = passageId,
            Number = 1,
            Environment = "Integration",
            ApplicationVersion = "1.0",
            StartedBy = "Synthetic reviewer",
            StartedUtc = utc,
            Tests =
            [
                new ProjectTestFrozenDefinition
                {
                    TestId = testBlockId,
                    Reference = "TEST-0001",
                    SourceRelativePath = testPath,
                    Markdown = "### TEST-0001 — Scenario",
                    Sha256 = new string('D', 64),
                    HeadingLine = 5
                }
            ],
            Results = [outcome]
        };
        ProjectTestCampaign campaign = new()
        {
            Id = Guid.NewGuid(),
            ProjectId = ownerId,
            Reference = "REC-0001",
            Title = "Synthetic qualification",
            CreatedUtc = utc.AddHours(-1),
            State = ProjectTestCampaignState.InProgress,
            Passages = [passage],
            IncidentLinks =
            [
                new ProjectTestIncidentLinkEvent
                {
                    Id = Guid.NewGuid(),
                    PassageId = passageId,
                    ResultId = resultId,
                    TestId = testBlockId,
                    IncidentId = incidentId,
                    Kind = ProjectTestIncidentLinkKind.Linked,
                    Actor = "Synthetic reviewer",
                    Rationale = "Linked to historical failure",
                    RecordedUtc = utc.AddMinutes(10)
                }
            ]
        };
        ProjectTestCampaignCatalog campaigns = new(
            [new ProjectTestCampaignEntry(campaign,
                campaignPath, new string('E', 64))], [], "REC-0002");

        ProjectRequirementRelationReview review =
            ProjectRequirementHandoverRelationReview.Build(
                preview, requirements, index, campaigns);
        string checklist = review.FormatChecklist();
        Assert(review.IsComplete &&
               review.Items.Count(item => item.Kind ==
                   ProjectRequirementRelationReviewKind.Criterion) == 1 &&
               review.Items.Count(item => item.Kind ==
                   ProjectRequirementRelationReviewKind.Test) == 1 &&
               review.Items.Count(item => item.Kind ==
                   ProjectRequirementRelationReviewKind.CampaignPassage) == 1 &&
               review.Items.Count(item => item.Kind ==
                   ProjectRequirementRelationReviewKind.Incident) == 1 &&
               review.Items.Count(item => item.Kind ==
                   ProjectRequirementRelationReviewKind.TypedRelation) == 1 &&
               checklist.Contains(incidentId, StringComparison.Ordinal) &&
               checklist.Contains("Failed", StringComparison.Ordinal) &&
               checklist.Contains("aucun résultat hérité",
                   StringComparison.OrdinalIgnoreCase) &&
               checklist.Contains(passageId.ToString("D"),
                   StringComparison.Ordinal),
            "A reviewed EXI B must surface links, REC context and INC " +
            "without assuming their results are inherited. " + checklist);
        Assert(ProjectRequirementHandoverRelationReview.Build(
            preview, requirements, index, campaigns).FormatChecklist() ==
            checklist, "The same immutable sources must give the same worklist.");

        ProjectRequirementRelationReview changed =
            ProjectRequirementHandoverRelationReview.Build(
                preview, requirements with
                {
                    Requirements =
                    [
                        requirement with
                        {
                            SourceSha256 = new string('F', 64)
                        }
                    ]
                }, index, campaigns);
        Assert(!changed.IsComplete && changed.Items.Count == 0,
            "A changed original SHA cannot reuse a link review.");

        ProjectRequirementRelationReview staleIndex =
            ProjectRequirementHandoverRelationReview.Build(
                preview, requirements, index with
                {
                    Targets =
                    [
                        requirementDocument with
                        {
                            ContentHash = new string('F', 64)
                        },
                        testDocument
                    ]
                }, campaigns);
        Assert(!staleIndex.IsComplete &&
               !staleIndex.Items.Any(item =>
                   item.Kind == ProjectRequirementRelationReviewKind.Test),
            "A stale EXI index cannot claim a confirmed test link.");
    }

    /// <summary>Creates an indexed document with a pinned content SHA.</summary>
    /// <param name="id">Existing document ID.</param>
    /// <param name="path">Workspace-relative source.</param>
    /// <param name="sha">Exact SHA-256 string.</param>
    /// <returns>Typed document index entry.</returns>
    private static LinkTargetEntry Document(
        Guid id, string path, string sha) => new()
    {
        Id = id, Kind = LinkTargetKind.Document, RelativePath = path,
        QualifiedName = path, DisplayName = "Synthetic", ContentHash = sha
    };

    /// <summary>Creates an identifiable indexed Markdown heading.</summary>
    /// <param name="id">Stable Markdown block ID.</param>
    /// <param name="documentId">Owning indexed document.</param>
    /// <param name="title">Human-readable heading.</param>
    /// <param name="line">One-based source line.</param>
    /// <returns>Unique indexed heading.</returns>
    private static IndexedMarkdownBlock Block(
        Guid id, Guid documentId, string title, int line) => new()
    {
        Id = id, DocumentId = documentId, HeadingTitle = title,
        HeadingLine = line, HeadingLevel = 3, HeadingOffset = line * 12
    };

    /// <summary>Stops on any incorrect immutable audit assertion.</summary>
    /// <param name="condition">Expected read-only fact.</param>
    /// <param name="message">Meaningful failure.</param>
    private static void Assert(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
