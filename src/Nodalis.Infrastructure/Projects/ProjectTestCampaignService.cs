using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Nodalis.Core.Domain;
using Nodalis.Core.Tests;
using Nodalis.Infrastructure.Persistence;
using Nodalis.Infrastructure.Reliability;

namespace Nodalis.Infrastructure.Projects;

/// <summary>One validated REC document and exact source revision.</summary>
/// <param name="Campaign">Frozen/history-owning source model.</param>
/// <param name="RelativePath">Workspace-relative canonical Markdown.</param>
/// <param name="SourceSha256">SHA-256 of the full approved source.</param>
public sealed record ProjectTestCampaignEntry(
    ProjectTestCampaign Campaign, string RelativePath, string SourceSha256);

/// <summary>Read-only project-local campaign catalog.</summary>
/// <param name="Campaigns">Unambiguous project-owned REC documents.</param>
/// <param name="Diagnostics">Invalid, legacy or duplicate source warnings.</param>
/// <param name="NextReference">Next unreserved REC code.</param>
public sealed record ProjectTestCampaignCatalog(
    IReadOnlyList<ProjectTestCampaignEntry> Campaigns,
    IReadOnlyList<string> Diagnostics, string? NextReference);

/// <summary>
/// Project-scoped REC Markdown registry. Creation is explicitly requested;
/// reading never creates folders or rewrites old campaign files.
/// </summary>
public sealed partial class ProjectTestCampaignService
{
    private const string TestsFolder = "Tests";
    private const string CampaignsFolder = "Campagnes";
    private const int MaxCampaignBytes = 16 * 1024 * 1024;
    private const int MaxCampaigns = 500;
    private readonly string _workspaceRoot;
    private readonly ProjectJourneySourceService _sources;

    /// <summary>Constructs a service scoped to exactly one workspace.</summary>
    /// <param name="workspaceRoot">Existing workspace root.</param>
    public ProjectTestCampaignService(string workspaceRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRoot);
        _workspaceRoot = Path.TrimEndingDirectorySeparator(
            Path.GetFullPath(workspaceRoot));
        _sources = new ProjectJourneySourceService(_workspaceRoot);
    }

    /// <summary>Reads an existing project's campaigns without mutations.</summary>
    /// <param name="projectDirectory">Existing project with manifest.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    /// <returns>Verified REC entries, diagnostics and next code.</returns>
    public async Task<ProjectTestCampaignCatalog> ListAsync(
        string projectDirectory, CancellationToken cancellationToken = default)
    {
        (string directory, ProjectManifest project) =
            await ResolveProjectAsync(projectDirectory, cancellationToken);
        return await ListInProjectAsync(
            directory, project.Id, cancellationToken);
    }

    /// <summary>
    /// Creates exactly one user-confirmed draft REC without overwriting a
    /// legacy filename or silently reallocating a reviewed reference.
    /// </summary>
    /// <param name="projectDirectory">Active project directory.</param>
    /// <param name="title">Human-entered campaign title.</param>
    /// <param name="expectedReference">Reference approved in the preview.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    /// <returns>Source-verified new draft campaign.</returns>
    public async Task<ProjectTestCampaignEntry> CreateAsync(
        string projectDirectory, string title, string expectedReference,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(title) ||
            title.Length > 240 || title.Any(char.IsControl))
        {
            throw new ArgumentException(
                "Titre de campagne requis sur une ligne (240 caractères max).",
                nameof(title));
        }

        (string directory, ProjectManifest project) =
            await ResolveProjectAsync(projectDirectory, cancellationToken);
        RequireActive(project);
        ProjectTestCampaignCatalog catalog = await ListInProjectAsync(
            directory, project.Id, cancellationToken);
        if (!string.Equals(catalog.NextReference, expectedReference,
                StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                "Référence REC réservée ou modifiée : actualisez l'aperçu.");
        }

        ProjectTestCampaign campaign = new()
        {
            Id = Guid.NewGuid(),
            ProjectId = project.Id,
            Reference = expectedReference,
            Title = title.Trim(),
            CreatedUtc = DateTimeOffset.UtcNow
        };
        string markdown = ProjectTestCampaignMarkdownCodec.Format(campaign);
        string testsFolder = Path.Combine(directory, TestsFolder);
        string campaignFolder = Path.Combine(testsFolder, CampaignsFolder);
        Directory.CreateDirectory(testsFolder);
        RequireNormalDirectory(testsFolder);
        Directory.CreateDirectory(campaignFolder);
        RequireNormalDirectory(campaignFolder);
        cancellationToken.ThrowIfCancellationRequested();
        string file = Path.Combine(campaignFolder, expectedReference + ".md");
        await AtomicFileWriter.WriteNewAllTextAsync(
            file, markdown, cancellationToken);
        return new ProjectTestCampaignEntry(
            campaign, _sources.GetReferenceForExistingFile(file),
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(markdown))));
    }

    /// <summary>
    /// Reopens an explicitly selected campaign and confirms exact project,
    /// GUID, relative path, uniqueness and SHA before any change.
    /// </summary>
    /// <param name="projectDirectory">Selected project.</param>
    /// <param name="snapshot">Previously displayed campaign revision.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    /// <returns>Validated current source and optimistic file session.</returns>
    private async Task<(string ProjectDirectory, ProjectTestCampaign Current,
        TextDocumentSession Session)> OpenVerifiedAsync(
        string projectDirectory, ProjectTestCampaignEntry snapshot,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        (string directory, ProjectManifest project) =
            await ResolveProjectAsync(projectDirectory, cancellationToken);
        RequireActive(project);
        if (snapshot.Campaign.ProjectId != project.Id ||
            snapshot.Campaign.Id == Guid.Empty ||
            !ValidSha(snapshot.SourceSha256))
        {
            throw new InvalidDataException(
                "Campagne REC absente ou hors projet.");
        }

        ProjectTestCampaignCatalog catalog = await ListInProjectAsync(
            directory, project.Id, cancellationToken);
        ProjectTestCampaignEntry? current = catalog.Campaigns.SingleOrDefault(
            item => item.Campaign.Id == snapshot.Campaign.Id &&
                string.Equals(item.RelativePath, snapshot.RelativePath,
                    StringComparison.OrdinalIgnoreCase));
        if (current is null ||
            !string.Equals(current.SourceSha256, snapshot.SourceSha256,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                "Campagne modifiée, déplacée ou ambiguë : actualisez le registre.");
        }

        string full = _sources.ResolveExistingFile(current.RelativePath);
        string expectedFolder = Path.GetFullPath(Path.Combine(
            directory, TestsFolder, CampaignsFolder));
        if (!string.Equals(
            Path.TrimEndingDirectorySeparator(
                Path.GetDirectoryName(full) ?? string.Empty),
            Path.TrimEndingDirectorySeparator(expectedFolder),
            StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                "Seul le dossier canonique de campagnes est modifiable.");
        }

        TextDocumentSession session = await TextDocumentSession.OpenAsync(
            full, cancellationToken);
        if (!string.Equals(session.Revision.Sha256, snapshot.SourceSha256,
            StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                "Campagne modifiée pendant la préparation de l'action.");
        }

        ProjectTestCampaign verified =
            ProjectTestCampaignMarkdownCodec.Parse(session.Content);
        return (directory, verified, session);
    }

    /// <summary>
    /// Publishes an explicitly approved append through atomic SHA-CAS.
    /// Concurrent writers cannot silently overwrite another user's result.
    /// </summary>
    /// <param name="session">Exact approved source session.</param>
    /// <param name="updated">New event-sourced campaign.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    /// <returns>Verified persisted campaign and SHA.</returns>
    private async Task<ProjectTestCampaignEntry> CommitAsync(
        TextDocumentSession session, ProjectTestCampaign updated,
        CancellationToken cancellationToken)
    {
        string markdown = ProjectTestCampaignMarkdownCodec.Format(updated);
        string staged = Path.Combine(Path.GetDirectoryName(session.Path)!,
            "." + Path.GetFileName(session.Path) + "." +
            Guid.NewGuid().ToString("N") + ".stage");
        try
        {
            await AtomicFileWriter.WriteNewAllTextAsync(
                staged, markdown, cancellationToken);
            string newHash = Convert.ToHexString(SHA256.HashData(
                Encoding.UTF8.GetBytes(markdown)));
            await AtomicFileWriter.ReplaceSnapshotIfUnchangedAsync(
                session.Path, session.Revision.Sha256, staged,
                newHash, cancellationToken);
            return new ProjectTestCampaignEntry(
                updated, _sources.GetReferenceForExistingFile(session.Path),
                newHash);
        }
        finally
        {
            if (File.Exists(staged))
            {
                File.Delete(staged);
            }
        }
    }

    /// <summary>Reads exactly the canonical project-local folder.</summary>
    /// <param name="directory">Validated project directory.</param>
    /// <param name="projectId">Manifest project GUID.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    /// <returns>Unambiguous entries and next unused readable reference.</returns>
    private async Task<ProjectTestCampaignCatalog> ListInProjectAsync(
        string directory, Guid projectId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string tests = Path.Combine(directory, TestsFolder);
        string campaigns = Path.Combine(tests, CampaignsFolder);
        if (!Directory.Exists(campaigns))
        {
            if (Directory.Exists(tests))
            {
                RequireNormalDirectory(tests);
            }

            return new ProjectTestCampaignCatalog([], [], "REC-0001");
        }

        RequireNormalDirectory(tests);
        RequireNormalDirectory(campaigns);
        string[] files = Directory.GetFiles(campaigns,
            "*.md", SearchOption.TopDirectoryOnly);
        if (files.Length > MaxCampaigns)
        {
            throw new InvalidDataException(
                "Trop de campagnes REC (>500) : inspection requise.");
        }

        List<ProjectTestCampaignEntry> found = [];
        List<string> diagnostics = [];
        int highest = 0;
        foreach (string file in files.OrderBy(path => path,
            StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();
            string name = Path.GetFileNameWithoutExtension(file);
            if (ProjectTestCampaignMarkdownCodec.IsReference(name))
            {
                highest = Math.Max(highest, int.Parse(name[4..],
                    CultureInfo.InvariantCulture));
            }

            try
            {
                string relative = _sources.GetReferenceForExistingFile(file);
                string verified = _sources.ResolveExistingFile(relative);
                FileInfo info = new(verified);
                if (info.Length > MaxCampaignBytes)
                {
                    diagnostics.Add(relative + " : fichier trop volumineux.");
                    continue;
                }

                byte[] bytes = await File.ReadAllBytesAsync(
                    verified, cancellationToken);
                string markdown = new UTF8Encoding(false, true).GetString(bytes);
                ProjectTestCampaign campaign =
                    ProjectTestCampaignMarkdownCodec.Parse(markdown);
                if (campaign.ProjectId != projectId ||
                    !string.Equals(name, campaign.Reference,
                        StringComparison.Ordinal))
                {
                    diagnostics.Add(relative +
                        " : référence ou projet propriétaire différent.");
                    continue;
                }

                highest = Math.Max(highest, int.Parse(
                    campaign.Reference[4..], CultureInfo.InvariantCulture));
                found.Add(new ProjectTestCampaignEntry(campaign, relative,
                    Convert.ToHexString(SHA256.HashData(bytes))));
            }
            catch (Exception exception) when (
                exception is InvalidDataException or
                DecoderFallbackException or IOException or
                UnauthorizedAccessException)
            {
                diagnostics.Add(name + ".md : " + exception.Message);
            }
        }

        HashSet<Guid> repeatedIds = found.GroupBy(item => item.Campaign.Id)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key).ToHashSet();
        HashSet<string> repeatedCodes = found.GroupBy(item =>
            item.Campaign.Reference, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key).ToHashSet(
                StringComparer.OrdinalIgnoreCase);
        foreach (ProjectTestCampaignEntry duplicate in found.Where(item =>
            repeatedIds.Contains(item.Campaign.Id) ||
            repeatedCodes.Contains(item.Campaign.Reference)))
        {
            diagnostics.Add(duplicate.RelativePath +
                " : GUID ou référence REC ambiguë.");
        }

        ProjectTestCampaignEntry[] valid = found.Where(item =>
            !repeatedIds.Contains(item.Campaign.Id) &&
            !repeatedCodes.Contains(item.Campaign.Reference))
            .OrderBy(item => item.Campaign.Reference,
                StringComparer.Ordinal).ToArray();
        string? next = highest >= 9999 ? null :
            "REC-" + (highest + 1).ToString("D4",
                CultureInfo.InvariantCulture);
        return new ProjectTestCampaignCatalog(valid, diagnostics, next);
    }

    /// <summary>Resolves only an existing project with a verified manifest.</summary>
    /// <param name="directory">Project location.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    /// <returns>Project folder and manifest.</returns>
    private async Task<(string Directory, ProjectManifest Manifest)>
        ResolveProjectAsync(string directory, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        string full = Path.TrimEndingDirectorySeparator(
            Path.GetFullPath(directory));
        string relative = Path.GetRelativePath(_workspaceRoot, full);
        if (Path.IsPathRooted(relative) ||
            relative is "." or ".." ||
            relative.StartsWith(".." + Path.DirectorySeparatorChar,
                StringComparison.Ordinal) ||
            relative.StartsWith(".." + Path.AltDirectorySeparatorChar,
                StringComparison.Ordinal))
        {
            throw new InvalidDataException("Projet hors workspace.");
        }

        string[] segments = relative.Split(Path.DirectorySeparatorChar);
        if (segments.Length < 3 ||
            !(string.Equals(segments[0],
                WorkspaceLayout.ApplicationsDirectoryName,
                StringComparison.OrdinalIgnoreCase) ||
              string.Equals(segments[0],
                WorkspaceLayout.ArchivesDirectoryName,
                StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidDataException("Dossier non reconnu comme projet.");
        }

        string manifestPath = Path.Combine(full,
            WorkspaceLayout.ProjectManifestFileName);
        _sources.ResolveExistingFile(
            _sources.GetReferenceForExistingFile(manifestPath));
        ProjectManifest manifest = await AtomicJsonFile.ReadAsync<
            ProjectManifest>(manifestPath, cancellationToken);
        if (manifest.Id == Guid.Empty)
        {
            throw new InvalidDataException("Projet sans GUID stable.");
        }

        return (full, manifest);
    }

    /// <summary>Disallows modifications to archived projects.</summary>
    /// <param name="manifest">Verified owner.</param>
    private static void RequireActive(ProjectManifest manifest)
    {
        if (manifest.ArchivedUtc.HasValue ||
            manifest.ArchiveOriginalRelativePath is not null)
        {
            throw new InvalidOperationException(
                "Une campagne archivée est consultable mais non modifiable.");
        }
    }

    /// <summary>Disallows symlinks and junctions in campaign folder.</summary>
    /// <param name="directory">Existing folder.</param>
    private static void RequireNormalDirectory(string directory)
    {
        if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0)
        {
            throw new InvalidDataException(
                "Dossier de campagnes sous lien symbolique refusé.");
        }
    }

    /// <summary>Checks a canonical SHA hex string.</summary>
    /// <param name="sha">Fingerprint.</param>
    /// <returns>True for a SHA-256 hex string.</returns>
    private static bool ValidSha(string sha) =>
        sha is { Length: 64 } && sha.All(Uri.IsHexDigit);
}
