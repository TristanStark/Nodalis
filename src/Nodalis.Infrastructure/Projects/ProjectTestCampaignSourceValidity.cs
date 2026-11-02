using System.Security.Cryptography;
using System.Text;
using Nodalis.Core.Links;
using Nodalis.Core.Tests;

namespace Nodalis.Infrastructure.Projects;

/// <summary>
/// Current source state is independent from the historic human verdict.
/// Even a Passed test may need requalification after a source change.
/// </summary>
public enum ProjectTestSourceValidity
{
    Current,
    Modified,
    Missing,
    Ambiguous,
    IndexOutOfDate
}

/// <summary>Read-only comparison of one frozen test against its live source.</summary>
/// <param name="PassageId">Historic passage identity.</param>
/// <param name="TestId">Frozen test identity.</param>
/// <param name="Reference">Readable reference at passage start.</param>
/// <param name="Validity">Conservative current-source status.</param>
/// <param name="FrozenSha256">Historic immutable section fingerprint.</param>
/// <param name="CurrentSha256">Current section fingerprint, when identifiable.</param>
/// <param name="Detail">Reason requiring review, never a verdict.</param>
public sealed record ProjectTestSourceValidityRow(
    Guid PassageId, Guid TestId, string Reference,
    ProjectTestSourceValidity Validity,
    string FrozenSha256, string? CurrentSha256, string Detail);

/// <summary>
/// Current validity diagnostics for the selected REC. The rows never modify
/// passages, outcomes, EXI requirements, or their human validation state.
/// </summary>
/// <param name="Rows">One row for each frozen passage/test occurrence.</param>
public sealed record ProjectTestSourceValidityOverview(
    IReadOnlyList<ProjectTestSourceValidityRow> Rows)
{
    /// <summary>How many historic occurrences need source requalification.</summary>
    public int NeedsReview => Rows.Count(row =>
        row.Validity != ProjectTestSourceValidity.Current);
}

/// <summary>
/// On-demand validity against one refreshed shared index and live Markdown.
/// Each cahier is read and parsed at most once regardless of passage count.
/// </summary>
public sealed partial class ProjectTestCampaignService
{
    /// <summary>
    /// Detects changes without changing historical snapshots. A source must
    /// belong to this project, match the shared index, and resolve to a
    /// globally unique block GUID before it can be reported Current.
    /// </summary>
    /// <param name="projectDirectory">Project owning the REC campaign.</param>
    /// <param name="snapshot">Previously displayed campaign and SHA.</param>
    /// <param name="index">Fresh shared link-index snapshot.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    /// <returns>Current-source validity, never conformity or execution.</returns>
    public async Task<ProjectTestSourceValidityOverview>
        AssessSourceValidityAsync(
            string projectDirectory, ProjectTestCampaignEntry snapshot,
            LinkIndexCatalog index,
            CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(index);
        (string directory, Nodalis.Core.Domain.ProjectManifest manifest) =
            await ResolveProjectAsync(projectDirectory, cancellationToken);
        ProjectTestCampaignCatalog catalog = await ListInProjectAsync(
            directory, manifest.Id, cancellationToken);
        ProjectTestCampaignEntry? currentCampaign =
            catalog.Campaigns.SingleOrDefault(item =>
                item.Campaign.Id == snapshot.Campaign.Id &&
                string.Equals(item.RelativePath, snapshot.RelativePath,
                    StringComparison.OrdinalIgnoreCase) &&
                string.Equals(item.SourceSha256, snapshot.SourceSha256,
                    StringComparison.OrdinalIgnoreCase));
        if (currentCampaign is null)
        {
            throw new InvalidDataException(
                "Campagne REC modifiée, déplacée ou ambiguë : actualisez.");
        }

        string prefix = Path.GetRelativePath(_workspaceRoot, directory)
            .Replace('\\', '/') + "/";
        Dictionary<Guid, IndexedMarkdownBlock[]> blocks = index.Blocks
            .GroupBy(item => item.Id)
            .ToDictionary(group => group.Key, group => group.ToArray());
        Dictionary<Guid, LinkTargetEntry[]> owners = index.Targets
            .GroupBy(item => item.Id)
            .ToDictionary(group => group.Key, group => group.ToArray());
        Dictionary<string, LinkTargetEntry[]> documents = index.Targets
            .Where(item => item.Kind == LinkTargetKind.Document)
            .GroupBy(item => item.RelativePath,
                StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.ToArray(),
                StringComparer.OrdinalIgnoreCase);
        Dictionary<string, (ProjectTestDefinitionScan Scan, string Sha)>
            sources = new(StringComparer.OrdinalIgnoreCase);
        HashSet<string> missing = new(StringComparer.OrdinalIgnoreCase);
        HashSet<string> stale = new(StringComparer.OrdinalIgnoreCase);
        List<ProjectTestSourceValidityRow> rows = [];

        foreach (ProjectTestPassage passage in currentCampaign.Campaign.Passages)
        {
            foreach (ProjectTestFrozenDefinition frozen in passage.Tests)
            {
                cancellationToken.ThrowIfCancellationRequested();
                string path = frozen.SourceRelativePath.Replace('\\', '/');
                ProjectTestSourceValidity state;
                string? currentHash = null;
                string detail;

                if (!path.StartsWith(prefix,
                        StringComparison.OrdinalIgnoreCase) ||
                    !IsTestFolder(path[prefix.Length..]))
                {
                    state = ProjectTestSourceValidity.Ambiguous;
                    detail = "Source du test hors du projet ou du dossier Tests/Recette.";
                }
                else if (!blocks.TryGetValue(frozen.TestId,
                        out IndexedMarkdownBlock[]? blockOwners) ||
                    blockOwners.Length != 1 ||
                    owners.ContainsKey(frozen.TestId) ||
                    !owners.TryGetValue(blockOwners[0].DocumentId,
                        out LinkTargetEntry[]? owningDocuments) ||
                    owningDocuments.Length != 1 ||
                    !string.Equals(owningDocuments[0].RelativePath,
                        path, StringComparison.OrdinalIgnoreCase) ||
                    !documents.TryGetValue(path,
                        out LinkTargetEntry[]? indexedDocs) ||
                    indexedDocs.Length != 1)
                {
                    state = ProjectTestSourceValidity.Ambiguous;
                    detail = "GUID de bloc absent, déplacé ou dupliqué dans l'index.";
                }
                else
                {
                    if (!sources.ContainsKey(path) &&
                        !missing.Contains(path) && !stale.Contains(path))
                    {
                        try
                        {
                            string full = _sources.ResolveExistingFile(path);
                            FileInfo info = new(full);
                            if (info.Length > MaxTestSourceBytes)
                            {
                                stale.Add(path);
                            }
                            else
                            {
                                byte[] bytes = await File.ReadAllBytesAsync(
                                    full, cancellationToken);
                                string sourceSha = Convert.ToHexString(
                                    SHA256.HashData(bytes));
                                if (!string.Equals(sourceSha,
                                        indexedDocs[0].ContentHash,
                                        StringComparison.OrdinalIgnoreCase))
                                {
                                    stale.Add(path);
                                }
                                else
                                {
                                    string text = new UTF8Encoding(false, true)
                                        .GetString(bytes);
                                    sources.Add(path, (
                                        ProjectTestDefinitionCodec.Inspect(text),
                                        sourceSha));
                                }
                            }
                        }
                        catch (Exception exception) when (
                            exception is IOException or
                            UnauthorizedAccessException or
                            InvalidDataException or
                            DecoderFallbackException)
                        {
                            missing.Add(path);
                        }
                    }

                    if (missing.Contains(path))
                    {
                        state = ProjectTestSourceValidity.Missing;
                        detail = "Cahier supprimé, déplacé ou inaccessible.";
                    }
                    else if (stale.Contains(path))
                    {
                        state = ProjectTestSourceValidity.IndexOutOfDate;
                        detail = "Cahier modifié depuis l'index : actualisez.";
                    }
                    else
                    {
                        ProjectTestDefinition[] matches =
                            sources[path].Scan.Tests.Where(item =>
                                item.Id == frozen.TestId &&
                                item.CanFreeze).ToArray();
                        if (matches.Length != 1 ||
                            matches[0].HeadingLine !=
                                blockOwners[0].HeadingLine)
                        {
                            state = ProjectTestSourceValidity.Ambiguous;
                            detail = "GUID de test malformé, répété ou non synchronisé.";
                        }
                        else
                        {
                            currentHash = matches[0].Sha256;
                            state = string.Equals(currentHash,
                                frozen.Sha256,
                                StringComparison.OrdinalIgnoreCase)
                                ? ProjectTestSourceValidity.Current
                                : ProjectTestSourceValidity.Modified;
                            detail = state == ProjectTestSourceValidity.Current
                                ? "Définition identique au passage figé."
                                : "Définition modifiée : requalification humaine requise.";
                        }
                    }
                }

                rows.Add(new ProjectTestSourceValidityRow(
                    passage.Id, frozen.TestId, frozen.Reference, state,
                    frozen.Sha256, currentHash, detail));
            }
        }

        return new ProjectTestSourceValidityOverview(rows);
    }
}
