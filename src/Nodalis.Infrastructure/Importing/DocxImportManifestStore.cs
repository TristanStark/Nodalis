using System.Security.Cryptography;
using System.Text.Json;
using System.Text;
using Nodalis.Core.Importing;
using Nodalis.Infrastructure.Persistence;

namespace Nodalis.Infrastructure.Importing;

/// <summary>
/// Reads and writes DOCX provenance inside the target project. Legacy projects
/// need no migration: an absent directory simply means no import history.
/// </summary>
public sealed class DocxImportManifestStore
{
    public const string DirectoryName = ".nodalis-imports";

    private const long MaxManifestBytes = 64L * 1024 * 1024;
    private readonly string _workspaceRoot;

    /// <summary>Creates a store without modifying the workspace.</summary>
    public DocxImportManifestStore(string workspaceRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRoot);
        _workspaceRoot = Path.GetFullPath(workspaceRoot);
    }

    /// <summary>Determines the unique, predictable JSON path for a preview import ID.</summary>
    public string GetManifestPath(string projectDirectory, Guid importId)
    {
        if (importId == Guid.Empty)
        {
            throw new InvalidDataException("Identifiant d'import DOCX manquant.");
        }

        string safeProject = EnsureWorkspacePath(projectDirectory);
        return Path.Combine(safeProject, DirectoryName,
            importId.ToString("N") + ".json");
    }

    /// <summary>Converts an existing workspace path to a portable, slash-separated path.</summary>
    public string ToWorkspaceRelativePath(string fullPath)
    {
        string safePath = EnsureWorkspacePath(fullPath);
        return Path.GetRelativePath(_workspaceRoot, safePath)
            .Replace('\\', '/');
    }

    /// <summary>
    /// Resolves a manifest's portable path for read-only inspection, rejecting
    /// absolute or escaping paths. Does not create any workspace directories.
    /// </summary>
    public string ResolveWorkspaceRelativePath(string relativePath)
    {
        ValidateRelativePath(relativePath);
        return EnsureWorkspacePath(Path.Combine(_workspaceRoot,
            relativePath.Replace('/', Path.DirectorySeparatorChar)));
    }

    /// <summary>Computes the SHA-256 of a text snapshot without normalizing it.</summary>
    public static string ComputeSha256(string content)
    {
        ArgumentNullException.ThrowIfNull(content);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(content)))
            .ToLowerInvariant();
    }

    /// <summary>Computes the checksum of the exact DOCX package bytes.</summary>
    public static async Task<string> ComputeFileSha256Async(
        string path, CancellationToken cancellationToken = default)
    {
        await using FileStream stream = new FileStream(path, FileMode.Open,
            FileAccess.Read, FileShare.Read, 81920, useAsync: true);
        byte[] digest = await SHA256.HashDataAsync(stream, cancellationToken);
        return Convert.ToHexString(digest).ToLowerInvariant();
    }

    /// <summary>
    /// Creates a new immutable provenance record atomically. Never updates an
    /// existing manifest; subsequent reimports will create a new version.
    /// </summary>
    public async Task<string> SaveNewAsync(
        string projectDirectory,
        DocxImportManifest manifest,
        CancellationToken cancellationToken = default)
    {
        Validate(manifest);
        string path = GetManifestPath(projectDirectory, manifest.ImportId);
        string directory = Path.GetDirectoryName(path)!;
        Directory.CreateDirectory(directory);
        string temporaryPath = Path.Combine(directory,
            "." + Path.GetFileName(path) + "." + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            await using (FileStream stream = new FileStream(temporaryPath,
                FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096,
                FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(stream, manifest,
                    JsonDefaults.Options, cancellationToken);
                await stream.FlushAsync(cancellationToken);
                if (stream.Length > MaxManifestBytes)
                {
                    throw new InvalidDataException(
                        "Manifeste DOCX trop volumineux pour être enregistré.");
                }

                stream.Flush(flushToDisk: true);
            }

            // File.Move without overwrite is a single atomic create in the
            // same directory. An existing import is NEVER replaced, even if
            // another process created it after this operation started.
            File.Move(temporaryPath, path);
            return path;
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    /// <summary>Reads and validates a previously written manifest, failing closed on corruption.</summary>
    public async Task<DocxImportManifest> LoadAsync(
        string path, CancellationToken cancellationToken = default)
    {
        string safePath = EnsureWorkspacePath(path);
        FileInfo info = new FileInfo(safePath);
        if (!info.Exists || info.Length > MaxManifestBytes)
        {
            throw new InvalidDataException(
                "Manifeste DOCX absent ou trop volumineux : " + safePath);
        }

        DocxImportManifest manifest;
        try
        {
            manifest = await AtomicJsonFile.ReadAsync<DocxImportManifest>(
                safePath, cancellationToken);
        }
        catch (System.Text.Json.JsonException exception)
        {
            throw new InvalidDataException("Manifeste DOCX corrompu : " + safePath,
                exception);
        }

        Validate(manifest);
        return manifest;
    }

    /// <summary>
    /// Lists all valid provenance records in a selected project, including
    /// renamed AND modified Word sources with no matching name/hash.
    /// This only supports explicit user selection; it never establishes
    /// identity or permits an automatic reimport.
    /// </summary>
    public async Task<IReadOnlyList<DocxImportManifest>> ListProjectHistoryAsync(
        string projectDirectory,
        CancellationToken cancellationToken = default)
    {
        string directory = Path.Combine(EnsureWorkspacePath(projectDirectory),
            DirectoryName);
        if (!Directory.Exists(directory))
        {
            return [];
        }

        List<DocxImportManifest> histories = [];
        foreach (string path in Directory.EnumerateFiles(directory, "*.json")
                     .OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (histories.Count >= 500)
            {
                throw new InvalidDataException(
                    "Trop d'historiques DOCX pour un seul projet.");
            }

            histories.Add(await LoadAsync(path, cancellationToken));
        }

        return histories
            .OrderByDescending(manifest => manifest.ImportedAtUtc)
            .ToArray();
    }

    /// <summary>
    /// Lists possible source associations for one project. A matching name
    /// alone is never considered proof that the document is the same source.
    /// An ambiguous association requires a later explicit user choice.
    /// </summary>
    public async Task<IReadOnlyList<DocxImportManifestMatch>> FindCandidatesAsync(
        string projectDirectory, string sourceFileName, string sourceSha256,
        CancellationToken cancellationToken = default)
    {
        string directory = Path.Combine(EnsureWorkspacePath(projectDirectory),
            DirectoryName);
        if (!Directory.Exists(directory))
        {
            return [];
        }

        if (!IsSha256(sourceSha256))
        {
            throw new InvalidDataException("Empreinte source DOCX invalide.");
        }

        List<DocxImportManifestMatch> candidates = [];
        foreach (string path in Directory.EnumerateFiles(directory, "*.json")
                     .OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();
            DocxImportManifest manifest = await LoadAsync(path, cancellationToken);
            bool sameHash = string.Equals(manifest.SourceSha256, sourceSha256,
                StringComparison.OrdinalIgnoreCase);
            bool sameName = string.Equals(manifest.SourceFileName, sourceFileName,
                StringComparison.OrdinalIgnoreCase);
            if (sameHash || sameName)
            {
                candidates.Add(new DocxImportManifestMatch
                {
                    Manifest = manifest,
                    ExactSourceHash = sameHash,
                    SameSourceFileName = sameName
                });
            }
        }

        return candidates.OrderByDescending(candidate => candidate.ExactSourceHash)
            .ThenByDescending(candidate => candidate.Manifest.ImportedAtUtc)
            .ToArray();
    }

    /// <summary>
    /// Rejects unsupported schemas, unsafe paths, duplicate fragment identities,
    /// altered snapshots and incomplete provenance.
    /// </summary>
    public void Validate(DocxImportManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        if (manifest.SchemaVersion != DocxImportManifest.CurrentSchemaVersion ||
            manifest.ImportId == Guid.Empty || manifest.ProjectId == Guid.Empty ||
            (manifest.PreviousImportId.HasValue &&
             (manifest.PreviousImportId.Value == Guid.Empty ||
              manifest.PreviousImportId.Value == manifest.ImportId)) ||
            (manifest.ReimportOperationId.HasValue &&
             (manifest.ReimportOperationId.Value == Guid.Empty ||
              manifest.PreviousImportId is null)) ||
            manifest.ImportedAtUtc == default ||
            string.IsNullOrWhiteSpace(manifest.SourceFileName) ||
            manifest.SourceFileName != Path.GetFileName(manifest.SourceFileName) ||
            !IsSha256(manifest.SourceSha256) ||
            manifest.Fragments is null || manifest.Fragments.Count == 0 ||
            manifest.ExcludedSectionIndexes is null ||
            manifest.ExcludedSectionIndexes.Any(index => index < 0) ||
            manifest.ExcludedSectionIndexes.Distinct().Count() != manifest.ExcludedSectionIndexes.Count ||
            (manifest.ApprovedPlanFingerprint is not null &&
             !IsSha256(manifest.ApprovedPlanFingerprint)))
        {
            throw new InvalidDataException("Manifeste DOCX incomplet ou incompatible.");
        }

        if (manifest.AppliedMappingProfile is not null)
        {
            DocxMappingProfileEngine.Validate(manifest.AppliedMappingProfile);
        }

        ValidateRelativePath(manifest.SourceCopyRelativePath);
        HashSet<Guid> fragmentIds = [];
        HashSet<int> sourceSections = [];
        foreach (DocxImportManifestFragment fragment in manifest.Fragments)
        {
            if (fragment.FragmentId == Guid.Empty ||
                !fragmentIds.Add(fragment.FragmentId) ||
                fragment.SourceSectionIndex < 0 ||
                !sourceSections.Add(fragment.SourceSectionIndex) ||
                manifest.ExcludedSectionIndexes.Contains(fragment.SourceSectionIndex) ||
                fragment.SelectedBlockIndexes is null ||
                fragment.SelectedBlockIndexes.Any(index => index < 0) ||
                fragment.SelectedBlockIndexes.Distinct().Count() != fragment.SelectedBlockIndexes.Count ||
                string.IsNullOrWhiteSpace(fragment.SourceHeading) ||
                string.IsNullOrWhiteSpace(fragment.TargetSection) ||
                fragment.BaseMarkdown is null ||
                (fragment.LatestWordMarkdownSnapshot is not null &&
                 fragment.ReimportResolution is not ("Word" or "Manual" or "Nodalis")) ||
                (fragment.ReimportResolution is not null &&
                 (fragment.LatestWordMarkdownSnapshot is null ||
                  fragment.ReimportResolution is not ("Word" or "Manual" or "Nodalis"))) ||
                (fragment.LatestWordSnapshotSha256 is not null &&
                 (fragment.LatestWordMarkdownSnapshot is null ||
                  !IsSha256(fragment.LatestWordSnapshotSha256) ||
                  !string.Equals(fragment.LatestWordSnapshotSha256,
                      ComputeSha256(fragment.LatestWordMarkdownSnapshot),
                      StringComparison.OrdinalIgnoreCase))) ||
                !IsSha256(fragment.FragmentSha256) ||
                !IsSha256(fragment.DestinationSha256) ||
                !string.Equals(fragment.FragmentSha256,
                    ComputeSha256(fragment.BaseMarkdown), StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("Fragment du manifeste DOCX invalide.");
            }

            ValidateRelativePath(fragment.DestinationRelativePath);
        }
    }

    /// <summary>Rejects escaping or absolute paths supplied by a manifest.</summary>
    private void ValidateRelativePath(string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath) ||
            Path.IsPathRooted(relativePath) ||
            relativePath.Contains(':'))
        {
            throw new InvalidDataException("Chemin de manifeste DOCX non relatif.");
        }

        string full = Path.GetFullPath(Path.Combine(_workspaceRoot,
            relativePath.Replace('/', Path.DirectorySeparatorChar)));
        EnsureWorkspacePath(full);
    }

    /// <summary>Resolves a local path and confines it to the current workspace.</summary>
    private string EnsureWorkspacePath(string path)
    {
        string full = Path.GetFullPath(path);
        string relative = Path.GetRelativePath(_workspaceRoot, full);
        if (Path.IsPathRooted(relative) ||
            relative == ".." ||
            relative.StartsWith(".." + Path.DirectorySeparatorChar,
                StringComparison.Ordinal))
        {
            throw new InvalidDataException("Chemin DOCX hors du workspace.");
        }

        return full;
    }

    /// <summary>Checks that a fingerprint contains exactly 64 hexadecimal characters.</summary>
    private static bool IsSha256(string? value) =>
        value is { Length: 64 } &&
        value.All(character =>
            character is >= '0' and <= '9' or >= 'a' and <= 'f' or >= 'A' and <= 'F');
}
