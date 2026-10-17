using System.Text.Json;
using Nodalis.Core.Links;
using Nodalis.Core.Reliability;
using Nodalis.Infrastructure.Persistence;

namespace Nodalis.Infrastructure.Reliability;

public sealed partial class WorkspaceIntegrityDiagnosticService
{
    /// <summary>
    /// Validates the persisted derived link index without triggering its automatic refresh behavior.
    /// </summary>
    /// <param name="issues">The report findings.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task representing the validation.</returns>
    private async Task ValidateLinkIndexAsync(
            ICollection<WorkspaceIntegrityIssue> issues,
            CancellationToken cancellationToken)
    {
        if (!File.Exists(_linkIndexPath))
        {
            issues.Add(CreateIssue(
                WorkspaceIntegritySeverity.Warning,
                "LINK_INDEX_MISSING",
                "L'index de liens dérivé est absent. Il peut être reconstruit explicitement.",
                WorkspaceLayout.LinkIndexFileName));

            return;
        }

        LinkIndexCatalog catalog;

        try
        {
            catalog = await AtomicJsonFile.ReadAsync<LinkIndexCatalog>(
                _linkIndexPath,
                cancellationToken);
        }
        catch (Exception exception) when (
            exception is IOException or
            UnauthorizedAccessException or
            InvalidDataException or
            JsonException)
        {
            issues.Add(CreateIssue(
                WorkspaceIntegritySeverity.Warning,
                "LINK_INDEX_INVALID",
                $"L'index de liens dérivé est illisible ou corrompu : {exception.Message}",
                WorkspaceLayout.LinkIndexFileName));

            return;
        }

        if (catalog.SchemaVersion != LinkIndexCatalog.CurrentSchemaVersion)
        {
            issues.Add(CreateIssue(
                WorkspaceIntegritySeverity.Warning,
                "LINK_INDEX_INVALID",
                $"L'index utilise le schéma {catalog.SchemaVersion}, attendu {LinkIndexCatalog.CurrentSchemaVersion}.",
                WorkspaceLayout.LinkIndexFileName));
        }

        global::System.Collections.Generic.HashSet<Guid> targetIds =
            catalog.Targets
                .Select(target => target.Id)
                .ToHashSet();

        foreach (IGrouping<Guid, LinkTargetEntry> duplicate in catalog.Targets
                     .GroupBy(target => target.Id)
                     .Where(group => group.Count() > 1))
        {
            issues.Add(CreateIssue(
                WorkspaceIntegritySeverity.Error,
                "DUPLICATE_INDEX_GUID",
                $"L'index contient plusieurs cibles avec le GUID {duplicate.Key:D}.",
                WorkspaceLayout.LinkIndexFileName,
                duplicate.Key));
        }

        global::System.Collections.Generic.HashSet<string> indexedDocuments =
            new HashSet<string>(
                catalog.Targets
                    .Where(target => target.Kind == LinkTargetKind.Document)
                    .Select(target =>
                        NormalizeStoredRelativePath(target.RelativePath)),
                StringComparer.OrdinalIgnoreCase);

        foreach (LinkTargetEntry target in catalog.Targets)
        {
            if (!TryResolveWorkspacePath(
                    target.RelativePath,
                    out string fullPath))
            {
                issues.Add(CreateIssue(
                    WorkspaceIntegritySeverity.Error,
                    "INDEX_PATH_OUTSIDE_WORKSPACE",
                    $"La cible indexée « {target.DisplayName} » pointe en dehors du workspace.",
                    target.RelativePath,
                    target.Id));

                continue;
            }

            bool exists =
                target.Kind == LinkTargetKind.Document
                    ? File.Exists(fullPath)
                    : Directory.Exists(fullPath);

            if (!exists)
            {
                issues.Add(CreateIssue(
                    WorkspaceIntegritySeverity.Error,
                    "REFERENCED_PATH_MISSING",
                    $"La cible indexée « {target.DisplayName} » référence un chemin absent.",
                    target.RelativePath,
                    target.Id));
            }
        }

        foreach (LinkReferenceEntry reference in catalog.References)
        {
            if (!targetIds.Contains(reference.SourceId))
            {
                issues.Add(CreateIssue(
                    WorkspaceIntegritySeverity.Warning,
                    "INDEX_SOURCE_MISSING",
                    $"Une référence de lien utilise une source absente de l'index ({reference.SourceId:D}).",
                    WorkspaceLayout.LinkIndexFileName,
                    reference.SourceId));
            }

            if (reference.TargetId is null)
            {
                issues.Add(CreateIssue(
                    WorkspaceIntegritySeverity.Warning,
                    "UNRESOLVED_LINK_REFERENCE",
                    $"Le lien « {reference.RawTarget} » n'est pas résolu.",
                    WorkspaceLayout.LinkIndexFileName,
                    reference.SourceId));
            }
            else if (!targetIds.Contains(reference.TargetId.Value))
            {
                issues.Add(CreateIssue(
                    WorkspaceIntegritySeverity.Warning,
                    "INDEX_TARGET_MISSING",
                    $"Une référence de lien pointe vers une cible absente ({reference.TargetId.Value:D}).",
                    WorkspaceLayout.LinkIndexFileName,
                    reference.TargetId.Value));
            }
        }

        foreach (global::Nodalis.Core.Relations.TypedRelationEntry relation in catalog.Relations)
        {
            if (!targetIds.Contains(relation.SourceId))
            {
                issues.Add(CreateIssue(
                    WorkspaceIntegritySeverity.Warning,
                    "INDEX_SOURCE_MISSING",
                    $"Une relation typée utilise une source absente de l'index ({relation.SourceId:D}).",
                    WorkspaceLayout.LinkIndexFileName,
                    relation.SourceId));
            }

            if (relation.TargetId is Guid relationTargetId &&
                !targetIds.Contains(relationTargetId))
            {
                issues.Add(CreateIssue(
                    WorkspaceIntegritySeverity.Warning,
                    "INDEX_TARGET_MISSING",
                    $"Une relation typée pointe vers une cible absente ({relationTargetId:D}).",
                    WorkspaceLayout.LinkIndexFileName,
                    relationTargetId));
            }
        }

        foreach (string markdownPath in EnumerateIndexedMarkdownFiles(issues))
        {
            string relativePath = NormalizeRelativePath(markdownPath);

            if (!indexedDocuments.Contains(
                    NormalizeStoredRelativePath(relativePath)))
            {
                issues.Add(CreateIssue(
                    WorkspaceIntegritySeverity.Warning,
                    "UNINDEXED_FILE",
                    "Ce fichier Markdown se trouve dans un emplacement structuré mais n'est pas référencé par l'index de liens.",
                    relativePath));
            }
        }

        DateTimeOffset newestSourceWrite = DateTimeOffset.MinValue;

        foreach (string sourcePath in EnumerateIndexSourceFiles(issues))
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                DateTimeOffset writeTime = new DateTimeOffset(
                    File.GetLastWriteTimeUtc(sourcePath),
                    TimeSpan.Zero);

                if (writeTime > newestSourceWrite)
                {
                    newestSourceWrite = writeTime;
                }
            }
            catch (Exception exception) when (
                exception is IOException or
                UnauthorizedAccessException)
            {
                issues.Add(CreateIssue(
                    WorkspaceIntegritySeverity.Warning,
                    "SOURCE_TIMESTAMP_UNREADABLE",
                    $"Impossible de lire la date de modification : {exception.Message}",
                    NormalizeRelativePath(sourcePath)));
            }
        }

        if (newestSourceWrite > catalog.UpdatedUtc)
        {
            issues.Add(CreateIssue(
                WorkspaceIntegritySeverity.Warning,
                "LINK_INDEX_STALE",
                "Des fichiers structurants ou Markdown sont plus récents que l'index de liens.",
                WorkspaceLayout.LinkIndexFileName));
        }
    }
}
