using System.Text.Json;
using Nodalis.Core.Domain;
using Nodalis.Core.Projects;
using Nodalis.Core.Requirements;
using Nodalis.Infrastructure.Projects;

/// <summary>Safe read-only A/B EXI preparation using only synthetic projects.</summary>
internal static class ProjectRequirementHandoverSmokeTests
{
    /// <summary>
    /// Checks proposed destination identity, source byte preservation,
    /// requalification of acceptance criteria and fresh source requirements.
    /// </summary>
    /// <param name="root">Disposable test workspace.</param>
    /// <returns>Completed assertions.</returns>
    public static async Task RunAsync(string root)
    {
        string application = Path.Combine(root, "Applications",
            "HandoverRequirements");
        string source = Path.Combine(application, "Projets", "Source");
        string target = Path.Combine(application, "Projets", "Target");
        Directory.CreateDirectory(source);
        Directory.CreateDirectory(target);
        Guid appId = Guid.NewGuid();
        ProjectManifest owner = new()
        {
            Id = Guid.NewGuid(), ApplicationId = appId, Name = "Source",
            InitialComplexity = ProjectComplexity.UltraSimple
        };
        ProjectManifest recipient = owner with
        {
            Id = Guid.NewGuid(), Name = "Target"
        };
        await File.WriteAllTextAsync(Path.Combine(source, ".project.json"),
            JsonSerializer.Serialize(owner));
        await File.WriteAllTextAsync(Path.Combine(target, ".project.json"),
            JsonSerializer.Serialize(recipient));

        ProjectRequirementRegistryService registry = new(root);
        ProjectRequirementEntry entry = await registry.CreateAsync(source,
            "Une exigence à reprendre");
        await registry.CreateAsync(target, "Exigence locale existante");
        string sourceFile = Path.Combine(source, "Documentation",
            "Exigences", entry.Card.Reference + ".md");
        string original = await File.ReadAllTextAsync(sourceFile);
        ProjectHandoverPreflightService preflight = new(root);
        ProjectHandoverIntentService intentions = new(root);
        ProjectRequirementHandoverPreviewService previews = new(root);
        ProjectHandoverIntentSnapshot history = await intentions.ReadAsync(source);

        ProjectHandoverPreview draftA = await preflight.PrepareAsync(
            source, target, entry.Card.Id, ProjectHandoverMethod.MoveOriginal);
        ProjectHandoverIntentSnapshot recordedA =
            await intentions.RecordIntentAsync(draftA, history,
                "Reviewer", "Déplacement EXI à qualifier avant transaction.");
        ProjectRequirementHandoverPreview previewA = await previews.PrepareAsync(
            source, recordedA.Ledger.Intents[0].Id);
        Assert(previewA.CanPrepare &&
               previewA.ProposedReference == "EXI-0002" &&
               previewA.ProposedId == entry.Card.Id &&
               previewA.ProposedTargetRelativePath!.EndsWith(
                   "/Documentation/Exigences/EXI-0002.md",
                   StringComparison.Ordinal),
            "A proposes next free target reference with original GUID. " +
            previewA.FormatReview());
        ProjectRequirementCard cardA =
            ProjectRequirementMarkdownCodec.Parse(previewA.ProposedMarkdown!);
        Assert(cardA.Id == entry.Card.Id &&
               cardA.ProjectId == recipient.Id &&
               cardA.Reference == "EXI-0002" &&
               cardA.Title == entry.Card.Title &&
               await File.ReadAllTextAsync(sourceFile) == original &&
               !File.Exists(Path.Combine(target, "Documentation",
                   "Exigences", "EXI-0002.md")),
            "An EXI preview must never create or move Markdown.");

        ProjectHandoverPreview draftB = await preflight.PrepareAsync(
            source, target, entry.Card.Id, ProjectHandoverMethod.LinkedSuccessor);
        ProjectHandoverIntentSnapshot recordedB =
            await intentions.RecordIntentAsync(draftB, recordedA,
                "Reviewer", "Successeur EXI sans critère à réévaluer.");
        ProjectRequirementHandoverPreview previewB = await previews.PrepareAsync(
            source, recordedB.Ledger.Intents[1].Id);
        ProjectRequirementCard cardB =
            ProjectRequirementMarkdownCodec.Parse(previewB.ProposedMarkdown!);
        Assert(previewB.CanPrepare &&
               cardB.Id == draftB.ProposedTargetObjectId &&
               cardB.Id != entry.Card.Id &&
               cardB.ProjectId == recipient.Id &&
               cardB.Validation == ProjectRequirementValidation.Draft &&
               cardB.Implementation ==
                   ProjectRequirementImplementation.NotStarted &&
               cardB.Verification ==
                   ProjectRequirementVerification.NotVerified &&
               previewB.ProposedMarkdown!.Contains(
                   "Passation B", StringComparison.Ordinal),
            "B proposes distinct draft identity and visible source provenance.");
        Assert(await File.ReadAllTextAsync(sourceFile) == original,
            "A and B previews must never touch source metadata.");

        bool rejectedStale = false;
        await File.AppendAllTextAsync(sourceFile, "\nNouvelle précision");
        try
        {
            await previews.PrepareAsync(source, recordedB.Ledger.Intents[1].Id);
        }
        catch (InvalidDataException)
        {
            rejectedStale = true;
        }

        Assert(rejectedStale,
            "An approved EXI intent must reject a later source change.");
        await File.WriteAllTextAsync(sourceFile, original);
        await registry.CreateAsync(target, "Nouvelle exigence destinataire");
        bool rejectedChangedTarget = false;
        try
        {
            await previews.PrepareAsync(
                source, recordedA.Ledger.Intents[0].Id);
        }
        catch (InvalidDataException)
        {
            rejectedChangedTarget = true;
        }

        Assert(rejectedChangedTarget,
            "An EXI intent must reject a destination that reserved a new code.");

        // A different recipient is required: the earlier B intention for
        // the first recipient is immutable and must never be duplicated.
        string criteriaTarget = Path.Combine(application, "Projets",
            "CriterionRecipient");
        Directory.CreateDirectory(criteriaTarget);
        await File.WriteAllTextAsync(
            Path.Combine(criteriaTarget, ".project.json"),
            JsonSerializer.Serialize(recipient with
            {
                Id = Guid.NewGuid(), Name = "CriterionRecipient"
            }));
        string criteria = original +
            "\n\n### CA-01 — Vérification locale\n" +
            "Condition métier à requalifier.\n";
        await File.WriteAllTextAsync(sourceFile, criteria);
        ProjectHandoverPreview withCa = await preflight.PrepareAsync(
            source, criteriaTarget, entry.Card.Id,
            ProjectHandoverMethod.LinkedSuccessor);
        ProjectHandoverIntentSnapshot latest = await intentions.ReadAsync(source);
        ProjectHandoverIntentSnapshot withCriteria =
            await intentions.RecordIntentAsync(withCa, latest,
                "Reviewer", "Les critères doivent conserver une preuve explicite.");
        ProjectRequirementHandoverPreview blocked = await previews.PrepareAsync(
            source, withCriteria.Ledger.Intents[^1].Id);
        Assert(!blocked.CanPrepare &&
               blocked.Blockers.Any(item => item.Contains(
                   "critères", StringComparison.OrdinalIgnoreCase)),
            "B cannot clone acceptance-criteria identities or unqualified CA.");
        Assert(await File.ReadAllTextAsync(sourceFile) == criteria,
            "A rejected CA succession must preserve original content.");

        // The canonical EXI writer uses AppendLine(), which produces CRLF
        // on Windows. Both LF and CRLF must be accepted without normalization
        // of the original bytes or mixed newlines in the candidate.
        string windowsSource = original.Replace("\r\n", "\n",
            StringComparison.Ordinal).Replace("\n", "\r\n",
            StringComparison.Ordinal);
        string windowsDraft = ProjectRequirementHandoverCodec.Prepare(
            windowsSource, entry.Card, recipient.Id, "EXI-0004",
            entry.Card.Id, Guid.NewGuid(), ProjectHandoverMethod.MoveOriginal);
        Assert(windowsDraft.Contains("\r\n", StringComparison.Ordinal) &&
               !windowsDraft.Replace("\r\n", string.Empty,
                   StringComparison.Ordinal).Contains('\r') &&
               ProjectRequirementMarkdownCodec.Parse(windowsDraft).Id ==
                   entry.Card.Id,
            "Windows-generated canonical EXI cards must retain CRLF in previews.");

        // Pure codec: even an explicitly approved/verified old requirement
        // may only produce a fresh B draft. The historical source is unchanged.
        ProjectRequirementCard approved = entry.Card with
        {
            Validation = ProjectRequirementValidation.Approved,
            Implementation = ProjectRequirementImplementation.Implemented,
            Verification = ProjectRequirementVerification.Verified
        };
        string canonical = ProjectRequirementMarkdownCodec.Format(approved);
        string proposal = ProjectRequirementHandoverCodec.Prepare(
            canonical, approved, recipient.Id, "EXI-0003",
            Guid.NewGuid(), Guid.NewGuid(),
            ProjectHandoverMethod.LinkedSuccessor);
        ProjectRequirementCard newCard =
            ProjectRequirementMarkdownCodec.Parse(proposal);
        Assert(newCard.Validation == ProjectRequirementValidation.Draft &&
               newCard.Implementation ==
                   ProjectRequirementImplementation.NotStarted &&
               newCard.Verification ==
                   ProjectRequirementVerification.NotVerified,
            "B may not inherit another project's human approvals or test verdict.");

        Directory.Delete(application, recursive: true);
    }

    /// <summary>Raises a regression error when a safety invariant fails.</summary>
    /// <param name="condition">Expected invariant.</param>
    /// <param name="message">Failure context.</param>
    private static void Assert(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
