using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Nodalis.Core.Milestones;
using Nodalis.Core.Tasks;
using Nodalis.Core.Tests;
using Nodalis.Infrastructure.Milestones;
using Nodalis.Infrastructure.Persistence;

namespace Nodalis.Infrastructure.Projects;

/// <summary>
/// Read-only, explicitly requested snapshot for other closure workstreams.
/// The source hash includes every inspected project-local Markdown file,
/// including documents without current open tasks.
/// </summary>
public sealed record ProjectClosureAdditionalSnapshot(
    IReadOnlyList<ProjectClosureAuditItem> Items,
    IReadOnlyList<string> Diagnostics, string Fingerprint);

/// <summary>
/// Inspects only the owning project's tree, excluding nested project manifests
/// and linked directories. No global link-index refresh or migration occurs.
/// </summary>
public sealed class ProjectClosureAdditionalInventoryService
{
    private const int MaxMarkdownFiles = 2000;
    private const long MaxTotalBytes = 96 * 1024 * 1024;
    private const long MaxSingleFileBytes = 2 * 1024 * 1024;
    private readonly string _root;
    private readonly ProjectJourneySourceService _sources;

    /// <summary>Creates a workspace-scoped, on-demand audit reader.</summary>
    /// <param name="workspaceRoot">Single workspace boundary.</param>
    public ProjectClosureAdditionalInventoryService(string workspaceRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRoot);
        _root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(workspaceRoot));
        _sources = new ProjectJourneySourceService(_root);
    }

    /// <summary>
    /// Reads REC and INC registries plus local Markdown checkboxes and jalons.
    /// Every excluded or ambiguous source is reported, never silently treated
    /// as a completed object or given a fabricated stable identifier.
    /// </summary>
    /// <param name="projectDirectory">Validated project-owned directory.</param>
    /// <param name="projectId">Authoritative manifest identity from the core audit.</param>
    /// <param name="cancellationToken">Cancellation of an explicit user request.</param>
    /// <returns>Frozen additional follow-ups and source fingerprint.</returns>
    public async Task<ProjectClosureAdditionalSnapshot> ReadAsync(
        string projectDirectory, Guid projectId,
        CancellationToken cancellationToken = default)
    {
        if (projectId == Guid.Empty)
        {
            throw new InvalidDataException("Projet sans identité persistante.");
        }

        string directory = Path.TrimEndingDirectorySeparator(
            Path.GetFullPath(projectDirectory));
        string manifestPath = _sources.ResolveExistingFile(
            _sources.GetReferenceForExistingFile(Path.Combine(
                directory, WorkspaceLayout.ProjectManifestFileName)));
        Nodalis.Core.Domain.ProjectManifest manifest =
            await AtomicJsonFile.ReadAsync<Nodalis.Core.Domain.ProjectManifest>(
                manifestPath, cancellationToken);
        if (manifest.Id != projectId)
        {
            throw new InvalidDataException(
                "Projet modifié depuis le lancement du pré-audit.");
        }

        List<ProjectClosureAuditItem> items = [];
        List<string> diagnostics = [];
        StringBuilder revisions = new();
        revisions.AppendLine(projectId.ToString("D"));

        ProjectTestCampaignCatalog campaigns =
            await new ProjectTestCampaignService(_root)
                .ListAsync(directory, cancellationToken);
        diagnostics.AddRange(campaigns.Diagnostics.Select(
            item => "Campagnes REC : " + item));
        foreach (ProjectTestCampaignEntry entry in campaigns.Campaigns)
        {
            if (entry.Campaign.ProjectId != projectId)
            {
                throw new InvalidDataException(
                    "Campagne REC étrangère dans le pré-audit.");
            }

            revisions.Append("REC|").Append(entry.Campaign.Id).Append('|')
                .AppendLine(entry.SourceSha256);
            if (entry.Campaign.State is ProjectTestCampaignState.Closed or
                ProjectTestCampaignState.Canceled)
            {
                continue;
            }

            items.Add(new ProjectClosureAuditItem(
                "Campagnes REC", entry.Campaign.Id,
                entry.Campaign.Reference, entry.Campaign.Title,
                "cycle " + entry.Campaign.State +
                    " · résultats et réserves à revoir",
                entry.RelativePath));
        }

        ProjectGateIncidentCatalog incidents =
            await new ProjectGateIncidentCatalogService(_root)
                .ListAsync(directory, cancellationToken);
        revisions.Append("INC|")
            .AppendLine(incidents.SourceSha256 ?? "(absent)");
        if (incidents.ExcludedCount > 0)
        {
            diagnostics.Add("Maintenance : " + incidents.ExcludedCount +
                " incident(s) sans ID stable unique ou enregistrement complet.");
        }

        foreach (ProjectGateIncidentCandidate entry in incidents.Candidates)
        {
            if (!Guid.TryParseExact(entry.Id["INC-".Length..], "N",
                out Guid identity) || identity == Guid.Empty)
            {
                diagnostics.Add("Maintenance : identité INC non reconnue.");
                continue;
            }

            // Maintenance does not expose a standardized closed/open status.
            // Every incident requires a human review, including those whose
            // free-text Status sounds resolved.
            string summary = entry.Summary.Length <= 240
                ? entry.Summary : entry.Summary[..240];
            string detail = "statut déclaré : " +
                (string.IsNullOrWhiteSpace(entry.Status)
                    ? "non renseigné" : entry.Status) +
                " · clôture non déduite";
            items.Add(new ProjectClosureAuditItem(
                "Incidents à vérifier", identity, entry.Id, summary,
                detail.Length <= 1000 ? detail : detail[..1000],
                entry.RelativePath));
        }

        // Enumerate only this project and its ordinary descendant folders.
        // A nested .project.json marks a different owner, not a subfolder to
        // silently mix into this project's audit.
        Stack<string> pending = new();
        pending.Push(directory);
        List<string> markdownFiles = [];
        while (pending.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string current = pending.Pop();
            if (!string.Equals(current, directory,
                    StringComparison.OrdinalIgnoreCase) &&
                File.Exists(Path.Combine(current,
                    WorkspaceLayout.ProjectManifestFileName)))
            {
                continue;
            }

            if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
            {
                diagnostics.Add("Tâches : répertoire lié ignoré.");
                continue;
            }

            foreach (string entry in Directory.EnumerateFileSystemEntries(current)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
            {
                cancellationToken.ThrowIfCancellationRequested();
                FileAttributes attributes = File.GetAttributes(entry);
                if ((attributes & FileAttributes.ReparsePoint) != 0)
                {
                    diagnostics.Add("Tâches : source liée non inspectée.");
                    continue;
                }

                if ((attributes & FileAttributes.Directory) != 0)
                {
                    pending.Push(entry);
                }
                else if (entry.EndsWith(".md",
                    StringComparison.OrdinalIgnoreCase))
                {
                    string localPath = Path.GetRelativePath(directory, entry)
                        .Replace(Path.DirectorySeparatorChar, '/');
                    // Opaque REC/MEP ledgers are already parsed and hashed by
                    // their canonical services; never scan their embedded JSON
                    // as user checkboxes or reject valid 16 MiB REC files.
                    bool separatelyIndexed =
                        localPath.StartsWith("Tests/Campagnes/",
                            StringComparison.OrdinalIgnoreCase) ||
                        localPath.StartsWith("Exploitation/Déploiement/Opérations/",
                            StringComparison.OrdinalIgnoreCase) ||
                        localPath.StartsWith("Exploitation/Déploiement/Suivi/",
                            StringComparison.OrdinalIgnoreCase);
                    if (!separatelyIndexed &&
                        !string.Equals(localPath, "Management/Bilan de clôture.md",
                            StringComparison.OrdinalIgnoreCase))
                    {
                        markdownFiles.Add(entry);
                    }
                }

                if (markdownFiles.Count > MaxMarkdownFiles)
                {
                    throw new InvalidDataException(
                        "Trop de fichiers Markdown pour un pré-audit fiable. " +
                        "Scindez le périmètre avant de signer le bilan.");
                }
            }
        }

        long inspectedBytes = 0;
        List<ProjectClosureAuditItem> taskItems = [];
        foreach (string path in markdownFiles.OrderBy(path => path,
            StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();
            string relative = _sources.GetReferenceForExistingFile(path);
            string verified = _sources.ResolveExistingFile(relative);
            FileInfo source = new(verified);
            long singleLimit = string.Equals(
                Path.GetRelativePath(directory, verified),
                "Fiche de Maintenance.md", StringComparison.OrdinalIgnoreCase)
                ? 25 * 1024 * 1024 : MaxSingleFileBytes;
            if (source.Length > singleLimit ||
                inspectedBytes + source.Length > MaxTotalBytes)
            {
                throw new InvalidDataException(
                    "Pré-audit des tâches trop volumineux : " +
                    "aucun bilan incomplet ne sera signé par omission.");
            }

            byte[] bytes = await File.ReadAllBytesAsync(verified,
                cancellationToken);
            inspectedBytes += bytes.Length;
            string documentHash = Convert.ToHexString(SHA256.HashData(bytes));
            if (string.Equals(Path.GetRelativePath(directory, verified),
                    "Fiche de Maintenance.md", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(documentHash, incidents.SourceSha256,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    "Fiche de maintenance modifiée pendant l'inventaire " +
                    "des incidents : relancez le pré-audit.");
            }

            revisions.Append("DOC|").Append(relative).Append('|')
                .AppendLine(documentHash);
            string text = new UTF8Encoding(false, true).GetString(bytes);
            TaskPersistentIdentityScan scan =
                TaskPersistentIdentityCodec.Inspect(text.Split('\n'));
            foreach (TaskPersistentIdentityIssue issue in scan.Issues)
            {
                diagnostics.Add("Tâche : identité ambiguë dans " +
                    relative + " : ligne " + issue.LineNumber);
            }

            foreach (TaskPersistentIdentityEntry entry in scan.Entries)
            {
                if (entry.RawLine.Length == 0 ||
                    Regex.IsMatch(entry.RawLine,
                        @"^[ \t]*[-*+]\s+\[[xX]\]",
                        RegexOptions.CultureInvariant))
                {
                    continue;
                }

                if (entry.Id is not Guid id || id == Guid.Empty)
                {
                    diagnostics.Add("Tâche non terminée sans ID persistant : " +
                        relative + " : ligne " + entry.LineNumber);
                    continue;
                }

                TaskPersistentIdentityCodec.ReadMarker(
                    entry.RawLine, out string cleaned);
                int checkboxEnd = cleaned.IndexOf(']');
                string title = (checkboxEnd >= 0
                    ? cleaned[(checkboxEnd + 1)..] : cleaned).Trim();
                if (title.Length == 0)
                {
                    title = "Tâche sans libellé";
                }

                taskItems.Add(new ProjectClosureAuditItem(
                    "Tâches ouvertes", id, "TACHE-" + id.ToString("D"),
                    title.Length <= 240 ? title : title[..240],
                    "checkbox non cochée · ligne " + entry.LineNumber,
                    relative));
            }
        }

        HashSet<Guid> duplicatedTasks = taskItems.GroupBy(item => item.Id)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key).ToHashSet();
        if (duplicatedTasks.Count > 0)
        {
            diagnostics.Add("Tâches : " + duplicatedTasks.Count +
                " GUID(s) dupliqué(s) entre les documents ; pas de passation implicite.");
        }

        items.AddRange(taskItems.Where(item =>
            !duplicatedTasks.Contains(item.Id)));
        IReadOnlyList<MilestoneItem> milestones =
            await new WorkspaceMilestoneService(_root)
                .ReadProjectForClosureAsync(directory, cancellationToken);
        foreach (MilestoneItem milestone in milestones)
        {
            if (milestone.ProjectId != projectId)
            {
                throw new InvalidDataException(
                    "Jalon découvert avec un autre propriétaire.");
            }

            // The regular UI may derive a position-based ID when a pasted row
            // has no GUID. Such an ID cannot be used for a durable handover.
            if (!milestone.RawLine.Contains(
                milestone.Id.ToString("D"), StringComparison.OrdinalIgnoreCase))
            {
                diagnostics.Add("Jalon sans GUID persistant : " +
                    milestone.SourceRelativePath + " : ligne " +
                    milestone.LineNumber);
                continue;
            }

            string status = milestone.Status.Trim();
            if (status.Equals("Terminé", StringComparison.OrdinalIgnoreCase) ||
                status.Equals("Terminée", StringComparison.OrdinalIgnoreCase) ||
                status.Equals("Annulé", StringComparison.OrdinalIgnoreCase) ||
                status.Equals("Annulée", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string detail = "statut déclaré : " +
                (string.IsNullOrWhiteSpace(status) ? "inconnu" : status);
            items.Add(new ProjectClosureAuditItem(
                "Jalons à suivre", milestone.Id,
                "JALON-" + milestone.Id.ToString("D"),
                milestone.Name.Length <= 240
                    ? milestone.Name : milestone.Name[..240],
                detail.Length <= 1000 ? detail : detail[..1000],
                milestone.SourceRelativePath));
        }

        HashSet<Guid> collisions = items.GroupBy(item => item.Id)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key).ToHashSet();
        if (collisions.Count > 0)
        {
            diagnostics.Add("Inventaire : " + collisions.Count +
                " identité(s) en conflit entre registres ; " +
                "objets concernés exclus, revue manuelle requise.");
            items.RemoveAll(item => collisions.Contains(item.Id));
        }

        foreach (string diagnostic in diagnostics)
        {
            revisions.Append("DIAGNOSTIC|").AppendLine(diagnostic);
        }

        return new ProjectClosureAdditionalSnapshot(
            items.OrderBy(item => item.Kind, StringComparer.Ordinal)
                .ThenBy(item => item.Reference, StringComparer.Ordinal)
                .ToArray(),
            diagnostics.ToArray(),
            Convert.ToHexString(SHA256.HashData(
                Encoding.UTF8.GetBytes(revisions.ToString()))));
    }
}
