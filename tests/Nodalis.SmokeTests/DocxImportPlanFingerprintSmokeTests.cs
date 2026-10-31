using Nodalis.Core.Importing;

internal static class DocxImportPlanFingerprintSmokeTests
{
    /// <summary>Ensures a re-ordered or externally changed dry-run cannot silently reuse approval.</summary>
    public static void Run()
    {
        DocxImportPlan original = new DocxImportPlan
        {
            TargetProjectDisplayName = "Application / Projet",
            TargetProjectRelativePath = "Apps/Projet",
            ProjectedContentSha256 = "CONTENT-A",
            Changes =
            [
                new DocxImportPlannedChange
                {
                    Action = "Créer",
                    RelativePath = "Apps/Projet/Technique/import.md",
                    Description = "Markdown"
                },
                new DocxImportPlannedChange
                {
                    Action = "Créer",
                    RelativePath = "Apps/Projet/Media-Docx/a.png",
                    Description = "Pièce jointe"
                }
            ]
        };

        string first = DocxImportPlanFingerprint.Compute(original);
        if (first.Length != 64 ||
            DocxImportPlanFingerprint.Compute(original with { Fingerprint = first }) != first ||
            DocxImportPlanFingerprint.Compute(original with { ProjectedContentSha256 = "CONTENT-B" }) == first ||
            DocxImportPlanFingerprint.Compute(original with { MappingProfileSha256 = "PROFILE-UPDATED" }) == first ||
            DocxImportPlanFingerprint.Compute(original with
            {
                Changes = original.Changes.AsEnumerable().Reverse().ToList()
            }) == first ||
            DocxImportPlanFingerprint.Compute(original with
            {
                Changes =
                [
                    original.Changes[0] with { RelativePath = "Apps/Projet/Technique/import-2.md" },
                    original.Changes[1]
                ]
            }) == first)
        {
            throw new InvalidOperationException(
                "Le plan DOCX approuvé doit détecter contenu, ordre et destinations modifiés.");
        }
    }
}
