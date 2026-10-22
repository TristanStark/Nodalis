using System.IO.Compression;
using System.Text;
using Nodalis.Core.Domain;
using Nodalis.Core.Importing;
using Nodalis.Infrastructure.Importing;

/// <summary>
/// Generates a 105-page-break DOCX fixture with nested styled headings to test
/// parser size handling and approved, multi-section Markdown imports without Office.
/// </summary>
internal static class DocxLargeDocumentReleaseSmokeTests
{
    /// <summary>Exercises large DOCX parsing, preview, exclusions and committed content.</summary>
    /// <param name="workspace">Disposable workspace containing an application.</param>
    /// <returns>Asynchronous qualification task.</returns>
    public static async Task RunAsync(string workspace)
    {
        string docxPath = Path.Combine(workspace,
            "qualification-large-" + Guid.NewGuid().ToString("N") + ".docx");
        StringBuilder body = new StringBuilder();
        for (int section = 0; section < 105; section++)
        {
            string title = (section % 3) switch
            {
                0 => "Développement",
                1 => "Tests",
                _ => "Recette"
            };
            body.Append("<w:p><w:pPr><w:pStyle w:val=\"Heading1\"/></w:pPr>")
                .Append("<w:r><w:t>")
                .Append(title).Append(' ').Append(section.ToString("D3"))
                .Append("</w:t></w:r></w:p>");
            body.Append("<w:p><w:r><w:t>Contenu de qualification ")
                .Append(section.ToString("D3"))
                .Append("</w:t></w:r></w:p>");
            body.Append("<w:p><w:r><w:br w:type=\"page\"/></w:r></w:p>");
        }

        string xml = "<w:document xmlns:w=\"http://schemas.openxmlformats.org/" +
            "wordprocessingml/2006/main\"><w:body>" + body +
            "</w:body></w:document>";
        string styles = "<w:styles xmlns:w=\"http://schemas.openxmlformats.org/" +
            "wordprocessingml/2006/main\"><w:style w:type=\"paragraph\" " +
            "w:styleId=\"Heading1\"><w:name w:val=\"Titre 1\"/>" +
            "<w:pPr><w:outlineLvl w:val=\"0\"/></w:pPr>" +
            "</w:style></w:styles>";

        using (ZipArchive archive = ZipFile.Open(docxPath, ZipArchiveMode.Create))
        {
            WriteXml(archive, "word/styles.xml", styles);
            WriteXml(archive, "word/document.xml", xml);
        }

        ParsedDocxDocument parsed = await new DocxParser().ParseAsync(docxPath);
        int headings = parsed.Blocks.Count(block =>
            block.Kind == DocxBlockKind.Paragraph &&
            block.Paragraph?.HeadingLevel == 1);
        Assert(headings == 105, "Every styled Word title must survive large-document parsing.");

        ImportDocumentTree tree = DocxStructuredImportReader.FromParsed(
            parsed, Path.GetFileName(docxPath), new string('a', 64));
        Assert(tree.Roots.Count == 105,
            "Every independent Word heading must stay addressable in the neutral import tree.");

        WorkspaceDocxImportService importer = new WorkspaceDocxImportService(workspace);
        DocxImportPreview preview = await importer.PreparePreviewAsync(docxPath);
        Assert(preview.Applications.Count > 0 && preview.Sections.Count > 0,
            "Large source needs an import target and at least one selectable part.");

        DocxImportCommitRequest request = new DocxImportCommitRequest
        {
            ApplicationId = preview.Applications[0].Id,
            NewProjectName = "DOCX qualification 105 pages " + Guid.NewGuid().ToString("N"),
            NewProjectComplexity = ProjectComplexity.UltraSimple,
            Sections = preview.Sections.Select(section => new DocxImportSectionSelection
            {
                SectionIndex = section.Index,
                Include = section.Index == 0 || section.Index % 11 != 0,
                TargetSection = (section.Index % 3) switch
                {
                    0 => "Technique",
                    1 => "Tests",
                    _ => "Recette"
                }
            }).ToList()
        };
        DocxImportPlan plan = await importer.BuildPlanAsync(preview, request);
        Assert(plan.Fingerprint.Length == 64 &&
               plan.Changes.Count(change =>
                   change.Action == "Créer" &&
                   change.Description.Contains("section Nodalis «",
                       StringComparison.Ordinal)) >= 1,
            "Every planned target needs an explicit, hash-checked file operation.");

        DocxImportCommitResult result = await importer.CommitAsync(
            preview, request with { ExpectedPlanFingerprint = plan.Fingerprint });
        string[] plannedMarkdownPaths = plan.Changes
            .Where(change => change.Action == "Créer" &&
                change.Description.Contains("section Nodalis «", StringComparison.Ordinal))
            .Select(change => Path.GetFullPath(Path.Combine(workspace, change.RelativePath)))
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        Assert(plannedMarkdownPaths.SequenceEqual(result.GeneratedFiles
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase),
                StringComparer.OrdinalIgnoreCase) &&
               result.GeneratedFiles.All(File.Exists),
            "A large import must create exactly the Markdown files declared by the approved plan.");
        Assert(File.Exists(result.SourceCopyPath) &&
               File.Exists(result.ManifestPath) &&
               File.Exists(Path.Combine(result.ProjectDirectory, "Fiche de Maintenance.md")),
            "Source, provenance and initial project files must survive the 105-page import.");

        string combined = string.Join("\n", result.GeneratedFiles
            .Select(File.ReadAllText));
        Assert(combined.Contains("Contenu de qualification 000", StringComparison.Ordinal),
            "Selected content must remain readable after conversion.");
    }

    /// <summary>Writes a ZIP entry with a UTF-8 XML payload.</summary>
    /// <param name="archive">Destination package.</param>
    /// <param name="entryName">Entry location within the package.</param>
    /// <param name="xml">XML document content.</param>
    private static void WriteXml(ZipArchive archive, string entryName, string xml)
    {
        using StreamWriter writer = new StreamWriter(
            archive.CreateEntry(entryName).Open(), new UTF8Encoding(false));
        writer.Write(xml);
    }

    /// <summary>Fails the smoke suite when an import contract is violated.</summary>
    /// <param name="valid">Expected condition.</param>
    /// <param name="reason">Actionable failure message.</param>
    private static void Assert(bool valid, string reason)
    {
        if (!valid)
        {
            throw new InvalidOperationException(reason);
        }
    }
}
