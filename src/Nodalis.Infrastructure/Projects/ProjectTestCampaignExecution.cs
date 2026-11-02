using System.Security.Cryptography;
using System.Text;
using Nodalis.Core.Links;
using Nodalis.Core.Tests;
using Nodalis.Infrastructure.Reliability;

namespace Nodalis.Infrastructure.Projects;

/// <summary>
/// Frozen passage creation and human result recording. Test definitions and
/// acceptance criteria are NEVER rewritten by this service.
/// </summary>
public sealed partial class ProjectTestCampaignService
{
    private const int MaxTestSourceBytes = 16 * 1024 * 1024;

    /// <summary>
    /// Starts a new passage from a reviewer-selected set of globally unique
    /// test GUIDs. Each source is re-read and its current SHA compared to the
    /// provided shared index to prevent freezing stale or foreign definitions.
    /// </summary>
    /// <param name="projectDirectory">Project owning the campaign and tests.</param>
    /// <param name="snapshot">Previously read REC and source revision.</param>
    /// <param name="index">Refreshed shared link index snapshot.</param>
    /// <param name="testIds">Explicitly approved test GUIDs, in chosen order.</param>
    /// <param name="environment">Current human-declared environment.</param>
    /// <param name="applicationVersion">Current human-declared version.</param>
    /// <param name="actor">Human authorizing this passage.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    /// <returns>Published REC revision with immutable new passage.</returns>
    public async Task<ProjectTestCampaignEntry> StartPassageAsync(
        string projectDirectory, ProjectTestCampaignEntry snapshot,
        LinkIndexCatalog index, IReadOnlyList<Guid> testIds,
        string environment, string applicationVersion, string actor,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(index);
        ArgumentNullException.ThrowIfNull(testIds);
        if (testIds.Count is < 1 or > 500 ||
            testIds.Contains(Guid.Empty) ||
            testIds.Distinct().Count() != testIds.Count ||
            !HumanField(environment, 160) ||
            !HumanField(applicationVersion, 160) ||
            !HumanField(actor, 160))
        {
            throw new InvalidDataException(
                "Sélection, environnement, version ou auteur de passage invalides.");
        }

        (string directory, ProjectTestCampaign campaign,
            TextDocumentSession session) = await OpenVerifiedAsync(
                projectDirectory, snapshot, cancellationToken);
        if (campaign.State is not
            (ProjectTestCampaignState.Draft or ProjectTestCampaignState.InProgress) ||
            campaign.Passages.Count >= 100)
        {
            throw new InvalidOperationException(
                "La campagne doit être ouverte et accepter un nouveau passage.");
        }

        string projectRelative = Path.GetRelativePath(_workspaceRoot,
            directory).Replace('\\', '/');
        Dictionary<Guid, IndexedMarkdownBlock[]> blocksById = index.Blocks
            .GroupBy(block => block.Id)
            .ToDictionary(group => group.Key, group => group.ToArray());
        Dictionary<Guid, LinkTargetEntry[]> targetsById = index.Targets
            .GroupBy(target => target.Id)
            .ToDictionary(group => group.Key, group => group.ToArray());
        Dictionary<string, (LinkTargetEntry Document,
            ProjectTestDefinitionScan Scan)> sourceCache = new(
                StringComparer.OrdinalIgnoreCase);
        List<ProjectTestFrozenDefinition> frozen = [];

        foreach (Guid testId in testIds)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!blocksById.TryGetValue(testId,
                    out IndexedMarkdownBlock[]? candidates) ||
                candidates.Length != 1 ||
                targetsById.ContainsKey(testId))
            {
                throw new InvalidDataException(
                    "GUID de test absent ou en collision dans l'index.");
            }

            IndexedMarkdownBlock block = candidates[0];
            if (!targetsById.TryGetValue(block.DocumentId,
                    out LinkTargetEntry[]? owners) ||
                owners.Length != 1 ||
                owners[0].Kind != LinkTargetKind.Document)
            {
                throw new InvalidDataException(
                    "Document propriétaire du test absent ou ambigu.");
            }

            LinkTargetEntry source = owners[0];
            string sourcePath = source.RelativePath.Replace('\\', '/');
            if (!sourcePath.StartsWith(projectRelative + "/",
                    StringComparison.OrdinalIgnoreCase) ||
                !IsTestFolder(sourcePath[(projectRelative.Length + 1)..]) ||
                string.IsNullOrWhiteSpace(source.ContentHash))
            {
                throw new InvalidDataException(
                    "Source de test hors du projet ou du référentiel Tests/Recette.");
            }

            if (!sourceCache.TryGetValue(sourcePath,
                    out (LinkTargetEntry Document,
                        ProjectTestDefinitionScan Scan) cached))
            {
                // A large cahier is read and parsed once, even for 180+ tests.
                string file = _sources.ResolveExistingFile(sourcePath);
                FileInfo info = new(file);
                if (info.Length > MaxTestSourceBytes)
                {
                    throw new InvalidDataException(
                        "Cahier trop volumineux (>16 Mio) pour figer un passage.");
                }

                byte[] bytes = await File.ReadAllBytesAsync(
                    file, cancellationToken);
                string actualSha = Convert.ToHexString(
                    SHA256.HashData(bytes));
                if (!string.Equals(actualSha, source.ContentHash,
                        StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException(
                        "Cahier modifié depuis l'index : actualisez avant le passage.");
                }

                string text = new UTF8Encoding(false, true).GetString(bytes);
                cached = (source, ProjectTestDefinitionCodec.Inspect(text));
                sourceCache.Add(sourcePath, cached);
            }

            ProjectTestDefinition[] matches = cached.Scan.Tests.Where(
                test => test.Id == testId && test.CanFreeze).ToArray();
            if (matches.Length != 1 ||
                matches[0].HeadingLine != block.HeadingLine)
            {
                throw new InvalidDataException(
                    "Test non identifié sans ambiguïté dans son cahier.");
            }

            ProjectTestDefinition chosen = matches[0];
            if (chosen.Markdown.Length > 512 * 1024)
            {
                throw new InvalidDataException(
                    "Section de test trop longue pour un passage figé.");
            }

            frozen.Add(new ProjectTestFrozenDefinition
            {
                TestId = testId,
                Reference = chosen.Reference,
                SourceRelativePath = sourcePath,
                Markdown = chosen.Markdown,
                Sha256 = chosen.Sha256,
                HeadingLine = chosen.HeadingLine
            });
        }

        ProjectTestPassage passage = new()
        {
            Id = Guid.NewGuid(),
            Number = campaign.Passages.Count + 1,
            Environment = environment.Trim(),
            ApplicationVersion = applicationVersion.Trim(),
            StartedBy = actor.Trim(),
            StartedUtc = DateTimeOffset.UtcNow,
            Tests = frozen,
            Results = []
        };
        ProjectTestCampaign updated = campaign with
        {
            State = ProjectTestCampaignState.InProgress,
            Passages = campaign.Passages.Append(passage).ToArray()
        };
        return await CommitAsync(session, updated, cancellationToken);
    }

    /// <summary>
    /// Records a single explicit human verdict in the latest active passage.
    /// A previously recorded verdict is immutable; rerun in a NEW passage.
    /// </summary>
    /// <param name="projectDirectory">Owning project.</param>
    /// <param name="snapshot">Previously displayed source and SHA.</param>
    /// <param name="passageId">Last active passage stable GUID.</param>
    /// <param name="testId">Frozen selected test ID.</param>
    /// <param name="outcome">Human-selected outcome.</param>
    /// <param name="actor">Human executor.</param>
    /// <param name="rationale">Human description of the result.</param>
    /// <param name="evidenceNote">Optional unverified descriptive evidence.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    /// <returns>New appended result and source revision.</returns>
    public async Task<ProjectTestCampaignEntry> RecordResultAsync(
        string projectDirectory, ProjectTestCampaignEntry snapshot,
        Guid passageId, Guid testId, ProjectTestOutcome outcome,
        string actor, string rationale, string? evidenceNote,
        CancellationToken cancellationToken = default)
    {
        if (passageId == Guid.Empty || testId == Guid.Empty ||
            !Enum.IsDefined(outcome) ||
            !HumanField(actor, 160) ||
            !HumanField(rationale, 2000) ||
            evidenceNote is { Length: > 4000 } ||
            evidenceNote?.Any(char.IsControl) == true)
        {
            throw new InvalidDataException(
                "Verdict, auteur, justification ou preuve déclarée invalide.");
        }

        (string directory, ProjectTestCampaign campaign,
            TextDocumentSession session) = await OpenVerifiedAsync(
                projectDirectory, snapshot, cancellationToken);
        if (campaign.State != ProjectTestCampaignState.InProgress ||
            campaign.Passages.Count == 0)
        {
            throw new InvalidOperationException(
                "Cette campagne n'accepte plus de nouveaux résultats.");
        }

        ProjectTestPassage passage = campaign.Passages[^1];
        if (passage.Id != passageId ||
            !passage.Tests.Any(item => item.TestId == testId) ||
            passage.Results.Any(item => item.TestId == testId))
        {
            throw new InvalidDataException(
                "Test absent, déjà qualifié ou passage historique non modifiable.");
        }

        ProjectTestResultEvent result = new()
        {
            Id = Guid.NewGuid(),
            TestId = testId,
            Outcome = outcome,
            Actor = actor.Trim(),
            Rationale = rationale.Trim(),
            EvidenceNote = evidenceNote?.Trim(),
            RecordedUtc = DateTimeOffset.UtcNow
        };
        ProjectTestPassage changed = passage with
        {
            Results = passage.Results.Append(result).ToArray()
        };
        ProjectTestCampaign updated = campaign with
        {
            Passages = campaign.Passages.SkipLast(1)
                .Append(changed).ToArray()
        };
        return await CommitAsync(session, updated, cancellationToken);
    }

    /// <summary>Only Tests/Recette content, not an arbitrary project note.</summary>
    /// <param name="relativeProjectPath">Path below the project root.</param>
    /// <returns>True for a test-definition folder.</returns>
    private static bool IsTestFolder(string relativeProjectPath)
    {
        string[] segments = relativeProjectPath.Split('/');
        return segments.Length >= 2 &&
            segments.SkipLast(1).Any(segment =>
                string.Equals(segment, "Tests",
                    StringComparison.OrdinalIgnoreCase) ||
                string.Equals(segment, "Recette",
                    StringComparison.OrdinalIgnoreCase)) &&
            !segments.SkipLast(1).Any(segment =>
                string.Equals(segment, "Campagnes",
                    StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Validates a human-supplied one-line field.</summary>
    /// <param name="value">Human value.</param>
    /// <param name="maximum">Maximum characters.</param>
    /// <returns>Whether it may be recorded.</returns>
    private static bool HumanField(string? value, int maximum) =>
        !string.IsNullOrWhiteSpace(value) &&
        value.Length <= maximum && !value.Any(char.IsControl);
}
