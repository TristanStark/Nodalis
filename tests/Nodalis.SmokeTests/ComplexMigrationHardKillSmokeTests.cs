using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Nodalis.Core.Domain;
using Nodalis.Infrastructure.Persistence;
using Nodalis.Infrastructure.Projects;

/// <summary>
/// Starts real isolated Windows child processes, kills them at durable
/// cross-project migration checkpoints, and qualifies the next startup.
/// </summary>
internal static class ComplexMigrationHardKillSmokeTests
{
    /// <summary>Exercises actual termination during publish and two rollback phases.</summary>
    /// <param name="root">Disposable outer test directory.</param>
    /// <returns>All independent crash-recovery qualifications.</returns>
    public static async Task RunAsync(string root)
    {
        foreach (string checkpoint in new[]
                 { "external-published", "rollback-source-moved",
                   "external-rolled-back" })
        {
            await VerifyKillAsync(root, checkpoint);
        }
    }

    /// <summary>Creates a fixture, kills one child and performs manual recovery.</summary>
    /// <param name="root">Disposable test parent.</param>
    /// <param name="checkpoint">Injected durable transaction checkpoint.</param>
    /// <returns>Recovery and exact byte comparison task.</returns>
    private static async Task VerifyKillAsync(string root, string checkpoint)
    {
        string workspace = Path.Combine(root, "Complex-v2-kill-" +
            checkpoint + "-" + Guid.NewGuid().ToString("N"));
        await new FileSystemWorkspaceStore(workspace)
            .InitializeAsync("Hard-kill Complex 2.0");
        try
        {
            string project = await CreateProjectAsync(workspace);
            string a = Path.Combine(workspace, "01 External.md");
            string b = Path.Combine(workspace, "02 External.md");
            string originalA = "[Technique](<Applications/Crash/Projets/" +
                "Legacy/Technique/Technique.md#target>)\r\n";
            string originalB = "![Jalons](<Applications/Crash/Projets/" +
                "Legacy/Jalons.md>)\r\n";
            await File.WriteAllTextAsync(a, originalA);
            await File.WriteAllTextAsync(b, originalB);
            byte[] originalABytes = await File.ReadAllBytesAsync(a);
            byte[] originalBBytes = await File.ReadAllBytesAsync(b);
            string originalProject = await ComplexProjectMigrationPlanService
                .FingerprintAsync(project);

            ComplexProjectMigrationPlan plan =
                await new ComplexProjectMigrationPlanService(workspace)
                    .PrepareAsync(project);
            Assert(plan.CanExecute && plan.ExternalLinkRewrites.Count == 2,
                "A real hard-kill fixture must have two affected backlinks.");

            ComplexProjectMigrationTransactionService transaction = new(workspace);
            Guid rollbackId = Guid.Empty;
            if (checkpoint != "external-published")
            {
                await transaction.ApplyAsync(
                    plan, plan.SourceFingerprint, approved: true);
                ComplexProjectMigrationJournal completed =
                    (await transaction.GetRollbackCandidatesAsync(project)).Single();
                rollbackId = completed.TransactionId;
            }

            string signal = Path.Combine(workspace,
                "kill-checkpoint-" + Guid.NewGuid().ToString("N") + ".txt");
            await KillAtCheckpointAsync(workspace, project, checkpoint,
                signal, rollbackId);

            Assert((await transaction.ListPendingProjectsAsync()).Single() == project,
                "A real process termination must retain one pending journal: " +
                checkpoint);
            if (checkpoint == "external-published")
            {
                Assert(File.Exists(Path.Combine(project, "Documentation",
                        "Technique", "Technique.md")) &&
                    (await File.ReadAllTextAsync(a)).Contains(
                        "Documentation/Technique/Technique.md",
                        StringComparison.Ordinal) &&
                    (await File.ReadAllBytesAsync(b)).SequenceEqual(originalBBytes),
                    "Hard kill during publish must retain one old and one new backlink.");
            }
            else if (checkpoint == "rollback-source-moved")
            {
                Assert(!Directory.Exists(project),
                    "Hard kill after rollback source move must leave a missing project.");
            }
            else
            {
                Assert(File.Exists(Path.Combine(project,
                        "Technique", "Technique.md")) &&
                    (await File.ReadAllBytesAsync(a)).SequenceEqual(originalABytes) &&
                    !(await File.ReadAllBytesAsync(b)).SequenceEqual(originalBBytes),
                    "Hard kill mid-rollback must preserve a recoverable mixed backlink set.");
            }

            await RejectNoApprovalAsync(transaction);
            Assert(await transaction.RecoverPendingAsync(approved: true) == 1 &&
                (await transaction.ListPendingProjectsAsync()).Count == 0,
                "Explicit startup recovery must reconcile the real killed process.");

            if (checkpoint == "external-published")
            {
                Assert((await File.ReadAllTextAsync(b)).Contains(
                    "Management/Jalons.md", StringComparison.Ordinal),
                    "A pending published migration must finish its remaining backlinks.");
                ComplexProjectMigrationJournal completed =
                    (await transaction.GetRollbackCandidatesAsync(project)).Single();
                await transaction.RollbackAsync(completed.TransactionId,
                    approved: true);
            }

            Assert(Directory.Exists(project) &&
                await ComplexProjectMigrationPlanService.FingerprintAsync(project) ==
                    originalProject &&
                (await File.ReadAllBytesAsync(a)).SequenceEqual(originalABytes) &&
                (await File.ReadAllBytesAsync(b)).SequenceEqual(originalBBytes) &&
                (await transaction.ListPendingProjectsAsync()).Count == 0,
                "Recovery and rollback after real process death must restore all bytes.");
            Assert(await transaction.RecoverPendingAsync(approved: true) == 0,
                "A recovered real crash must be idempotent.");
        }
        finally
        {
            if (Directory.Exists(workspace))
            {
                Directory.Delete(workspace, recursive: true);
            }
        }
    }

    /// <summary>Starts the same executable and kills a child after its signal.</summary>
    /// <param name="workspace">Disposable workspace.</param>
    /// <param name="project">Project subject to migration.</param>
    /// <param name="checkpoint">Named checkpoint to kill.</param>
    /// <param name="signal">Synchronization file written by the child.</param>
    /// <param name="transactionId">Rollback ID or empty on first publish.</param>
    /// <returns>Completed child termination.</returns>
    private static async Task KillAtCheckpointAsync(string workspace,
        string project, string checkpoint, string signal, Guid transactionId)
    {
        string executable = Environment.ProcessPath ??
            throw new InvalidOperationException(
                "Hard-kill test executable is unavailable.");
        ProcessStartInfo info = new(executable)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        if (string.Equals(Path.GetFileNameWithoutExtension(executable),
            "dotnet", StringComparison.OrdinalIgnoreCase))
        {
            info.ArgumentList.Add(typeof(ComplexMigrationHardKillSmokeTests)
                .Assembly.Location);
        }

        info.ArgumentList.Add("--complex-migration-kill-child");
        info.ArgumentList.Add(workspace);
        info.ArgumentList.Add(project);
        info.ArgumentList.Add(checkpoint);
        info.ArgumentList.Add(signal);
        info.ArgumentList.Add(transactionId.ToString("D"));
        using Process child = Process.Start(info) ??
            throw new InvalidOperationException(
                "Complex migration child process could not be started.");
        bool reached = false;
        try
        {
            DateTime deadline = DateTime.UtcNow.AddSeconds(40);
            while (DateTime.UtcNow < deadline && !child.HasExited)
            {
                if (File.Exists(signal))
                {
                    reached = true;
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

        if (!reached)
        {
            string error = await child.StandardError.ReadToEndAsync();
            throw new InvalidOperationException(
                "The child did not reach " + checkpoint + ": " + error);
        }
    }

    /// <summary>Runs exactly one approved operation until the parent kills it.</summary>
    /// <param name="workspace">Existing isolated workspace.</param>
    /// <param name="project">Project to migrate or restore.</param>
    /// <param name="checkpoint">Checkpoint to signal.</param>
    /// <param name="signal">Signal file outside the project.</param>
    /// <param name="transactionId">Rollback ID or empty for first publication.</param>
    /// <returns>Never completes normally after the requested checkpoint.</returns>
    public static async Task RunChildAsync(string workspace, string project,
        string checkpoint, string signal, Guid transactionId)
    {
        ComplexProjectMigrationTransactionService executor = new(workspace,
            checkpoint: phase =>
            {
                if (phase == checkpoint)
                {
                    File.WriteAllText(signal, phase);
                    Thread.Sleep(Timeout.Infinite);
                }
            });

        if (checkpoint == "external-published")
        {
            ComplexProjectMigrationPlan plan =
                await new ComplexProjectMigrationPlanService(workspace)
                    .PrepareAsync(project);
            await executor.ApplyAsync(plan, plan.SourceFingerprint, approved: true);
        }
        else
        {
            await executor.RollbackAsync(transactionId, approved: true);
        }

        throw new InvalidOperationException(
            "The child finished without reaching crash checkpoint " +
            checkpoint);
    }

    /// <summary>Creates one complete legacy Complex project fixture.</summary>
    /// <param name="workspace">Disposable parent workspace.</param>
    /// <returns>Absolute project directory.</returns>
    private static async Task<string> CreateProjectAsync(string workspace)
    {
        JsonSerializerOptions json = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true
        };
        json.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        Guid applicationId = Guid.NewGuid();
        string application = Path.Combine(workspace, "Applications", "Crash");
        Directory.CreateDirectory(application);
        await File.WriteAllTextAsync(Path.Combine(application,
            WorkspaceLayout.ApplicationManifestFileName),
            JsonSerializer.Serialize(new ApplicationManifest
            {
                Id = applicationId,
                Name = "Crash"
            }, json));

        string project = Path.Combine(application, "Projets", "Legacy");
        Directory.CreateDirectory(project);
        SectionManifest[] sections =
        [
            Section("Jalons", "milestones", true),
            Section("Documentation", "documentation", true),
            Section("Fonctionnel", "functional", true),
            Section("Technique", "technical-complex", true),
            Section("Risques", "risks", true),
            Section("Dépendances", "dependencies", true),
            Section("Exploitation", "operations", true),
            Section("Déploiement", "deployment", true),
            Section("Réunions", null, false),
            Section("Décisions", null, false),
            Section("Tests", "tests-complex", true),
            Section("Glossaire", "glossary", true)
        ];
        await File.WriteAllTextAsync(Path.Combine(project,
            WorkspaceLayout.ProjectManifestFileName),
            JsonSerializer.Serialize(new ProjectManifest
            {
                Id = Guid.NewGuid(),
                ApplicationId = applicationId,
                Name = "Legacy",
                InitialComplexity = ProjectComplexity.Complex,
                Sections = sections.ToList()
            }, json));
        foreach (string name in new[]
                 { "Documentation", "Fonctionnel", "Technique", "Risques",
                   "Dépendances", "Exploitation", "Déploiement", "Réunions",
                   "Décisions", "Tests" })
        {
            string directory = Path.Combine(project, name);
            Directory.CreateDirectory(directory);
            await File.WriteAllTextAsync(Path.Combine(directory, name + ".md"),
                "# " + name + "\n");
        }

        await File.WriteAllTextAsync(Path.Combine(project, "Jalons.md"),
            "# Jalons\n");
        await File.WriteAllTextAsync(Path.Combine(project, "Glossaire.md"),
            "# Glossaire\n");
        return project;
    }

    /// <summary>Creates one ephemeral manifest section.</summary>
    /// <param name="name">Display section name.</param>
    /// <param name="key">Template classification.</param>
    /// <param name="singleton">Whether section is a singleton.</param>
    /// <returns>Section with stable GUID.</returns>
    private static SectionManifest Section(string name, string? key,
        bool singleton) => new()
    {
        Id = Guid.NewGuid(),
        Name = name,
        TemplateKey = key,
        IsSingleton = singleton
    };

    /// <summary>Ensures an unapproved recovery never runs.</summary>
    /// <param name="executor">Transaction under review.</param>
    /// <returns>Assertion task.</returns>
    private static async Task RejectNoApprovalAsync(
        ComplexProjectMigrationTransactionService executor)
    {
        bool rejected = false;
        try
        {
            await executor.RecoverPendingAsync(approved: false);
        }
        catch (InvalidOperationException)
        {
            rejected = true;
        }

        Assert(rejected, "Crash recovery must require explicit human approval.");
    }

    /// <summary>Asserts a reliable transaction invariant.</summary>
    /// <param name="condition">Required condition.</param>
    /// <param name="message">Failure explanation.</param>
    private static void Assert(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
