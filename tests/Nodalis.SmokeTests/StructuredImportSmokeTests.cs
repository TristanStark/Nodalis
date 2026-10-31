using System.Text.Json;
using Nodalis.Core.Importing;
using Nodalis.Infrastructure.Importing;

internal static class StructuredImportSmokeTests
{
    /// <summary>
    /// Validates format-neutral planning, nested selections and path hardening without workspace writes.
    /// </summary>
    public static void Run()
    {
        ParsedDocxDocument word = new ParsedDocxDocument
        {
            Blocks =
            [
                Paragraph("Introduction", 1),
                Paragraph("Présentation générale"),
                Paragraph("Tests", 1),
                Paragraph("Cas de test"),
                Paragraph("Section à ignorer", 2),
                Paragraph("Non retenu"),
                Paragraph("Recette", 1),
                Paragraph("PV de recette")
            ]
        };

        ImportDocumentTree docx = DocxStructuredImportReader.FromParsed(
            word,
            "import.docx",
            new string('A', 64));

        Assert(docx.Roots.Count == 3, "The DOCX tree must preserve three top-level sections.");
        Assert(docx.Roots[1].Children[1].Children.Count == 1,
            "A nested title must own its paragraph.");
        Assert(docx.Roots[0].SourceOrder == 0 &&
               docx.Roots[1].SourceOrder == 2 &&
               docx.Roots[2].SourceOrder == 6,
            "Source order must be stable across nested sections.");

        ImportSelectionRule[] rules =
        [
            new ImportSelectionRule
            {
                SourceId = docx.Roots[0].SourceId,
                Include = true,
                DestinationRelativePath = "Projets/Demo/Presentation.md"
            },
            new ImportSelectionRule
            {
                SourceId = docx.Roots[1].SourceId,
                Include = true,
                DestinationRelativePath = "Projets/Demo/Tests.md"
            },
            new ImportSelectionRule
            {
                SourceId = docx.Roots[1].Children[1].SourceId,
                Include = false
            },
            new ImportSelectionRule
            {
                SourceId = docx.Roots[2].SourceId,
                Include = true,
                DestinationRelativePath = "Projets/Demo/Recette.md"
            }
        ];

        ImportDocumentPlan plan = ImportDocumentPlanBuilder.Prepare(docx, rules);
        Assert(plan.Fragments.Count == 6,
            "Only explicitly retained fragments may appear in the dry run.");
        Assert(!plan.Fragments.Any(fragment =>
                fragment.Text.Contains("Non retenu", StringComparison.Ordinal)),
            "An excluded nested subtree must never appear in the plan.");
        Assert(plan.Fragments.Any(fragment =>
                fragment.Text == "PV de recette" &&
                fragment.DestinationRelativePath.EndsWith("Recette.md", StringComparison.Ordinal)),
            "Destination inheritance must follow the selected parent.");
        Assert(plan.Fragments.Select(fragment => fragment.SourceOrder)
                .SequenceEqual(plan.Fragments.Select(fragment => fragment.SourceOrder).Order()),
            "Plan fragments must retain their source ordering.");

        ImportDocumentPlan again = ImportDocumentPlanBuilder.Prepare(docx, rules);
        Assert(JsonSerializer.Serialize(plan) == JsonSerializer.Serialize(again),
            "The same source and selection must yield the same serialized plan.");

        IImportDocumentReader alternative = new FakeImportReader();
        ImportDocumentTree fake = alternative.ReadAsync("unused").GetAwaiter().GetResult();
        ImportDocumentPlan fakePlan = ImportDocumentPlanBuilder.Prepare(
            fake,
            [
                new ImportSelectionRule
                {
                    SourceId = fake.Roots[0].SourceId,
                    Include = true,
                    DestinationRelativePath = "Projets/Demo/Technique.md"
                }
            ]);
        Assert(fakePlan.FormatId == "synthetic" && fakePlan.Fragments.Count == 1,
            "An independent reader must use the same plan builder.");

        foreach (string invalid in new[]
        {
            "../secret.md", "C:/outside.md", "/absolute.md",
            "Projets/CON.md", "Dossier//fichier.md",
            "Dossier/../fichier.md", "note.txt", "folder/file.md:stream"
        })
        {
            bool rejected = false;
            try
            {
                ImportDocumentPlanBuilder.ValidateDestination(invalid);
            }
            catch (InvalidDataException)
            {
                rejected = true;
            }

            Assert(rejected, "Unsafe import destination was allowed: " + invalid);
        }

        bool unknownRejected = false;
        try
        {
            ImportDocumentPlanBuilder.Prepare(
                docx,
                [
                    new ImportSelectionRule
                    {
                        SourceId = "missing",
                        Include = true,
                        DestinationRelativePath = "Projets/Demo/Tests.md"
                    }
                ]);
        }
        catch (InvalidDataException)
        {
            unknownRejected = true;
        }

        Assert(unknownRejected, "An unknown source identity must prevent a commit plan.");
    }

    /// <summary>
    /// Builds a parsed DOCX paragraph without interacting with a filesystem.
    /// </summary>
    private static DocxBlock Paragraph(string text, int? level = null)
    {
        return new DocxBlock
        {
            Kind = DocxBlockKind.Paragraph,
            Paragraph = new DocxParagraph
            {
                Text = text,
                HeadingLevel = level
            }
        };
    }

    /// <summary>
    /// Throws when a smoke assertion fails.
    /// </summary>
    private static void Assert(bool success, string message)
    {
        if (!success)
        {
            throw new InvalidOperationException(message);
        }
    }

    private sealed class FakeImportReader : IImportDocumentReader
    {
        public string FormatId => "synthetic";

        /// <inheritdoc />
        public Task<ImportDocumentTree> ReadAsync(
            string sourcePath,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new ImportDocumentTree
            {
                FormatId = FormatId,
                SourceFileName = "sample.fixture",
                SourceSha256 = new string('B', 64),
                Roots =
                [
                    new ImportDocumentNode
                    {
                        SourceId = "fake-b1",
                        Kind = ImportDocumentNodeKind.Table,
                        SourceOrder = 0,
                        Text = "ID | Résultat"
                    }
                ]
            });
        }
    }
}
