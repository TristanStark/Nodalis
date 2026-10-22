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
    /// Runs a real child process and kills it after its first Markdown file was
    /// committed but before the DOCX source/provenance were recorded.
    /// </summary>
    /// <param name="workspace">Test workspace.</param>
    /// <returns>Process-boundary smoke test.</returns>
    public static async Task RunAsync(string workspace)
    {
        IReadOnlyList<ProjectCreationTarget> targets =
            await new ProjectCreationTargetDiscovery().DiscoverAsync(workspace);
        ProjectCreationTarget target = targets.First(candidate =>
            candidate.ModuleId is null && candidate.ParentProjectId is null);
        ProjectCreationResult project =
            await new FileSystemProjectCreator(workspace).CreateAsync(
                new ProjectCreationRequest
                {
                    Name = "HardKill Word " + Guid.NewGuid().ToString("N"),
                    Complexity = ProjectComplexity.UltraSimple,
                    Target = target
                });

        string docx = Path.Combine(workspace,
            "hard-kill-source-" + Guid.NewGuid().ToString("N") + ".docx");
        using (ZipArchive archive = ZipFile.Open(docx, ZipArchiveMode.Create))
        {
            WriteEntry(archive, "word/styles.xml", """
                <w:styles xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
                <w:style w:type="paragraph" w:styleId="Heading1">
                <w:name w:val="Titre 1"/><w:pPr><w:outlineLvl w:val="0"/></w:pPr>
                </w:style></w:styles>
                """);
            WriteEntry(archive, "word/document.xml", """
                <w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
                <w:body>
                <w:p><w:pPr><w:pStyle w:val="Heading1"/></w:pPr>
                <w:r><w:t>Technique</w:t></w:r></w:p>
                <w:p><w:r><w:t>Interruption volontaire au checkpoint</w:t></w:r></w:p>
                </w:body></w:document>
                """);
        }

        string manifest = Path.Combine(project.ProjectDirectory,
            WorkspaceLayout.ProjectManifestFileName);
        byte[] originalManifest = await File.ReadAllBytesAsync(manifest);
        string userNote = Path.Combine(project.ProjectDirectory, "Notes rapides.md");
        string originalNote = await File.ReadAllTextAsync(userNote);
        string signal = Path.Combine(workspace,
            "hard-kill-signal-" + Guid.NewGuid().ToString("N") + ".txt");
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
        start.ArgumentList.Add(project.Project.Id.ToString("D"));
        start.ArgumentList.Add(signal);

        using Process child = Process.Start(start) ??
            throw new InvalidOperationException("Child process could not be launched.");
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
            string errors = await child.StandardError.ReadToEndAsync();
            throw new InvalidOperationException(
                "DOCX hard-kill checkpoint not reached. Child said: " + errors);
        }

        string writtenMarkdown = await File.ReadAllTextAsync(signal);
        Assert(File.Exists(writtenMarkdown), "Child must publish an imported Markdown file.");
        DocxFirstImportRecoveryService recovery =
            new DocxFirstImportRecoveryService(workspace);
        Assert((await recovery.ListPendingProjectsAsync()).Any(path =>
                string.Equals(path, project.ProjectDirectory,
                    StringComparison.OrdinalIgnoreCase)),
            "A killed child must leave a durable recoverable DOCX journal.");

        Assert(await recovery.RecoverPendingAsync() == 1 &&
               !File.Exists(writtenMarkdown) &&
               (await File.ReadAllBytesAsync(manifest)).SequenceEqual(originalManifest) &&
               await File.ReadAllTextAsync(userNote) == originalNote &&
               File.Exists(docx),
            "Recovery after actual process kill must restore manifest and preserve existing notes/source.");
    }

    /// <summary>Child-mode import that intentionally blocks at a deterministic post-write checkpoint.</summary>
    /// <param name="workspace">Existing test workspace.</param>
    /// <param name="docx">Word source.</param>
    /// <param name="projectId">Existing target project.</param>
    /// <param name="signalPath">Signal for the parent to kill the process.</param>
    /// <returns>Only completes if unexpectedly not terminated.</returns>
    public static async Task RunChildAsync(
        string workspace, string docx, Guid projectId, string signalPath)
    {
        WorkspaceDocxImportService importer =
            new WorkspaceDocxImportService(workspace,
                afterMarkdownCreatedCheckpoint: (_, destination) =>
                {
                    // Signal only once the file is completely written; the
                    // parent will terminate this process before provenance.
                    File.WriteAllText(signalPath, destination);
                    Thread.Sleep(Timeout.Infinite);
                });
        DocxImportPreview preview = await importer.PreparePreviewAsync(docx);
        DocxImportTargetOption target = preview.Projects.Single(candidate =>
            candidate.Id == projectId);
        DocxImportCommitRequest request = new DocxImportCommitRequest
        {
            ApplicationId = target.ApplicationId,
            ProjectId = projectId,
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
            "The hard-kill smoke child unexpectedly completed.");
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
