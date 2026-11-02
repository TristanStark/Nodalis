using Nodalis.Core.Markdown;
using Nodalis.Core.Requirements;

/// <summary>
/// Synthetic acceptance-criterion tests: stable block IDs, duplicate CA codes,
/// missing markers and fenced-code exclusions without modifying the Markdown.
/// </summary>
internal static class ProjectRequirementCriterionSmokeTests
{
    /// <summary>Checks local CA inventory and deliberately conservative identity.</summary>
    public static void Run()
    {
        Guid firstId = Guid.NewGuid();
        Guid duplicateId = Guid.NewGuid();
        string original = "# EXI-0001 — Fiche fictive\n" +
            "## Contexte\n" +
            "### CA-90 — Hors section\n" +
            "## Critères d'acceptation\n" +
            "### CA-01 — Afficher un résultat\n" +
            MarkdownBlockIdentityCodec.FormatMarker(firstId) + "\n" +
            "### CA-02 — Exporter un fichier\n" +
            "~~~md\n" +
            "### CA-99 — Faux critère dans du code\n" +
            "~~~\n" +
            "## Autre section\n" +
            "### CA-77 — Après la section\n";
        ProjectRequirementCriterionScan scan =
            ProjectRequirementCriterionCodec.Inspect(original);

        Assert(scan.Criteria.Count == 2 &&
               scan.Criteria[0].Reference == "CA-01" &&
               scan.Criteria[0].Id == firstId &&
               scan.Criteria[0].LocallyIdentified &&
               scan.Criteria[0].HeadingLine == 5 &&
               scan.Criteria[1].Reference == "CA-02" &&
               scan.Criteria[1].Id is null &&
               !scan.Criteria[1].LocallyIdentified &&
               scan.Diagnostics.Any(item => item.Contains("sans GUID",
                   StringComparison.Ordinal)),
            "Only the acceptance section may expose locally identified CA blocks.");

        string renamed = original.Replace(
            "CA-01 — Afficher un résultat",
            "CA-01 — Afficher un résultat renommé",
            StringComparison.Ordinal);
        ProjectRequirementCriterionScan afterRename =
            ProjectRequirementCriterionCodec.Inspect(renamed);
        Assert(afterRename.Criteria[0].Id == firstId &&
               afterRename.Criteria[0].Title == "Afficher un résultat renommé",
            "Renaming a criterion must preserve its marker identity.");

        string repeatedCodes = original.Replace(
            "CA-02 — Exporter un fichier",
            "CA-01 — Exporter un fichier",
            StringComparison.Ordinal);
        ProjectRequirementCriterionScan repeated =
            ProjectRequirementCriterionCodec.Inspect(repeatedCodes);
        Assert(repeated.Criteria.Count == 2 &&
               repeated.Criteria.All(item => !item.LocallyIdentified) &&
               repeated.Diagnostics.Any(item => item.Contains(
                   "dupliquée", StringComparison.Ordinal)),
            "Repeated local CA codes must invalidate ambiguous candidates.");

        string reusedId = original.Replace(
            "### CA-02 — Exporter un fichier\n",
            "### CA-02 — Exporter un fichier\n" +
                MarkdownBlockIdentityCodec.FormatMarker(firstId) + "\n",
            StringComparison.Ordinal);
        ProjectRequirementCriterionScan collisions =
            ProjectRequirementCriterionCodec.Inspect(reusedId);
        Assert(collisions.Criteria.Count == 2 &&
               collisions.Criteria.All(item => !item.LocallyIdentified),
            "Two criteria cannot reuse the same block marker.");

        string malformed = original.Replace(
            MarkdownBlockIdentityCodec.FormatMarker(firstId),
            "<!-- nodalis:block:invalid-guid -->",
            StringComparison.Ordinal);
        ProjectRequirementCriterionScan damaged =
            ProjectRequirementCriterionCodec.Inspect(malformed);
        Assert(damaged.Criteria[0].Id is null &&
               !damaged.Criteria[0].LocallyIdentified,
            "Malformed IDs cannot be published as stable links.");

        Assert(ProjectRequirementCriterionCodec.IsReference("CA-01") &&
               ProjectRequirementCriterionCodec.IsReference("CA-99") &&
               !ProjectRequirementCriterionCodec.IsReference("CA-00") &&
               !ProjectRequirementCriterionCodec.IsReference("CA-1") &&
               !ProjectRequirementCriterionCodec.IsReference("ca-01"),
            "Local criterion references require two nonzero canonical digits.");

        string fencedOnly = "## Critères d'acceptation\n" +
            "~~~markdown\n" +
            "### CA-01 — Fausse entrée\n" +
            MarkdownBlockIdentityCodec.FormatMarker(duplicateId) + "\n" +
            "~~~\n";
        Assert(ProjectRequirementCriterionCodec.Inspect(fencedOnly)
                   .Criteria.Count == 0,
            "Code fences cannot declare real acceptance criteria.");

        Assert(ProjectRequirementCriterionCodec.Inspect(
                   "## Critères d'acceptation\n").Criteria.Count == 0,
            "An empty section must not invent fake acceptance criteria.");
    }

    /// <summary>Reports only fixed synthetic diagnostics.</summary>
    /// <param name="condition">Expected invariant.</param>
    /// <param name="message">Synthetic failure.</param>
    private static void Assert(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
