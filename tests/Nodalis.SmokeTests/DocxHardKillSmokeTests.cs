using System.Diagnostics;
using System.IO.Compression;
using System.Text;
using Nodalis.Core.Domain;
using Nodalis.Core.Importing;
using Nodalis.Core.Projects;
using Nodalis.Infrastructure.Importing;
using Nodalis.Infrastructure.Projects;
using Nodalis.Infrastructure.Persistence;

internal static class DocxHardKillSmokeTests
{
    /// <summary>
    /// Terminates actual Windows child processes at every initial-import
    /// persistence boundary and checks what the next process may recover.
    /// </summary>
    /// <param name="workspace">Disposable smoke-test workspace.</param>
    /// <returns>End-to-end recovery qualification.</returns>
    public static async Task RunAsync(string workspace)
    {
        foreach (string checkpoint in new[]
                 { "project", "media", "markdown", "source", "provenance" })
        {
            await VerifyCheckpointAsync(workspace, checkpoint);
        }
    }

    /// <summary>Exercises one kill-and-recover sequence with a fresh DOCX source.</summary>
    /// <param name="workspace">Existing workspace.</param>
    /// <param name="checkpoint">Named durable checkpoint.</param>
    /// <returns>Completed checkpoint test.</returns>
    private static async Task VerifyCheckpointAsync(string workspace, string checkpoint)
    {
        IReadOnlyList<ProjectCreationTarget> targets =
            await new ProjectCreationTargetDiscovery().DiscoverAsync(workspace);
        ProjectCreationTarget target = targets.First(candidate =>
            candidate.ModuleId is null && candidate.ParentProjectId is null);
        bool createsProject = checkpoint == "project";
        ProjectCreationResult? existing = createsProject
            ? null
            : await new FileSystemProjectCreator(workspace).CreateAsync(
                new ProjectCreationRequest
                {
                    Name = "Kill " + checkpoint + " " + Guid.NewGuid().ToString("N"),
                    Complexity = ProjectComplexity.UltraSimple,
                    Target = target
                });

        string docx = Path.Combine(workspace,
            "kill-" + checkpoint + "-" + Guid.NewGuid().ToString("N") + ".docx");
        using (ZipArchive archive = ZipFile.Open(docx, ZipArchiveMode.Create))
        {
            WriteEntry(archive, "word/styles.xml", """
                <w:styles xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
                  <w:style w:type="paragraph" w:styleId="Heading1">
                    <w:name w:val="Titre 1"/><w:pPr><w:outlineLvl w:val="0"/></w:pPr>
                  </w:style>
                </w:styles>
                """);
            WriteEntry(archive, "word/document.xml", """
                <w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main"
                 xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships"
                 xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main"
                 xmlns:wp="http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing">
                  <w:body>
                    <w:p><w:pPr><w:pStyle w:val="Heading1"/></w:pPr>
                      <w:r><w:t>Technique</w:t></w:r></w:p>
                    <w:p><w:r><w:t>Interruption au checkpoint DOCX</w:t></w:r></w:p>
                    <w:p><w:r><w:drawing><wp:inline><wp:docPr id="1" name="Test image"/>
                      <a:graphic><a:graphicData><a:blip r:embed="rId1"/></a:graphicData></a:graphic>
                    </wp:inline></w:drawing></w:r></w:p>
                  </w:body>
                </w:document>
                """);
            WriteEntry(archive, "word/_rels/document.xml.rels", """
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  <Relationship Id="rId1"
                  Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/image"
                  Target="media/kill.png"/>
                </Relationships>
                """);
            byte[] pixel = Convert.FromBase64String(
                "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAusB9Y9fXzwAAAAASUVORK5CYII=");
            using Stream image = archive.CreateEntry("word/media/kill.png").Open();
            image.Write(pixel);
        }

        string? projectDirectory = existing?.ProjectDirectory;
        string? manifestPath = projectDirectory is null ? null :
            Path.Combine(projectDirectory, WorkspaceLayout.ProjectManifestFileName);
        byte[]? beforeManifest = manifestPath is null ? null :
            await File.ReadAllBytesAsync(manifestPath);
        string? notesPath = projectDirectory is null ? null :
            Path.Combine(projectDirectory, "Notes rapides.md");
        string? originalNotes = notesPath is null ? null :
            await File.ReadAllTextAsync(notesPath);
        string signal = Path.Combine(workspace,
            "kill-signal-" + Guid.NewGuid().ToString("N") + ".txt");
        string executable = Environment.ProcessPath ??
            throw new InvalidOperationException("Executable path unavailable for crash test.");
        ProcessStartInfo start = new ProcessStartInfo(executable)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true,
            RedirectStandardOutput = true
        };
        if (string.Equals(Path.GetFileNameWithoutExtension(executable),
                "dotnet", StringComparison.OrdinalIgnoreCase))
        {
            start.ArgumentList.Add(typeof(DocxHardKillSmokeTests).Assembly.Location);
        }

        start.ArgumentList.Add("--docx-hardkill-child");
        start.ArgumentList.Add(workspace);
        start.ArgumentList.Add(docx);
        start.ArgumentList.Add(existing?.Project.Id.ToString("D") ?? Guid.Empty.ToString("D"));
        start.ArgumentList.Add(signal);
        start.ArgumentList.Add(checkpoint);
        using Process child = Process.Start(start) ??
            throw new InvalidOperationException("DOCX crash-test child could not be launched.");
        bool checkpointReached = false;
        try
        {
            DateTime deadline = DateTime.UtcNow.AddSeconds(40);
            while (DateTime.UtcNow < deadline && !child.HasExited)
            {
                if (File.Exists(signal))
                {
                    checkpointReached = true;
                    break;
                }

                await Task.Delay(100);
            }
        }
        finally
        {
            if (!child.HasExited)
            {
                child.Kill(entireProcessTree: true);
            }

            await child.WaitForExitAsync();
        }

        if (!checkpointReached)
        {
            string errorText = await child.StandardError.ReadToEndAsync();
            throw new InvalidOperationException(
                "DOCX child never reached checkpoint " + checkpoint + ": " + errorText);
        }

        string signaledPath = await File.ReadAllTextAsync(signal);
        if (createsProject)
        {
            projectDirectory = signaledPath;
            Assert(Directory.Exists(projectDirectory),
                "Project-creation checkpoint must persist a real directory.");
        }
        else if (checkpoint is "markdown" or "source" or "provenance")
        {
            Assert(File.Exists(signaledPath),
                "Checkpoint " + checkpoint + " must observe a committed file.");
        }

        DocxFirstImportRecoveryService recovery =
            new DocxFirstImportRecoveryService(workspace);
        Assert((await recovery.ListPendingProjectsAsync()).Any(candidate =>
                string.Equals(candidate, projectDirectory,
                    StringComparison.OrdinalIgnoreCase)),
            "Hard-kill checkpoint " + checkpoint + " must leave a pending journal.");
        Assert(await recovery.RecoverPendingAsync() == 1,
            "Interrupted checkpoint " + checkpoint + " needs exactly one recovery.");
        Assert((await recovery.ListPendingProjectsAsync()).Count == 0,
            "Checkpoint " + checkpoint + " must no longer block future imports.");
        Assert(File.Exists(docx),
            "Crash recovery must never delete the original Word file.");

        if (createsProject)
        {
            Assert(Directory.Exists(projectDirectory) &&
                   File.Exists(Path.Combine(projectDirectory!, "Fiche de Maintenance.md")),
                "Crash after project creation must preserve its user-editable scaffold.");
            return;
        }

        Assert(manifestPath is not null && beforeManifest is not null &&
               notesPath is not null && originalNotes is not null &&
               (await File.ReadAllBytesAsync(manifestPath)).SequenceEqual(beforeManifest) &&
               await File.ReadAllTextAsync(notesPath) == originalNotes,
            "Crash recovery must not corrupt an existing project's manifest or notes.");

        if (checkpoint == "provenance")
        {
            Assert(File.Exists(signaledPath) &&
                   Directory.GetFiles(projectDirectory!, "*.md",
                       SearchOption.AllDirectories).Any(path =>
                       Path.GetFileName(path).StartsWith("Import - ",
                           StringComparison.Ordinal)),
                "A fully persisted import must be completed, not rolled back.");
        }
        else
        {
            Assert(Directory.GetFiles(projectDirectory!, "*.md",
                       SearchOption.AllDirectories).All(path =>
                       !Path.GetFileName(path).StartsWith("Import - ",
                           StringComparison.Ordinal)),
                "Recovery must remove incomplete imported Markdown files.");
            Assert(!Directory.Exists(Path.Combine(projectDirectory!, "Media-Docx")) ||
                   Directory.GetFiles(Path.Combine(projectDirectory!, "Media-Docx")).Length == 0,
                "Incomplete imported image must be removed after process termination.");
            if (checkpoint == "source")
            {
                Assert(!File.Exists(signaledPath),
                    "Partial DOCX source copy must be removed after crash rollback.");
            }
        }

        Assert(await recovery.RecoverPendingAsync() == 0,
            "Hard-kill recovery must be idempotent.");
    }

    /// <summary>Runs one Word import in a child until its selected checkpoint.</summary>
    /// <param name="workspace">Existing workspace.</param>
    /// <param name="docx">Word source file.</param>
    /// <param name="projectId">Existing project or empty for new-project fixture.</param>
    /// <param name="signalPath">File that informs the parent which file was committed.</param>
    /// <param name="checkpoint">Checkpoint that the parent must kill.</param>
    /// <returns>Never returns normally if the checkpoint is reached.</returns>
    public static async Task RunChildAsync(
        string workspace, string docx, Guid projectId, string signalPath,
        string checkpoint)
    {
        Action<string> signal = destination =>
        {
            File.WriteAllText(signalPath, destination);
            Thread.Sleep(Timeout.Infinite);
        };
        WorkspaceDocxImportService importer = new WorkspaceDocxImportService(
            workspace,
            afterMarkdownCreatedCheckpoint: (_, destination) =>
            {
                if (checkpoint == "markdown")
                {
                    signal(destination);
                }
            },
            afterDurableCheckpoint: (stage, destination) =>
            {
                if (stage == checkpoint)
                {
                    signal(destination);
                }
            });
        DocxImportPreview preview = await importer.PreparePreviewAsync(docx);
        DocxImportTargetOption? project = projectId == Guid.Empty
            ? null
            : preview.Projects.Single(candidate => candidate.Id == projectId);
        Guid appId = project?.ApplicationId ??
            preview.Applications.First().Id;
        DocxImportCommitRequest request = new DocxImportCommitRequest
        {
            ApplicationId = appId,
            ProjectId = projectId == Guid.Empty ? null : projectId,
            NewProjectName = projectId == Guid.Empty
                ? "DOCX Crash " + Guid.NewGuid().ToString("N")
                : null,
            NewProjectComplexity = ProjectComplexity.UltraSimple,
            Sections = preview.Sections.Select(section => new DocxImportSectionSelection
            {
                SectionIndex = section.Index,
                Include = true,
                TargetSection = "ImportCrashProtected"
            }).ToList()
        };
        DocxImportPlan plan = await importer.BuildPlanAsync(preview, request);
        await importer.CommitAsync(preview,
            request with { ExpectedPlanFingerprint = plan.Fingerprint });
        throw new InvalidOperationException(
            "DOCX crash child unexpectedly completed checkpoint " + checkpoint);
    }

    /// <summary>Writes one synthetic Open XML package entry.</summary>
    /// <param name="archive">ZIP archive.</param>
    /// <param name="name">Member path.</param>
    /// <param name="xml">XML content.</param>
    private static void WriteEntry(ZipArchive archive, string name, string xml)
    {
        using StreamWriter writer = new StreamWriter(
            archive.CreateEntry(name).Open(), new UTF8Encoding(false));
        writer.Write(xml);
    }

    /// <summary>Reports the actual unsafe outcome of a hard process termination.</summary>
    /// <param name="condition">Required invariant.</param>
    /// <param name="reason">Regression detail.</param>
    private static void Assert(bool condition, string reason)
    {
        if (!condition)
        {
            throw new InvalidOperationException(reason);
        }
    }
}
