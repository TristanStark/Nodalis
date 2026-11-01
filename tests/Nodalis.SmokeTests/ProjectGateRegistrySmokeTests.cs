using System.Security.Cryptography;
using Nodalis.Core.Domain;
using Nodalis.Core.Projects;
using Nodalis.Infrastructure.Persistence;
using Nodalis.Infrastructure.Projects;

/// <summary>
/// Tests explicit human gate decisions, immutable historical snapshots,
/// safe evidence fingerprints and non-owning links with no milestone edits.
/// </summary>
internal static class ProjectGateRegistrySmokeTests
{
    /// <summary>Exercises the project gates from first read to review history.</summary>
    /// <param name="root">Disposable test root.</param>
    /// <returns>Completed gate registry regression suite.</returns>
    public static async Task RunAsync(string root)
    {
        string workspace = Path.Combine(root, "Gate-registry-v2-smoke");
        await new FileSystemWorkspaceStore(workspace)
            .InitializeAsync("Gate Registry v2");
        try
        {
            string application = Path.Combine(workspace,
                WorkspaceLayout.ApplicationsDirectoryName, "Test");
            Directory.CreateDirectory(application);
            FileSystemProjectCreator creator = new(workspace);
            ProjectCreationResult project = await creator.CreateAsync(
                new ProjectCreationRequest
                {
                    Name = "Gate Tests",
                    Complexity = ProjectComplexity.Complex,
                    Target = new ProjectCreationTarget
                    {
                        ApplicationId = Guid.NewGuid(),
                        ApplicationName = "Test",
                        ParentDirectory = application,
                        DisplayName = "Test"
                    }
                });
            ProjectGateRegistryService gates = new(workspace);
            string gatePath = Path.Combine(
                project.ProjectDirectory, ProjectGateRegistryService.RegistryFileName);
            string originalPresentation =
                await File.ReadAllTextAsync(project.OverviewFilePath);

            ProjectGateRegistrySnapshot initial = await gates.ReadAsync(
                project.ProjectDirectory);
            Assert(initial.Sha256 is null && initial.Registry.Gates.Count == 0 &&
                !File.Exists(gatePath),
                "Reading gates must not create a registry or invent approvals.");
            ProjectGateRegistry proposed = ProjectGateDefaults.ProposeMissing(
                initial.Registry);
            Assert(proposed.Gates.Count == 5 &&
                proposed.Decisions.Count == 0 && !File.Exists(gatePath),
                "Standard gate templates are optional and only in memory.");
            ProjectGateRegistry idempotent = ProjectGateDefaults.ProposeMissing(proposed);
            Assert(idempotent.Gates.Select(gate => gate.Id).SequenceEqual(
                    proposed.Gates.Select(gate => gate.Id)),
                "Applying optional standard templates twice must preserve IDs.");

            ProjectGateDefinition gate = proposed.Gates.Single(item =>
                item.Name == "GO/NO-GO production");
            proposed = ProjectGateEditor.AddCriterion(
                proposed, gate.Id, "Visa du responsable d'exploitation");
            ProjectGateDefinition current = proposed.Gates.Single(item =>
                item.Id == gate.Id);
            Assert(current.Criteria.Count == gate.Criteria.Count + 1 &&
                gate.Criteria.Count == 3 &&
                current.Criteria.All(item => item.Id != Guid.Empty),
                "New gate criteria must have stable IDs and not mutate old snapshots.");

            await RejectAsync(() => gates.SaveAsync(
                project.ProjectDirectory, proposed with
                {
                    ProjectId = Guid.NewGuid()
                }, initial.Sha256),
                "Cross-project gate registries must be rejected.");
            ProjectGateRegistrySnapshot saved = await gates.SaveAsync(
                project.ProjectDirectory, proposed, initial.Sha256);
            Assert(saved.Sha256 is { Length: 64 } && File.Exists(gatePath),
                "The first explicit write creates a revisioned registry.");

            string testDirectory = Path.Combine(project.ProjectDirectory, "Tests");
            Directory.CreateDirectory(testDirectory);
            string evidenceFile = Path.Combine(testDirectory, "Recette.md");
            const string evidenceText = "# Validation\n\nTests humains approuvés.";
            await File.WriteAllTextAsync(evidenceFile, evidenceText);
            ProjectJourneySourceService sourceService = new(workspace);
            string evidencePath = sourceService.GetReferenceForExistingFile(evidenceFile);
            ProjectGateEvidenceSnapshot proof =
                await gates.CaptureEvidenceAsync(evidencePath);
            string expectedHash = Convert.ToHexString(SHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes(evidenceText)));
            Assert(proof.Sha256 == expectedHash &&
                proof.RelativePath == evidencePath,
                "Decision evidence should record the SHA-256 of the actual bytes.");

            await RejectAsync(() => gates.CaptureEvidenceAsync("../outside.md"),
                "Evidence must never escape the workspace.");
            await RejectAsync(() => gates.CaptureEvidenceAsync(
                "Applications/NoSuchDocument.md"),
                "Missing evidence must never receive a fabricated hash.");
            string hugeFile = Path.Combine(testDirectory, "huge.md");
            await using (FileStream stream = new(hugeFile,
                FileMode.CreateNew, FileAccess.Write))
            {
                stream.SetLength(26 * 1024 * 1024);
            }

            string hugePath = sourceService.GetReferenceForExistingFile(hugeFile);
            await RejectAsync(() => gates.CaptureEvidenceAsync(hugePath),
                "Huge files must not be loaded or hashed without a bounded policy.");

            ProjectGateReview draft = CreateReview(current,
                ProjectGateVerdict.Go, [proof]);
            ProjectGateRegistry afterGo = ProjectGateEditor.AppendDecision(
                saved.Registry, draft);
            ProjectGateDecision first = afterGo.Decisions.Single();
            Assert(first.Verdict == ProjectGateVerdict.Go &&
                first.GateName == current.Name &&
                first.Assessments.Count == current.Criteria.Count &&
                first.Evidence.Single().Sha256 == expectedHash &&
                first.RecordedUtc != default &&
                saved.Registry.Decisions.Count == 0,
                "A user-confirmed GO must freeze criterion and evidence snapshots.");

            ProjectGateReview noEvidence = draft with { Evidence = [] };
            await RejectAsync(() => Task.FromResult(
                ProjectGateEditor.AppendDecision(saved.Registry, noEvidence)),
                "A GO cannot be inferred without proof.");
            ProjectGateReview unknown = draft with
            {
                Assessments = current.Criteria.Select(item =>
                    new ProjectGateCriterionAssessment
                    {
                        CriterionId = item.Id,
                        Name = item.Name,
                        Optional = item.Optional,
                        Status = ProjectGateCriterionStatus.Unknown
                    }).ToList()
            };
            await RejectAsync(() => Task.FromResult(
                ProjectGateEditor.AppendDecision(saved.Registry, unknown)),
                "An unknown mandatory criterion must block an unconditional GO.");
            await RejectAsync(() => Task.FromResult(
                ProjectGateEditor.AppendDecision(saved.Registry,
                    draft with { Rationale = "OK" })),
                "GO requires an explicit human justification.");

            ProjectGateRegistrySnapshot withFirst = await gates.SaveAsync(
                project.ProjectDirectory, afterGo, saved.Sha256);
            await RejectAsync(() => gates.SaveAsync(
                project.ProjectDirectory, afterGo, saved.Sha256),
                "Stale writers cannot overwrite a concurrent gate registry.");

            ProjectGateRegistry corrupted = withFirst.Registry with
            {
                Decisions = withFirst.Registry.Decisions.Select(item =>
                    item with { Verdict = ProjectGateVerdict.NoGo }).ToList()
            };
            await RejectAsync(() => gates.SaveAsync(
                project.ProjectDirectory, corrupted, withFirst.Sha256),
                "Existing human verdicts must be immutable.");
            await RejectAsync(() => gates.SaveAsync(
                project.ProjectDirectory,
                withFirst.Registry with { Decisions = [] }, withFirst.Sha256),
                "Historical verdicts cannot be erased.");

            ProjectGateRegistry later = ProjectGateEditor.AddCriterion(
                withFirst.Registry, gate.Id, "Requalification des incidents");
            ProjectGateDefinition updatedGate = later.Gates.Single(item =>
                item.Id == gate.Id);
            ProjectGateReview conditional = CreateReview(updatedGate,
                ProjectGateVerdict.GoWithReservations, [proof]) with
            {
                Assessments = updatedGate.Criteria.Select(item =>
                    new ProjectGateCriterionAssessment
                    {
                        CriterionId = item.Id,
                        Name = item.Name,
                        Optional = item.Optional,
                        Status = ProjectGateCriterionStatus.Unknown,
                        Comment = "Risque encore à examiner"
                    }).ToList(),
                Reservations = [new ProjectGateReservation
                {
                    Id = Guid.NewGuid(),
                    Description = "Revue de sécurité à terminer avant déploiement",
                    Owner = "Qualité"
                }]
            };
            later = ProjectGateEditor.AppendDecision(later, conditional);
            Assert(later.Decisions.Count == 2 &&
                later.Decisions[0].Id == first.Id &&
                later.Decisions[0].Verdict == ProjectGateVerdict.Go &&
                later.Decisions[0].Assessments.Count == current.Criteria.Count &&
                later.Decisions[1].Assessments.Count == updatedGate.Criteria.Count &&
                later.Decisions[1].Reservations.Count == 1,
                "A conditional GO may preserve reviewed uncertainty, while " +
                "historical GO snapshots remain unchanged.");
            await RejectAsync(() => Task.FromResult(
                ProjectGateEditor.AppendDecision(withFirst.Registry,
                    conditional with { Reservations = [] })),
                "Conditional GO requires an identified reservation.");

            ProjectGateRegistrySnapshot withSecond = await gates.SaveAsync(
                project.ProjectDirectory, later, withFirst.Sha256);
            ProjectGateRegistrySnapshot reread = await new ProjectGateRegistryService(
                workspace).ReadAsync(project.ProjectDirectory);
            Assert(withSecond.Sha256 == reread.Sha256 &&
                reread.Registry.Decisions.Count == 2 &&
                reread.Registry.Decisions[0].Id == first.Id &&
                reread.Registry.Decisions[1].Reservations.Single().Owner == "Qualité",
                "Decisions, evidence and reservations must survive a restart.");

            ProjectJourneyService journeys = new(workspace);
            ProjectJourneySnapshot journey = await journeys.InitializeAsync(
                project.ProjectDirectory);
            ProjectJourneyStep step = journey.Document.Steps.First();
            ProjectJourneyOperation operation = step.Operations.First();
            ProjectJourneyDocument linked = ProjectJourneyEditor.AddGateLink(
                journey.Document, step.Id, operation.Id, gate.Id);
            linked = ProjectJourneyEditor.AddGateLink(
                linked, step.Id, operation.Id, gate.Id);
            Assert(linked.Steps.First().Operations.First()
                    .GateIds.SequenceEqual([gate.Id]) &&
                operation.GateIds.Count == 0,
                "Linking a canonical gate must preserve operation identity " +
                "and never duplicate a reference.");
            ProjectJourneySnapshot linkSaved = await journeys.SaveAsync(
                project.ProjectDirectory, linked, journey.Sha256);
            ProjectJourneyDocument unlinked = ProjectJourneyEditor.RemoveGateLink(
                linkSaved.Document, step.Id, operation.Id, gate.Id);
            Assert(unlinked.Steps.First().Operations.First().GateIds.Count == 0 &&
                reread.Registry.Decisions.Count == 2 &&
                await File.ReadAllTextAsync(project.OverviewFilePath) ==
                    originalPresentation &&
                await File.ReadAllTextAsync(evidenceFile) == evidenceText,
                "Link/unlink never modifies source documents or human verdicts.");

            await File.WriteAllTextAsync(evidenceFile, "# Changed");
            ProjectGateEvidenceSnapshot changedProof =
                await gates.CaptureEvidenceAsync(evidencePath);
            Assert(changedProof.Sha256 != first.Evidence.Single().Sha256 &&
                (await gates.ReadAsync(project.ProjectDirectory))
                    .Registry.Decisions[0].Evidence[0].Sha256 == expectedHash,
                "Revalidation must not rewrite the historical evidence fingerprint.");
        }
        finally
        {
            if (Directory.Exists(workspace))
            {
                Directory.Delete(workspace, recursive: true);
            }
        }
    }

    /// <summary>Creates an explicitly positive review from known criteria.</summary>
    /// <param name="gate">Existing gate definition.</param>
    /// <param name="verdict">Explicit human verdict.</param>
    /// <param name="proofs">Explicit local proofs.</param>
    /// <returns>Review draft.</returns>
    private static ProjectGateReview CreateReview(ProjectGateDefinition gate,
        ProjectGateVerdict verdict, List<ProjectGateEvidenceSnapshot> proofs) => new()
    {
        GateId = gate.Id,
        Verdict = verdict,
        Actor = "Responsable de recette",
        Scope = "Version 2.0 / environnement de recette",
        Rationale = "La revue humaine a vérifié les critères et les preuves.",
        Version = "2.0.0-rc",
        Environment = "RECETTE",
        Evidence = proofs,
        Assessments = gate.Criteria.Select(item =>
            new ProjectGateCriterionAssessment
            {
                CriterionId = item.Id,
                Name = item.Name,
                Optional = item.Optional,
                Status = ProjectGateCriterionStatus.Conform,
                Comment = "Examiné manuellement"
            }).ToList()
    };

    /// <summary>Verifies unsafe commands are rejected.</summary>
    /// <param name="action">Expected failing command.</param>
    /// <param name="message">Assertion diagnostic.</param>
    /// <returns>Completed negative assertion.</returns>
    private static async Task RejectAsync(Func<Task> action, string message)
    {
        bool rejected = false;
        try
        {
            await action();
        }
        catch (Exception exception) when (exception is IOException or
            InvalidDataException or ArgumentException or InvalidOperationException or
            UnauthorizedAccessException or KeyNotFoundException)
        {
            rejected = true;
        }

        Assert(rejected, message);
    }

    /// <summary>Fails a smoke test when a gate invariant breaks.</summary>
    /// <param name="condition">Expected invariant.</param>
    /// <param name="message">Diagnostic.</param>
    private static void Assert(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
