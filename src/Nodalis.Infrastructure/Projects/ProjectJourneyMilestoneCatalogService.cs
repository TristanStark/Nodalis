using Nodalis.Core.Domain;
using Nodalis.Core.Milestones;
using Nodalis.Infrastructure.Milestones;
using Nodalis.Infrastructure.Persistence;

namespace Nodalis.Infrastructure.Projects;

/// <summary>
/// Read-only bridge from canonical project milestones to journey references.
/// Only GUIDs physically stored in an Id column are eligible for new links;
/// positional legacy IDs and ambiguous duplicate GUIDs are never proposed.
/// Scans happen only upon an explicit user command, not while drawing WPF tabs.
/// </summary>
public sealed class ProjectJourneyMilestoneCatalogService
{
    private readonly string _workspaceRoot;
    private readonly ProjectJourneySourceService _sources;
    private readonly WorkspaceMilestoneService _milestones;

    /// <summary>Creates a read-only milestone catalog for one workspace.</summary>
    /// <param name="workspaceRoot">Workspace root.</param>
    public ProjectJourneyMilestoneCatalogService(string workspaceRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRoot);
        _workspaceRoot = Path.GetFullPath(workspaceRoot);
        _sources = new ProjectJourneySourceService(_workspaceRoot);
        _milestones = new WorkspaceMilestoneService(_workspaceRoot);
    }

    /// <summary>
    /// Reads a project's canonical milestone file without creating/migrating
    /// missing IDs. Unstable legacy rows and duplicate IDs are excluded.
    /// </summary>
    /// <param name="projectDirectory">Existing project directory.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Explicitly selectable persistent milestones and diagnostics.</returns>
    public async Task<ProjectJourneyMilestoneCatalog> ListAsync(
        string projectDirectory, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectDirectory);
        string projectPath = Path.GetFullPath(projectDirectory);
        string manifestPath = Path.Combine(projectPath,
            WorkspaceLayout.ProjectManifestFileName);
        string manifestReference = _sources.GetReferenceForExistingFile(manifestPath);
        // The source check above verifies containment and all ancestors.
        _sources.ResolveExistingFile(manifestReference);
        ProjectManifest manifest = await AtomicJsonFile.ReadAsync<ProjectManifest>(
            manifestPath, cancellationToken);
        if (manifest.Id == Guid.Empty)
        {
            throw new InvalidDataException("Projet sans identité stable.");
        }

        IReadOnlyList<MilestoneItem> milestones =
            await _milestones.GetMilestonesAsync(projectPath, cancellationToken);
        MilestoneItem[] inProject = milestones.Where(item =>
            item.ProjectId == manifest.Id).ToArray();
        Dictionary<string, string[]> fileLines = new(
            StringComparer.OrdinalIgnoreCase);
        List<MilestoneItem> persisted = [];
        int legacy = 0;
        foreach (MilestoneItem milestone in inProject)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!ProjectJourneySourcePath.IsSafe(milestone.SourceRelativePath))
            {
                legacy++;
                continue;
            }

            string path;
            try
            {
                path = _sources.ResolveExistingFile(milestone.SourceRelativePath);
            }
            catch (Exception exception) when (exception is IOException or
                ArgumentException or UnauthorizedAccessException or
                InvalidDataException)
            {
                legacy++;
                continue;
            }

            if (!fileLines.TryGetValue(path, out string[]? lines))
            {
                lines = await File.ReadAllLinesAsync(path, cancellationToken);
                fileLines.Add(path, lines);
            }

            if (IsPersistedId(milestone, lines))
            {
                persisted.Add(milestone);
            }
            else
            {
                legacy++;
            }
        }

        Guid[] ambiguous = persisted.GroupBy(item => item.Id)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key).ToArray();
        HashSet<Guid> collisions = ambiguous.ToHashSet();
        ProjectJourneyMilestoneCandidate[] candidates = persisted
            .Where(item => !collisions.Contains(item.Id))
            .Select(item => new ProjectJourneyMilestoneCandidate(
                item.Id, item.Name, item.Status, item.TargetDate,
                item.SourceRelativePath, item.LineNumber))
            .OrderBy(item => item.TargetDate ?? DateOnly.MaxValue)
            .ThenBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
        return new ProjectJourneyMilestoneCatalog(candidates,
            legacy, ambiguous);
    }

    /// <summary>
    /// Revalidates the chosen ID against current canonical Markdown before a
    /// journey relation is committed. Does not update milestone files.
    /// </summary>
    /// <param name="projectDirectory">Owning project.</param>
    /// <param name="milestoneId">Selected persisted milestone.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Verified milestone candidate.</returns>
    public async Task<ProjectJourneyMilestoneCandidate> RequireCurrentAsync(
        string projectDirectory, Guid milestoneId,
        CancellationToken cancellationToken = default)
    {
        if (milestoneId == Guid.Empty)
        {
            throw new ArgumentException("Jalon sans identifiant.", nameof(milestoneId));
        }

        ProjectJourneyMilestoneCatalog latest =
            await ListAsync(projectDirectory, cancellationToken);
        return latest.Candidates.SingleOrDefault(item => item.Id == milestoneId)
            ?? throw new InvalidOperationException(
                "Le jalon a été supprimé, déplacé ou n'a plus d'ID stable. " +
                "Actualisez la liste avant de créer le lien.");
    }

    /// <summary>
    /// Matches an actual persisted GUID from the table's Id column,
    /// never a synthetic positional GUID or dependency GUID.
    /// </summary>
    /// <param name="item">Parsed milestone row.</param>
    /// <param name="lines">Source document lines.</param>
    /// <returns>Whether the canonical table stores exactly that stable ID.</returns>
    private static bool IsPersistedId(
        MilestoneItem item, IReadOnlyList<string> lines)
    {
        int lineIndex = item.LineNumber - 1;
        if (lineIndex < 0 || lineIndex >= lines.Count || item.Id == Guid.Empty ||
            !string.Equals(item.RawLine, lines[lineIndex], StringComparison.Ordinal))
        {
            return false;
        }

        for (int index = lineIndex - 1; index >= 0; index--)
        {
            string line = lines[index].Trim();
            if (!line.StartsWith('|'))
            {
                // A separate non-table block ends the current table scope.
                break;
            }

            string[] headers = line.Split('|')
                .Skip(1).SkipLast(1)
                .Select(cell => cell.Trim().ToLowerInvariant())
                .ToArray();
            if (!headers.Contains("jalon"))
            {
                continue;
            }

            int idIndex = Array.FindIndex(headers, cell =>
                cell is "id" or "identifiant");
            string[] cells = lines[lineIndex].Split('|')
                .Skip(1).SkipLast(1)
                .Select(cell => cell.Trim()).ToArray();
            return idIndex >= 0 && idIndex < cells.Length &&
                Guid.TryParse(cells[idIndex], out Guid persistedId) &&
                persistedId == item.Id;
        }

        return false;
    }
}

/// <summary>One verified, non-owning milestone reference candidate.</summary>
/// <param name="Id">Stable GUID from the canonical Markdown Id column.</param>
/// <param name="Name">Existing milestone name.</param>
/// <param name="Status">Existing milestone status; never edited by linking.</param>
/// <param name="TargetDate">Existing target date.</param>
/// <param name="SourceRelativePath">Portable source document path.</param>
/// <param name="LineNumber">Source table row for navigation.</param>
public sealed record ProjectJourneyMilestoneCandidate(
    Guid Id, string Name, string Status, DateOnly? TargetDate,
    string SourceRelativePath, int LineNumber)
{
    /// <summary>Human-readable label for a selection dialog.</summary>
    public string Label => Name + " · " + Status +
        (TargetDate is DateOnly date ? " · " + date.ToString("dd/MM/yyyy") : "");

    /// <summary>File and line used by the existing Markdown editor.</summary>
    public string Location => SourceRelativePath + ":" + LineNumber;
}

/// <summary>Read-only milestone candidates and excluded-row diagnostics.</summary>
/// <param name="Candidates">Unambiguous persisted milestone IDs.</param>
/// <param name="LegacyWithoutStableId">Rows needing canonical ID migration.</param>
/// <param name="DuplicateIds">IDs occurring more than once in a project.</param>
public sealed record ProjectJourneyMilestoneCatalog(
    IReadOnlyList<ProjectJourneyMilestoneCandidate> Candidates,
    int LegacyWithoutStableId,
    IReadOnlyList<Guid> DuplicateIds);
