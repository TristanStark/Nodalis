using System.Text.Json;
using Nodalis.Core.Importing;

/// <summary>
/// Read-only validation of untrusted external AI proposal envelopes.
/// </summary>
internal static class ProjectAiResponseImportSmokeTests
{
    /// <summary>Tests format, limits, idempotency keys and three-way conflicts.</summary>
    public static void Run()
    {
        Guid responseId = Guid.NewGuid();
        Guid proposalId = Guid.NewGuid();
        string raw = JsonSerializer.Serialize(new
        {
            schema = "nodalis.import.v1",
            responseId,
            promptKey = "frt100-01",
            proposals = new[]
            {
                new
                {
                    proposalId,
                    kind = "requirement",
                    operation = "update",
                    targetId = (Guid?)null,
                    candidateReference = "SOURCE-FICTIVE",
                    sourcePath = "Applications/Exemple/Projets/Fictif/Notes.md",
                    certainty = "derived",
                    baseFields = new { title = "Avant" },
                    proposedFields = new
                    {
                        title = "Après",
                        description = "Proposition fictive à valider"
                    }
                }
            }
        });
        ProjectAiResponsePreview preview = ProjectAiResponseImport.Parse(raw);
        Assert(preview.Document.Proposals.Count == 1 &&
            preview.Document.Proposals[0].ProposalId == proposalId &&
            ProjectAiResponseImport.Parse(raw).RawSha256 == preview.RawSha256,
            "Same untrusted response must preserve stable identifiers and hash.");

        ProjectAiResponsePreview fenced =
            ProjectAiResponseImport.Parse("```json\n" + raw + "\n```");
        Assert(fenced.Document.ResponseId == responseId,
            "One explicit JSON fence may be reviewed without execution.");

        IReadOnlyList<ProjectAiFieldDifference> diff =
            ProjectAiThreeWayDiff.Compare(
                new Dictionary<string, string>
                {
                    ["title"] = "Avant",
                    ["description"] = "Description de base"
                },
                new Dictionary<string, string>
                {
                    ["title"] = "Modification locale",
                    ["description"] = "Description de base"
                },
                new Dictionary<string, string>
                {
                    ["title"] = "Proposition IA",
                    ["description"] = "Description proposée",
                    ["sourceExcerpt"] = "À vérifier"
                });
        Assert(diff.Count == 3 &&
            diff.Single(row => row.Field == "title").Kind ==
                ProjectAiFieldDifferenceKind.ConcurrentConflict &&
            diff.Single(row => row.Field == "description").Kind ==
                ProjectAiFieldDifferenceKind.CandidateChange &&
            diff.Single(row => row.Field == "sourceExcerpt").Kind ==
                ProjectAiFieldDifferenceKind.UnknownBase,
            "Unrecorded base and concurrent changes must not be overwritten.");

        string duplicate = raw.Replace(
            "\"schema\":\"nodalis.import.v1\"",
            "\"schema\":\"nodalis.import.v1\",\"schema\":\"nodalis.import.v1\"",
            StringComparison.Ordinal);
        Reject(() => ProjectAiResponseImport.Parse(duplicate),
            "Duplicate JSON keys must be rejected.");
        Reject(() => ProjectAiResponseImport.Parse(raw.Replace(
            "nodalis.import.v1", "nodalis.import.v2",
            StringComparison.Ordinal)),
            "Unknown schema must fail closed.");
        Reject(() => ProjectAiResponseImport.Parse(raw.Replace(
            "\"operation\":\"update\"",
            "\"operation\":\"delete\"",
            StringComparison.Ordinal)),
            "AI never proposes delete commands.");
        Reject(() => ProjectAiResponseImport.Parse(raw.Replace(
            "\"sourcePath\":\"Applications/Exemple/Projets/Fictif/Notes.md\"",
            "\"sourcePath\":\"../../secret.md\"",
            StringComparison.Ordinal)),
            "Traversal cannot enter candidate provenance.");
        Reject(() => ProjectAiResponseImport.Parse(raw.Replace(
            "\"title\":\"Avant\"",
            "\"status\":\"Approved\"",
            StringComparison.Ordinal)),
            "Status/approval fields are prohibited even in untrusted JSON.");

        ProjectAiImportCandidate exemplar = preview.Document.Proposals[0];
        Reject(() => ProjectAiResponseImport.Validate(
            preview.Document with
            {
                Proposals = Enumerable.Repeat(exemplar, 2).ToList()
            }),
            "Duplicate proposal identities must be diagnosed.");
        Reject(() => ProjectAiResponseImport.Validate(
            preview.Document with
            {
                Proposals = Enumerable.Repeat(exemplar with
                {
                    ProposalId = Guid.NewGuid()
                }, 101).ToList()
            }),
            "More than 100 proposals must be refused before UI rendering.");
    }

    /// <summary>Expects a rejected untrusted response.</summary>
    /// <param name="action">Parsing or validation action.</param>
    /// <param name="why">Regression message.</param>
    private static void Reject(Action action, string why)
    {
        try
        {
            action();
        }
        catch (InvalidDataException)
        {
            return;
        }

        throw new InvalidOperationException(why);
    }

    /// <summary>Asserts one synthetic contract invariant.</summary>
    /// <param name="condition">Expected outcome.</param>
    /// <param name="message">Failure details.</param>
    private static void Assert(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
