using Nodalis.Core.Domain;
using Nodalis.Core.Reliability;
using Nodalis.Infrastructure.Links;
using Nodalis.Infrastructure.Persistence;

namespace Nodalis.Infrastructure.Reliability;

/// <summary>
/// Performs deterministic, read-only workspace integrity checks and exposes explicit derived-index repair.
/// </summary>
public sealed partial class WorkspaceIntegrityDiagnosticService
{
    private readonly string _workspaceRoot;
    private readonly string _linkIndexPath;
    private readonly WorkspaceLinkIndexService _linkIndexService;

    /// <summary>
    /// Initializes a diagnostic service for one workspace root.
    /// </summary>
    /// <param name="workspaceRoot">The workspace root directory.</param>
    public WorkspaceIntegrityDiagnosticService(string workspaceRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRoot);

        _workspaceRoot = Path.GetFullPath(workspaceRoot);
        _linkIndexPath = Path.Combine(
            _workspaceRoot,
            WorkspaceLayout.LinkIndexFileName);
        _linkIndexService = new WorkspaceLinkIndexService(_workspaceRoot);
    }

    /// <summary>
    /// Scans the workspace without modifying manifests, Markdown documents or derived indexes.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A deterministic integrity report.</returns>
    public async Task<WorkspaceIntegrityReport> ScanAsync(
            CancellationToken cancellationToken = default)
    {
        global::System.Collections.Generic.List<WorkspaceIntegrityIssue> issues =
            new List<WorkspaceIntegrityIssue>();
        global::System.Collections.Generic.List<IdentityEntry> identities =
            new List<IdentityEntry>();
        global::System.Collections.Generic.List<ApplicationEntry> applications =
            new List<ApplicationEntry>();
        global::System.Collections.Generic.List<ModuleEntry> modules =
            new List<ModuleEntry>();
        global::System.Collections.Generic.List<ProjectEntry> projects =
            new List<ProjectEntry>();

        string workspaceManifestPath = Path.Combine(
            _workspaceRoot,
            WorkspaceLayout.WorkspaceManifestFileName);

        WorkspaceManifest? workspace = await ReadManifestAsync<WorkspaceManifest>(
            workspaceManifestPath,
            "WORKSPACE_MANIFEST_MISSING",
            "WORKSPACE_MANIFEST_INVALID",
            "Le manifest du workspace est absent.",
            "Le manifest du workspace est illisible ou corrompu.",
            issues,
            cancellationToken);

        if (workspace is not null)
        {
            AddIdentity(
                identities,
                workspace.Id,
                "workspace",
                workspace.Name,
                ".",
                "root");

            if (workspace.SchemaVersion > WorkspaceManifest.CurrentSchemaVersion)
            {
                issues.Add(CreateIssue(
                    WorkspaceIntegritySeverity.Error,
                    "WORKSPACE_SCHEMA_UNSUPPORTED",
                    $"Le schéma workspace {workspace.SchemaVersion} est plus récent que la version prise en charge ({WorkspaceManifest.CurrentSchemaVersion}).",
                    WorkspaceLayout.WorkspaceManifestFileName,
                    workspace.Id));
            }
        }

        string applicationsRoot = Path.Combine(
            _workspaceRoot,
            WorkspaceLayout.ApplicationsDirectoryName);

        if (!Directory.Exists(applicationsRoot))
        {
            issues.Add(CreateIssue(
                WorkspaceIntegritySeverity.Error,
                "APPLICATIONS_DIRECTORY_MISSING",
                "Le dossier structurel Applications est absent.",
                WorkspaceLayout.ApplicationsDirectoryName));
        }
        else
        {
            foreach (string applicationDirectory in EnumerateChildDirectories(
                         applicationsRoot,
                         issues))
            {
                await ScanApplicationAsync(
                    applicationDirectory,
                    identities,
                    applications,
                    modules,
                    projects,
                    issues,
                    cancellationToken);
            }
        }

        ValidateIdentityIntegrity(identities, issues);
        ValidateRelationships(applications, modules, projects, issues);
        await ValidateLinkIndexAsync(issues, cancellationToken);

        global::System.Collections.Generic.List<WorkspaceIntegrityIssue> ordered =
            issues
                .OrderBy(issue =>
                    issue.Severity == WorkspaceIntegritySeverity.Error ? 0 : 1)
                .ThenBy(issue => issue.Code, StringComparer.Ordinal)
                .ThenBy(
                    issue => issue.RelativePath ?? string.Empty,
                    StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(
                    issue => issue.Message,
                    StringComparer.CurrentCultureIgnoreCase)
                .ToList();

        return new WorkspaceIntegrityReport
        {
            GeneratedUtc = DateTimeOffset.UtcNow,
            Issues = ordered
        };
    }

    /// <summary>
    /// Explicitly rebuilds derived workspace indexes without changing business documents or manifests.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task representing the rebuild.</returns>
    public async Task RebuildDerivedIndexesAsync(
            CancellationToken cancellationToken = default)
    {
        await _linkIndexService.RefreshAsync(cancellationToken);
    }
}
