using System.IO.Compression;
using System.Text;
using System.Text.Json;
using Nodalis.Core.Domain;
using Nodalis.Core.Exporting;
using Nodalis.Infrastructure.Exporting;
using Nodalis.Infrastructure.Persistence;

internal static class ProjectExportSmokeTests
{
    /// <summary>
    /// Verifies portable Markdown, offline HTML, native DOCX, section ordering, attachment handling, and source immutability.
    /// </summary>
    /// <param name="root">The smoke-test root directory.</param>
    /// <returns>A task representing the verification.</returns>
    public static async Task RunAsync(
            string root)
    {
        string workspaceRoot =
            Path.Combine(
                root,
                "ProjectExportWorkspace");
        string destinationRoot =
            Path.Combine(
                root,
                "ProjectExportDestination");

        FileSystemWorkspaceStore store =
            new FileSystemWorkspaceStore(
                workspaceRoot);

        await store.InitializeAsync(
            "Project Export Workspace");

        Guid applicationId =
            Guid.NewGuid();
        Guid projectId =
            Guid.NewGuid();
        Guid techniqueSectionId =
            Guid.NewGuid();
        Guid testsSectionId =
            Guid.NewGuid();

        string applicationDirectory =
            Path.Combine(
                workspaceRoot,
                WorkspaceLayout.ApplicationsDirectoryName,
                "Application Export");
        string projectDirectory =
            Path.Combine(
                applicationDirectory,
                WorkspaceLayout.ProjectsDirectoryName,
                "Projet Export");
        string techniqueDirectory =
            Path.Combine(
                projectDirectory,
                "Technique");
        string testsDirectory =
            Path.Combine(
                projectDirectory,
                "Tests");
        string techniqueAssetsDirectory =
            Path.Combine(
                techniqueDirectory,
                "assets");

        Directory.CreateDirectory(
            techniqueAssetsDirectory);
        Directory.CreateDirectory(
            testsDirectory);
        Directory.CreateDirectory(
            destinationRoot);

        JsonSerializerOptions jsonOptions =
            new JsonSerializerOptions
            {
                PropertyNamingPolicy =
                    JsonNamingPolicy.CamelCase,
                WriteIndented =
                    true
            };

        ApplicationManifest application =
            new ApplicationManifest
            {
                Id =
                    applicationId,
                Name =
                    "Application Export"
            };

        ProjectManifest project =
            new ProjectManifest
            {
                Id =
                    projectId,
                Name =
                    "Projet Export",
                ApplicationId =
                    applicationId,
                InitialComplexity =
                    ProjectComplexity.Medium,
                Sections =
                [
                    new SectionManifest
                    {
                        Id =
                            techniqueSectionId,
                        Name =
                            "Technique",
                        Order =
                            20,
                        IsSingleton =
                            false,
                        TemplateKey =
                            "technical-standard"
                    },
                    new SectionManifest
                    {
                        Id =
                            testsSectionId,
                        Name =
                            "Tests",
                        Order =
                            40,
                        IsSingleton =
                            false,
                        TemplateKey =
                            "tests-standard"
                    }
                ]
            };

        await File.WriteAllTextAsync(
            Path.Combine(
                applicationDirectory,
                WorkspaceLayout.ApplicationManifestFileName),
            JsonSerializer.Serialize(
                application,
                jsonOptions));

        string projectManifestPath =
            Path.Combine(
                projectDirectory,
                WorkspaceLayout.ProjectManifestFileName);

        await File.WriteAllTextAsync(
            projectManifestPath,
            JsonSerializer.Serialize(
                project,
                jsonOptions));

        string architecturePath =
            Path.Combine(
                techniqueDirectory,
                "Architecture.md");
        string apiPath =
            Path.Combine(
                techniqueDirectory,
                "API.md");
        string validationPath =
            Path.Combine(
                testsDirectory,
                "Validation.md");
        string schemaPath =
            Path.Combine(
                techniqueAssetsDirectory,
                "schema.png");

        string architecture =
            "# Architecture\n\n" +
            "Consultez [[API|l'API]] et [la validation](../Tests/Validation.md).\n\n" +
            "![Schéma](assets/schema.png)\n";
        string api =
            "# API\n\n" +
            "Contrat local.\n";
        string validation =
            "# Validation\n\n" +
            "- [x] Test principal\n";
        byte[] schemaBytes =
        [
            137,
            80,
            78,
            71,
            13,
            10,
            26,
            10,
            1,
            2,
            3,
            4
        ];

        await File.WriteAllTextAsync(
            architecturePath,
            architecture);
        await File.WriteAllTextAsync(
            apiPath,
            api);
        await File.WriteAllTextAsync(
            validationPath,
            validation);
        await File.WriteAllBytesAsync(
            schemaPath,
            schemaBytes);

        string projectManifestBefore =
            await File.ReadAllTextAsync(
                projectManifestPath);
        DateTime architectureWriteBefore =
            File.GetLastWriteTimeUtc(
                architecturePath);
        byte[] schemaBefore =
            await File.ReadAllBytesAsync(
                schemaPath);

        WorkspaceProjectExportService service =
            new WorkspaceProjectExportService(
                workspaceRoot);

        ProjectExportPreview preview =
            await service.PreparePreviewAsync(
                projectDirectory);

        Assert(
            preview.ProjectId ==
                projectId &&
            preview.MarkdownDocumentCount ==
                3 &&
            preview.AttachmentCount ==
                1 &&
            preview.Sections.Count ==
                2,
            "Project export preview must discover project sections, Markdown documents, and referenced attachments without writing source data.");

        ProjectExportRequest request =
            new ProjectExportRequest
            {
                DestinationDirectory =
                    destinationRoot,
                BaseName =
                    "Projet Export Test",
                IncludeAttachments =
                    true,
                Formats =
                [
                    ProjectExportFormat.PortableMarkdown,
                    ProjectExportFormat.StandaloneHtml,
                    ProjectExportFormat.NativeDocx
                ],
                Sections =
                [
                    new ProjectExportSectionSelection
                    {
                        SectionId =
                            testsSectionId,
                        Order =
                            0
                    },
                    new ProjectExportSectionSelection
                    {
                        SectionId =
                            techniqueSectionId,
                        Order =
                            1
                    }
                ]
            };

        ProjectExportResult result =
            await service.ExportAsync(
                preview,
                request);

        Assert(
            result.OutputPaths.Count ==
                3 &&
            result.ExportedDocumentCount ==
                3 &&
            result.ExportedAttachmentCount ==
                1,
            "Project export must create every selected format and report exported document and attachment counts.");

        string markdownDirectory =
            result.OutputPaths.Single(
                Directory.Exists);
        string htmlPath =
            result.OutputPaths.Single(path =>
                string.Equals(
                    Path.GetExtension(
                        path),
                    ".html",
                    StringComparison.OrdinalIgnoreCase));
        string docxPath =
            result.OutputPaths.Single(path =>
                string.Equals(
                    Path.GetExtension(
                        path),
                    ".docx",
                    StringComparison.OrdinalIgnoreCase));

        string readme =
            await File.ReadAllTextAsync(
                Path.Combine(
                    markdownDirectory,
                    "README.md"));

        Assert(
            readme.IndexOf(
                "### Tests",
                StringComparison.Ordinal) <
            readme.IndexOf(
                "### Technique",
                StringComparison.Ordinal),
            "Portable Markdown table of contents must respect the user-configured section order.");

        string exportedArchitecturePath =
            Path.Combine(
                markdownDirectory,
                "Technique",
                "Architecture.md");
        string exportedArchitecture =
            await File.ReadAllTextAsync(
                exportedArchitecturePath);

        Assert(
            !exportedArchitecture.Contains(
                "[[API",
                StringComparison.Ordinal) &&
            exportedArchitecture.Contains(
                "[l'API](API.md)",
                StringComparison.Ordinal) &&
            exportedArchitecture.Contains(
                "../Attachments/schema.png",
                StringComparison.Ordinal),
            "Portable Markdown must replace Nodalis internal links and rewrite local attachment paths.");

        Assert(
            File.Exists(
                Path.Combine(
                    markdownDirectory,
                    "Attachments",
                    "schema.png")),
            "Portable Markdown must copy referenced attachments when requested.");

        string html =
            await File.ReadAllTextAsync(
                htmlPath);

        Assert(
            html.Contains(
                "data:image/png;base64,",
                StringComparison.Ordinal) &&
            html.Contains(
                "href=\"#doc-",
                StringComparison.Ordinal) &&
            html.IndexOf(
                ">Tests</h2>",
                StringComparison.Ordinal) <
            html.IndexOf(
                ">Technique</h2>",
                StringComparison.Ordinal),
            "Standalone HTML must embed local attachments, resolve internal navigation, and preserve section order without external assets.");

        using (ZipArchive archive =
               ZipFile.OpenRead(
                   docxPath))
        {
            Assert(
                archive.GetEntry(
                    "word/document.xml") is not null &&
                archive.GetEntry(
                    "word/styles.xml") is not null,
                "Native DOCX export must contain valid Open XML document and style parts.");

            ZipArchiveEntry documentEntry =
                archive.GetEntry(
                    "word/document.xml")
                ?? throw new InvalidOperationException(
                    "DOCX document part is missing.");

            using StreamReader reader =
                new StreamReader(
                    documentEntry.Open(),
                    Encoding.UTF8);

            string documentXml =
                await reader.ReadToEndAsync();

            Assert(
                documentXml.Contains(
                    "Projet Export",
                    StringComparison.Ordinal) &&
                documentXml.IndexOf(
                    ">Tests<",
                    StringComparison.Ordinal) <
                documentXml.IndexOf(
                    ">Technique<",
                    StringComparison.Ordinal),
                "Native DOCX must consolidate the selected project content in configured section order.");
        }

        byte[] schemaAfter =
            await File.ReadAllBytesAsync(
                schemaPath);

        Assert(
            await File.ReadAllTextAsync(
                architecturePath) ==
                architecture &&
            await File.ReadAllTextAsync(
                apiPath) ==
                api &&
            await File.ReadAllTextAsync(
                validationPath) ==
                validation &&
            await File.ReadAllTextAsync(
                projectManifestPath) ==
                projectManifestBefore &&
            schemaBefore.SequenceEqual(
                schemaAfter) &&
            File.GetLastWriteTimeUtc(
                architecturePath) ==
                architectureWriteBefore,
            "Every export format must leave the project source unchanged.");

        ProjectExportRequest withoutAttachments =
            new ProjectExportRequest
            {
                DestinationDirectory =
                    destinationRoot,
                BaseName =
                    "Projet Export Sans PJ",
                IncludeAttachments =
                    false,
                Formats =
                [
                    ProjectExportFormat.PortableMarkdown
                ],
                Sections =
                [
                    new ProjectExportSectionSelection
                    {
                        SectionId =
                            techniqueSectionId,
                        Order =
                            0
                    }
                ]
            };

        ProjectExportResult withoutAttachmentsResult =
            await service.ExportAsync(
                preview,
                withoutAttachments);

        string withoutAttachmentsDirectory =
            withoutAttachmentsResult.OutputPaths.Single();

        Assert(
            !Directory.Exists(
                Path.Combine(
                    withoutAttachmentsDirectory,
                    "Attachments")) &&
            (await File.ReadAllTextAsync(
                    Path.Combine(
                        withoutAttachmentsDirectory,
                        "Technique",
                        "Architecture.md")))
                .Contains(
                    "[image exclue : Schéma]",
                    StringComparison.Ordinal),
            "Attachment exclusion must produce a readable portable export without copying attachment files.");

        ProjectExportRequest unsafeDestination =
            request with
            {
                DestinationDirectory =
                    workspaceRoot
            };

        await AssertThrowsAsync<InvalidOperationException>(
            () =>
                service.ExportAsync(
                    preview,
                    unsafeDestination),
            "Project export must reject destinations inside the workspace before writing files.");
    }

    /// <summary>
    /// Throws when an export smoke-test condition is not satisfied.
    /// </summary>
    /// <param name="condition">The condition to verify.</param>
    /// <param name="message">The failure message.</param>
    private static void Assert(
            bool condition,
            string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(
                message);
        }
    }

    /// <summary>
    /// Verifies that an asynchronous operation throws the expected exception type.
    /// </summary>
    /// <typeparam name="TException">The expected exception type.</typeparam>
    /// <param name="action">The asynchronous action.</param>
    /// <param name="message">The failure message.</param>
    /// <returns>A task representing the assertion.</returns>
    private static async Task AssertThrowsAsync<TException>(
            Func<Task> action,
            string message)
        where TException : Exception
    {
        try
        {
            await action();
        }
        catch (TException)
        {
            return;
        }

        throw new InvalidOperationException(
            message);
    }
}
