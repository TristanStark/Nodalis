using System.Security.Cryptography;
using System.Text;
using Nodalis.Core.Markdown;
using Nodalis.Core.Tests;

/// <summary>Deterministic large-cahier test identity and block snapshot checks.</summary>
internal static class ProjectTestDefinitionSmokeTests
{
    /// <summary>Tests 180 blocks, nested sections and conservative identity.</summary>
    public static void Run()
    {
        StringBuilder cahier = new("# Cahier de recette fictif\n");
        List<Guid> identifiers = [];
        for (int index = 1; index <= 180; index++)
        {
            Guid id = Guid.NewGuid();
            identifiers.Add(id);
            cahier.Append("## TEST-")
                .Append(index.ToString("D4"))
                .Append(" — Scénario ").Append(index).Append('\n')
                .Append(MarkdownBlockIdentityCodec.FormatMarker(id))
                .Append('\n')
                .Append("### Étapes\n- Exécuter une action fictive\n")
                .Append("### Attendu\n- Résultat ").Append(index)
                .Append("\n");
        }

        cahier.Append("## Autres informations\n");
        cahier.Append("~~~md\n## TEST-0999 — Faux test\n~~~\n");
        string original = cahier.ToString();
        ProjectTestDefinitionScan scan =
            ProjectTestDefinitionCodec.Inspect(original);
        Check(scan.Tests.Count == 180 &&
            scan.Tests.All(test => test.CanFreeze) &&
            scan.Tests[0].Id == identifiers[0] &&
            scan.Tests[^1].Id == identifiers[^1] &&
            scan.Tests[0].Markdown.Contains("### Attendu",
                StringComparison.Ordinal) &&
            !scan.Tests[0].Markdown.Contains("TEST-0002",
                StringComparison.Ordinal) &&
            !scan.Tests[^1].Markdown.Contains("TEST-0999",
                StringComparison.Ordinal) &&
            scan.Tests[0].Sha256 == Convert.ToHexString(
                SHA256.HashData(Encoding.UTF8.GetBytes(
                    scan.Tests[0].Markdown))),
            "180 individually identified tests must have exact bounded snapshots.");

        string changed = original.Replace(
            "Résultat 2\n", "Résultat modifié 2\n",
            StringComparison.Ordinal);
        ProjectTestDefinitionScan revised =
            ProjectTestDefinitionCodec.Inspect(changed);
        Check(scan.Tests[0].Sha256 == revised.Tests[0].Sha256 &&
            scan.Tests[1].Sha256 != revised.Tests[1].Sha256 &&
            scan.Tests[2].Sha256 == revised.Tests[2].Sha256,
            "Editing one test may not invalidate the other 179 snapshots.");

        string renamed = original.Replace(
            "TEST-0001 — Scénario 1",
            "TEST-0001 — Scénario renommé",
            StringComparison.Ordinal);
        ProjectTestDefinitionScan afterRename =
            ProjectTestDefinitionCodec.Inspect(renamed);
        Check(afterRename.Tests[0].Id == identifiers[0] &&
            afterRename.Tests[0].Sha256 != scan.Tests[0].Sha256,
            "A renamed title preserves GUID but changes content version.");

        string duplicateId = original.Replace(
            MarkdownBlockIdentityCodec.FormatMarker(identifiers[1]),
            MarkdownBlockIdentityCodec.FormatMarker(identifiers[0]),
            StringComparison.Ordinal);
        ProjectTestDefinitionScan duplicateIds =
            ProjectTestDefinitionCodec.Inspect(duplicateId);
        Check(!duplicateIds.Tests[0].CanFreeze &&
            !duplicateIds.Tests[1].CanFreeze &&
            duplicateIds.Tests[2].CanFreeze,
            "Both occurrences of a repeated GUID must be ineligible.");

        string duplicateCode = original.Replace(
            "## TEST-0002 — Scénario 2",
            "## TEST-0001 — Scénario 2",
            StringComparison.Ordinal);
        ProjectTestDefinitionScan duplicateCodes =
            ProjectTestDefinitionCodec.Inspect(duplicateCode);
        Check(!duplicateCodes.Tests[0].CanFreeze &&
            !duplicateCodes.Tests[1].CanFreeze,
            "A repeated readable reference must be diagnosed.");

        string missing = original.Replace(
            MarkdownBlockIdentityCodec.FormatMarker(identifiers[0]),
            "<!-- nodalis:block:invalid -->",
            StringComparison.Ordinal);
        ProjectTestDefinitionScan invalid =
            ProjectTestDefinitionCodec.Inspect(missing);
        Check(!invalid.Tests[0].CanFreeze &&
            invalid.Tests[0].Id is null,
            "Malformed markers may not produce an executable snapshot.");

        Check(ProjectTestDefinitionCodec.IsReference("TEST-0001") &&
            ProjectTestDefinitionCodec.IsReference("TEST-001") &&
            ProjectTestDefinitionCodec.IsReference("TST-123") &&
            !ProjectTestDefinitionCodec.IsReference("TEST-0000") &&
            !ProjectTestDefinitionCodec.IsReference("TEST-00001") &&
            !ProjectTestDefinitionCodec.IsReference("test-0001"),
            "References must be canonical positive TEST-/TST- codes.");

        Check(original == cahier.ToString(),
            "The inspection must never mutate the original source.");
    }

    /// <summary>Checks invariants using synthetic failure descriptions.</summary>
    /// <param name="condition">Required condition.</param>
    /// <param name="message">Failure text with no workspace data.</param>
    private static void Check(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
