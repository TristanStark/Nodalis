using System.Text;
using Nodalis.Core.Maintenance;
using Nodalis.Core.Tests;

namespace Nodalis.Infrastructure.Projects;

/// <summary>
/// Reads REC and the existing project maintenance Markdown once for a
/// calculated anomaly view; no extra anomaly or incident store is created.
/// </summary>
public sealed class ProjectRecAnomalyService
{
    private readonly string _workspaceRoot;
    private readonly ProjectJourneySourceService _sources;

    /// <summary>Confines the projection to one existing workspace.</summary>
    /// <param name="workspaceRoot">Current canonical workspace root.</param>
    public ProjectRecAnomalyService(string workspaceRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRoot);
        _workspaceRoot = Path.GetFullPath(workspaceRoot);
        _sources = new ProjectJourneySourceService(_workspaceRoot);
    }

    /// <summary>
    /// Builds the read-only view from project-local REC and maintenance files,
    /// reporting incomplete records rather than calling them resolved.
    /// </summary>
    /// <param name="projectDirectory">Existing project directory.</param>
    /// <param name="cancellationToken">Cancel I/O without writing.</param>
    /// <returns>Historical failures, links and source diagnostics.</returns>
    public async Task<ProjectRecAnomalyOverview> ReadAsync(
        string projectDirectory, CancellationToken cancellationToken = default)
    {
        ProjectTestCampaignCatalog catalog =
            await new ProjectTestCampaignService(_workspaceRoot).ListAsync(
                projectDirectory, cancellationToken);
        List<string> warnings = [.. catalog.Diagnostics];
        List<MaintenanceRecord> maintenance = [];
        IReadOnlyList<ProjectRecRevalidationEvent> decisions = [];

        string fullProject = Path.GetFullPath(projectDirectory);
        string sheet = Path.Combine(fullProject, "Fiche de Maintenance.md");
        if (!File.Exists(sheet))
        {
            warnings.Add("Fiche de maintenance absente : liens INC " +
                "historiques non vérifiables.");
        }
        else
        {
            string relative = _sources.GetReferenceForExistingFile(sheet);
            string verified = _sources.ResolveExistingFile(relative);
            FileInfo info = new(verified);
            if (info.Length > 25 * 1024 * 1024)
            {
                warnings.Add("Fiche de maintenance trop volumineuse : " +
                    "état des incidents non vérifiable.");
            }
            else
            {
                byte[] bytes = await File.ReadAllBytesAsync(
                    verified, cancellationToken);
                string markdown;
                try
                {
                    markdown = new UTF8Encoding(false, true)
                        .GetString(bytes);
                }
                catch (DecoderFallbackException exception)
                {
                    throw new InvalidDataException(
                        "Fiche de maintenance UTF-8 illisible.", exception);
                }

                MaintenanceParseResult parsed =
                    MaintenanceMarkdownCodec.Parse(markdown);
                maintenance.AddRange(parsed.Records);
                decisions = ProjectRecRevalidationCodec.Read(markdown);
                foreach (MaintenanceDiagnostic item in parsed.Diagnostics)
                {
                    warnings.Add("Maintenance ligne " + item.LineNumber +
                        " : " + item.Message);
                }
            }
        }

        Guid projectId = catalog.Campaigns.Select(item =>
            item.Campaign.ProjectId).Distinct().SingleOrDefault();
        if (projectId == Guid.Empty && catalog.Campaigns.Count == 0)
        {
            // No REC exists: the source manifest is validated by ListAsync
            // above. A read-only empty projection cannot infer a project
            // identity, so show a zero-row warning without writing anything.
            warnings.Add("Aucune campagne REC dans ce projet.");
            return new ProjectRecAnomalyOverview([], warnings);
        }

        ProjectRecAnomalyOverview overview =
            ProjectRecAnomalyProjection.Build(projectId,
                catalog.Campaigns.Select(item => item.Campaign).ToArray(),
                maintenance, warnings);
        if (decisions.Count == 0 || overview.IncidentFollowups.Count == 0)
        {
            return overview;
        }

        string currentSha = ProjectRecRevalidationService.RecFingerprint(catalog);
        return overview with
        {
            IncidentFollowups = overview.IncidentFollowups.Select(item =>
            {
                ProjectRecRevalidationEvent? last =
                    ProjectRecRevalidationCodec.Latest(
                        decisions, item.IncidentId);
                return item with
                {
                    LatestHumanDecision = last,
                    LatestDecisionUsesCurrentRec =
                        last is not null &&
                        string.Equals(last.RecRegistrySha256, currentSha,
                            StringComparison.OrdinalIgnoreCase)
                };
            }).ToArray()
        };
    }
}
