using System.Security.Cryptography;
using System.Text;
using Nodalis.Core.Maintenance;
using Nodalis.Core.Projects;

namespace Nodalis.Infrastructure.Projects;

/// <summary>One stable, project-local incident from the canonical Markdown.</summary>
/// <param name="Id">INC-prefixed persisted GUID.</param>
/// <param name="Summary">Original human-readable description.</param>
/// <param name="RelativePath">Workspace-relative maintenance document.</param>
/// <param name="LineNumber">Original incident heading line.</param>
public sealed record ProjectGateIncidentCandidate(
    string Id, string Summary, string RelativePath, int LineNumber)
{
    /// <summary>Short human-readable picker label.</summary>
    public string Label => Summary + " · " + Id;

    /// <summary>Source navigation hint, not a persistent identity.</summary>
    public string Location => RelativePath + ":" + LineNumber;

    /// <summary>
    /// Human-entered maintenance status. Never interpreted as a guaranteed
    /// incident resolution by a read-only closure inventory.
    /// </summary>
    public string Status { get; init; } = string.Empty;
}

/// <summary>Read-only eligible incidents and excluded legacy count.</summary>
/// <param name="Candidates">Only unique, complete, stable-ID incident blocks.</param>
/// <param name="ExcludedCount">Malformed or repeated incident candidates.</param>
public sealed record ProjectGateIncidentCatalog(
    IReadOnlyList<ProjectGateIncidentCandidate> Candidates,
    int ExcludedCount)
{
    /// <summary>
    /// Fingerprint of the maintenance Markdown actually parsed, or null when
    /// the file is absent; useful for source-consistent human closure reviews.
    /// </summary>
    public string? SourceSha256 { get; init; }
}

/// <summary>
/// Resolves only a project's own Fiche de Maintenance.md, never task positions,
/// foreign projects, fuzzy titles or arbitrary recursive filesystem searches.
/// </summary>
public sealed class ProjectGateIncidentCatalogService
{
    private readonly ProjectJourneySourceService _sources;

    /// <summary>Creates a workspace-confined read-only incident catalog.</summary>
    /// <param name="workspaceRoot">Active workspace root.</param>
    public ProjectGateIncidentCatalogService(string workspaceRoot)
    {
        _sources = new ProjectJourneySourceService(workspaceRoot);
    }

    /// <summary>
    /// Discovers stable canonical incidents without creating or migrating
    /// source files. Legacy records and duplicate IDs are not linkable.
    /// </summary>
    /// <param name="projectDirectory">Already existing project.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    /// <returns>Verified link candidates and exclusion diagnostics.</returns>
    public async Task<ProjectGateIncidentCatalog> ListAsync(
        string projectDirectory, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectDirectory);
        string path = Path.Combine(Path.GetFullPath(projectDirectory),
            "Fiche de Maintenance.md");
        if (!File.Exists(path))
        {
            return new ProjectGateIncidentCatalog([], 0);
        }

        // Security check rejects paths escaping the workspace and all
        // traversed symbolic links, including the maintenance source itself.
        string relative = _sources.GetReferenceForExistingFile(path);
        string resolved = _sources.ResolveExistingFile(relative);
        FileInfo info = new(resolved);
        if (info.Length > 25 * 1024 * 1024)
        {
            throw new InvalidDataException(
                "Fiche maintenance trop grande pour la revue des incidents.");
        }

        byte[] bytes = await File.ReadAllBytesAsync(
            resolved, cancellationToken);
        string markdown = new UTF8Encoding(false, true).GetString(bytes)
            .TrimStart('\uFEFF');
        IReadOnlyList<MaintenanceHistoryItem> items =
            MaintenanceHistoryIndex.Parse(resolved,
                Path.GetFileName(projectDirectory), markdown);
        MaintenanceHistoryItem[] incidents = items
            .Where(item => item.Kind == MaintenanceRecordKind.Incident).ToArray();
        HashSet<string> uniqueIds = incidents
            .Where(item => ProjectGateValidation.IsIncidentId(item.Id))
            .GroupBy(item => item.Id!, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() == 1)
            .Select(group => group.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        ProjectGateIncidentCandidate[] candidates = incidents
            .Where(item => !item.IsIncomplete && item.Id is not null &&
                uniqueIds.Contains(item.Id))
            .Select(item => new ProjectGateIncidentCandidate(
                item.Id!, item.Summary, relative, item.LineNumber)
            { Status = item.Status })
            .OrderBy(item => item.Summary, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(item => item.Id, StringComparer.OrdinalIgnoreCase).ToArray();
        return new ProjectGateIncidentCatalog(
            candidates, incidents.Length - candidates.Length)
        {
            SourceSha256 = Convert.ToHexString(SHA256.HashData(bytes))
        };
    }
}
