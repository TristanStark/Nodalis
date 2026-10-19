using System.Text.Json;
using Nodalis.Core.Domain;
using Nodalis.Core.Importing;
using Nodalis.Infrastructure.Importing;
using Nodalis.Infrastructure.Persistence;

internal static class DocxReimportReviewSmokeTests
{
    /// <summary>
    /// Exercises the full read-only source/base/local review, including
    /// a changed Word section, local edits, deletion and cross-project refusal.
    /// </summary>
    public static async Task RunAsync(string root)
    {
        string projectDirectory = Path.Combine(root,
            "Review " + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(projectDirectory);
        Guid projectId = Guid.NewGuid();
        ProjectManifest project = new ProjectManifest
        {
            Id = projectId,
            Name = "Reimport review",
            ApplicationId = Guid.NewGuid(),
            InitialComplexity = ProjectComplexity.Medium
        };
        await File.WriteAllTextAsync(Path.Combine(projectDirectory,
            WorkspaceLayout.ProjectManifestFileName),
            JsonSerializer.Serialize(project, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            }));

        string documents = Path.Combine(projectDirectory, "Tests");
        Directory.CreateDirectory(documents);
        string markdownPath = Path.Combine(documents, "Import - spec.md");
        string original = "## Tests\n\nCas initial";
        string latestWord = "## Tests\n\nCas Word modifié";
        await File.WriteAllTextAsync(markdownPath, original);

        string sourceDirectory = Path.Combine(root, WorkspaceLayout.ImportsDirectoryName,
            WorkspaceLayout.ImportSourcesDirectoryName);
        Directory.CreateDirectory(sourceDirectory);
        string oldCopy = Path.Combine(sourceDirectory,
            "spec-review-" + Guid.NewGuid().ToString("N") + ".docx");
        string newCopy = Path.Combine(root,
            "new-review-" + Guid.NewGuid().ToString("N") + ".docx");
        await File.WriteAllTextAsync(oldCopy, "Original DOCX package fixture");
        await File.WriteAllTextAsync(newCopy, "Updated DOCX package fixture");

        DocxImportManifestStore store = new DocxImportManifestStore(root);
        DocxImportManifestFragment fragment = new DocxImportManifestFragment
        {
            FragmentId = Guid.NewGuid(),
            SourceSectionIndex = 0,
            HeadingBlockIndex = 0,
            SourceHeading = "Tests",
            SourceParentPath = string.Empty,
            TargetSection = "Tests",
            DestinationRelativePath = store.ToWorkspaceRelativePath(markdownPath),
            BaseMarkdown = original,
            FragmentSha256 = DocxImportManifestStore.ComputeSha256(original),
            DestinationSha256 = DocxImportManifestStore.ComputeSha256(original),
            SelectedBlockIndexes = [1]
        };
        DocxImportManifest previous = new DocxImportManifest
        {
            ImportId = Guid.NewGuid(),
            ProjectId = projectId,
            ImportedAtUtc = DateTimeOffset.UtcNow,
            SourceFileName = "spec.docx",
            SourceSha256 = await DocxImportManifestStore.ComputeFileSha256Async(oldCopy),
            SourceCopyRelativePath = store.ToWorkspaceRelativePath(oldCopy),
            Fragments = [fragment]
        };

        DocxBlock heading = new DocxBlock
        {
            Kind = DocxBlockKind.Paragraph,
            Paragraph = new DocxParagraph { Text = "Tests", HeadingLevel = 1 }
        };
        DocxBlock paragraph = new DocxBlock
        {
            Kind = DocxBlockKind.Paragraph,
            Paragraph = new DocxParagraph { Text = "Cas Word modifié" }
        };
        DocxImportPreview preview = new DocxImportPreview
        {
            StagedImport = new DocxStagedImport
            {
                OriginalSourcePath = "spec.docx",
                StagedCopyPath = newCopy,
                Document = new ParsedDocxDocument { Blocks = [heading, paragraph] }
            },
            Analysis = new DocxImportAnalysis(),
            Sections =
            [
                new DocxImportSectionPreview
                {
                    Index = 0,
                    HeadingBlockIndex = 0,
                    SourceHeading = "Tests",
                    SuggestedTargetSection = "Tests",
                    BlockCount = 1,
                    MarkdownPreview = latestWord,
                    Blocks =
                    [
                        new DocxImportBlockPreview
                        {
                            BlockIndex = 1,
                            Block = paragraph,
                            KindLabel = "Paragraphe",
                            DisplayText = "Cas Word modifié",
                            MarkdownPreview = "Cas Word modifié"
                        }
                    ]
                }
            ]
        };

        DocxReimportReviewService service = new DocxReimportReviewService(root);
        DocxReimportReview wordOnly = await service.BuildAsync(
            projectDirectory, previous, preview);
        Assert(wordOnly.Fragments.Count == 1 &&
               wordOnly.Fragments[0].Change?.Kind == DocxReimportChangeKind.WordChanged &&
               wordOnly.Fragments[0].BaseMarkdown == original &&
               wordOnly.Fragments[0].LatestWordMarkdown == latestWord &&
               wordOnly.Fragments[0].CurrentNodalisMarkdown == original &&
               !wordOnly.SourcePackageUnchanged,
            "Read-only review must compare old base, new Word and actual Nodalis text.");

        await File.WriteAllTextAsync(markdownPath, original + "\nAjout local");
        DocxReimportReview both = await service.BuildAsync(
            projectDirectory, previous, preview);
        Assert(both.Fragments[0].Change?.Kind == DocxReimportChangeKind.BothChanged &&
               both.Fragments[0].RequiresExplicitResolution &&
               File.ReadAllText(markdownPath).Contains("Ajout local", StringComparison.Ordinal),
            "A local edit must remain intact and create a three-way conflict.");

        File.Delete(markdownPath);
        DocxReimportReview missing = await service.BuildAsync(
            projectDirectory, previous, preview);
        Assert(missing.Fragments[0].Change?.Kind ==
                   DocxReimportChangeKind.DestinationMissing &&
               !File.Exists(markdownPath),
            "A removed local file must remain removed after inspection.");

        bool refusedOtherProject = false;
        try
        {
            await service.BuildAsync(projectDirectory,
                previous with { ProjectId = Guid.NewGuid() }, preview);
        }
        catch (InvalidDataException)
        {
            refusedOtherProject = true;
        }

        Assert(refusedOtherProject,
            "Reviewing a history from another project must fail closed.");

        bool refusedEscape = false;
        try
        {
            await service.BuildAsync(projectDirectory,
                previous with
                {
                    Fragments =
                    [
                        fragment with { DestinationRelativePath = "../secret.md" }
                    ]
                }, preview);
        }
        catch (InvalidDataException)
        {
            refusedEscape = true;
        }

        Assert(refusedEscape && File.Exists(oldCopy) && File.Exists(newCopy),
            "A malicious relative destination must not touch preserved source packages.");
    }

    /// <summary>Fails the end-to-end review contract.</summary>
    private static void Assert(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
