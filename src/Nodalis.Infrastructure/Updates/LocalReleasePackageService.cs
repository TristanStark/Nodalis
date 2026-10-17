using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Nodalis.Core.Updates;

namespace Nodalis.Infrastructure.Updates;

/// <summary>
/// Validates and stages Nodalis release archives without accessing the network or workspace data.
/// </summary>
public sealed class LocalReleasePackageService
{
    private const string ExpectedProduct = "Nodalis";
    private const string ExpectedTargetRid = "win-x64";
    private const string ManifestFileName = "release-manifest.json";
    private const string ApplicationExecutableName = "Nodalis.exe";
    private const string UpdaterExecutableName = "Nodalis.Updater.exe";

    private static readonly JsonSerializerOptions ManifestJsonOptions =
        new()
        {
            PropertyNameCaseInsensitive = true
        };

    /// <summary>
    /// Fully validates a local release archive and extracts the verified payload beside the application directory.
    /// </summary>
    /// <param name="archivePath">The selected local ZIP archive.</param>
    /// <param name="installationDirectory">The current Nodalis application directory.</param>
    /// <param name="currentVersion">The running application version.</param>
    /// <param name="workspaceSchemaVersion">The workspace schema supported by the running application.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A staged package that is safe to hand to the updater process.</returns>
    public async Task<StagedReleasePackage> ValidateAndStageAsync(
            string archivePath,
            string installationDirectory,
            string currentVersion,
            int workspaceSchemaVersion,
            CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(
            archivePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(
            installationDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(
            currentVersion);

        string fullArchivePath = Path.GetFullPath(
            archivePath);
        string fullInstallationDirectory = Path.GetFullPath(
            installationDirectory);

        if (!File.Exists(
                fullArchivePath))
        {
            throw new FileNotFoundException(
                "L'archive de mise à jour sélectionnée est introuvable.",
                fullArchivePath);
        }

        if (!string.Equals(
                Path.GetExtension(
                    fullArchivePath),
                ".zip",
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                "La mise à jour doit être fournie sous forme d'archive ZIP.");
        }

        string? installationParent =
            Directory.GetParent(
                    fullInstallationDirectory.TrimEnd(
                        Path.DirectorySeparatorChar,
                        Path.AltDirectorySeparatorChar))
                ?.FullName;

        if (string.IsNullOrWhiteSpace(
                installationParent))
        {
            throw new InvalidOperationException(
                "Le dossier parent de l'installation Nodalis est introuvable.");
        }

        string stagingDirectory = Path.Combine(
            installationParent,
            $".nodalis-update-{Guid.NewGuid():N}");

        try
        {
            Directory.CreateDirectory(
                stagingDirectory);

            using ZipArchive archive = ZipFile.OpenRead(
                fullArchivePath);

            ZipArchiveEntry manifestEntry = FindManifestEntry(
                archive);

            ReleaseManifest manifest = await ReadManifestAsync(
                manifestEntry,
                cancellationToken);

            ValidateManifest(
                manifest,
                currentVersion,
                workspaceSchemaVersion);

            string payloadPrefix =
                NormalizePayloadDirectory(
                    manifest.PayloadDirectory) +
                "/";

            HashSet<string> seenPaths =
                new HashSet<string>(
                    StringComparer.OrdinalIgnoreCase);

            foreach (ReleaseFileEntry file in manifest.Files)
            {
                cancellationToken.ThrowIfCancellationRequested();

                string relativePath =
                    NormalizeRelativeFilePath(
                        file.Path);

                if (!seenPaths.Add(
                        relativePath))
                {
                    throw new InvalidDataException(
                        $"Le manifeste contient plusieurs fois le fichier « {relativePath} ».");
                }

                string archiveEntryName =
                    payloadPrefix +
                    relativePath;

                ZipArchiveEntry? sourceEntry = archive.Entries
                    .FirstOrDefault(entry =>
                        string.Equals(
                            entry.FullName,
                            archiveEntryName,
                            StringComparison.OrdinalIgnoreCase));

                if (sourceEntry is null ||
                    string.IsNullOrEmpty(
                        sourceEntry.Name))
                {
                    throw new InvalidDataException(
                        $"Le fichier « {relativePath} » déclaré dans le manifeste est absent de l'archive.");
                }

                if (file.Length < 0 ||
                    sourceEntry.Length !=
                    file.Length)
                {
                    throw new InvalidDataException(
                        $"La taille du fichier « {relativePath} » ne correspond pas au manifeste.");
                }

                string targetPath = GetSafeStagingPath(
                    stagingDirectory,
                    relativePath);

                string? targetDirectory =
                    Path.GetDirectoryName(
                        targetPath);

                if (!string.IsNullOrWhiteSpace(
                        targetDirectory))
                {
                    Directory.CreateDirectory(
                        targetDirectory);
                }

                string actualSha256 =
                    await ExtractAndHashAsync(
                        sourceEntry,
                        targetPath,
                        cancellationToken);

                string expectedSha256 =
                    NormalizeSha256(
                        file.Sha256,
                        relativePath);

                if (!string.Equals(
                        actualSha256,
                        expectedSha256,
                        StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException(
                        $"Le SHA-256 du fichier « {relativePath} » ne correspond pas au manifeste.");
                }
            }

            EnsureRequiredPayloadFile(
                stagingDirectory,
                ApplicationExecutableName);
            EnsureRequiredPayloadFile(
                stagingDirectory,
                UpdaterExecutableName);

            await WriteStagedManifestAsync(
                stagingDirectory,
                manifest,
                cancellationToken);

            return new StagedReleasePackage
            {
                ArchivePath =
                    fullArchivePath,
                StagedApplicationDirectory =
                    stagingDirectory,
                Manifest =
                    manifest
            };
        }
        catch
        {
            TryDeleteDirectory(
                stagingDirectory);
            throw;
        }
    }

    /// <summary>
    /// Persists the validated release manifest inside the staging directory for updater-side revalidation.
    /// </summary>
    /// <param name="stagingDirectory">The verified staging directory.</param>
    /// <param name="manifest">The validated release manifest.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    private static async Task WriteStagedManifestAsync(
            string stagingDirectory,
            ReleaseManifest manifest,
            CancellationToken cancellationToken)
    {
        string manifestPath = Path.Combine(
            stagingDirectory,
            ManifestFileName);

        if (File.Exists(
                manifestPath))
        {
            throw new InvalidDataException(
                $"{ManifestFileName} est un nom réservé dans le payload applicatif.");
        }

        await using global::System.IO.FileStream stream = new FileStream(
            manifestPath,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 4096,
            useAsync: true);

        await JsonSerializer.SerializeAsync(
            stream,
            manifest,
            ManifestJsonOptions,
            cancellationToken);
    }

    /// <summary>
    /// Gets the directory used to preserve the application version immediately preceding an update.
    /// </summary>
    /// <param name="installationDirectory">The current application directory.</param>
    /// <returns>The sibling rollback directory.</returns>
    public static string GetPreviousVersionDirectory(
            string installationDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(
            installationDirectory);

        string fullInstallationDirectory = Path.GetFullPath(
                installationDirectory)
            .TrimEnd(
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar);

        return fullInstallationDirectory +
            ".previous";
    }

    /// <summary>
    /// Determines whether a usable previous version is available for rollback.
    /// </summary>
    /// <param name="installationDirectory">The current application directory.</param>
    /// <returns><see langword="true"/> when a previous Nodalis executable is available.</returns>
    public static bool CanRollback(
            string installationDirectory)
    {
        string previousDirectory =
            GetPreviousVersionDirectory(
                installationDirectory);

        return File.Exists(
            Path.Combine(
                previousDirectory,
                ApplicationExecutableName));
    }

    /// <summary>
    /// Determines whether two directories overlap by equality or containment.
    /// </summary>
    /// <param name="firstDirectory">The first directory.</param>
    /// <param name="secondDirectory">The second directory.</param>
    /// <returns><see langword="true"/> when either directory contains the other.</returns>
    public static bool PathsOverlap(
            string firstDirectory,
            string secondDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(
            firstDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(
            secondDirectory);

        string first = EnsureTrailingSeparator(
            Path.GetFullPath(
                firstDirectory));
        string second = EnsureTrailingSeparator(
            Path.GetFullPath(
                secondDirectory));

        return first.StartsWith(
                   second,
                   StringComparison.OrdinalIgnoreCase) ||
               second.StartsWith(
                   first,
                   StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Locates the single release manifest within an archive.
    /// </summary>
    /// <param name="archive">The opened release archive.</param>
    /// <returns>The manifest ZIP entry.</returns>
    private static ZipArchiveEntry FindManifestEntry(
            ZipArchive archive)
    {
        ZipArchiveEntry[] candidates = archive.Entries
            .Where(entry =>
                string.Equals(
                    entry.Name,
                    ManifestFileName,
                    StringComparison.OrdinalIgnoreCase))
            .ToArray();

        if (candidates.Length != 1)
        {
            throw new InvalidDataException(
                "L'archive doit contenir exactement un release-manifest.json.");
        }

        return candidates[0];
    }

    /// <summary>
    /// Deserializes a release manifest.
    /// </summary>
    /// <param name="entry">The manifest ZIP entry.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The parsed release manifest.</returns>
    private static async Task<ReleaseManifest> ReadManifestAsync(
            ZipArchiveEntry entry,
            CancellationToken cancellationToken)
    {
        await using Stream stream =
            entry.Open();

        using StreamReader reader = new StreamReader(
            stream,
            Encoding.UTF8,
            detectEncodingFromByteOrderMarks: true,
            bufferSize: 4096,
            leaveOpen: false);

        string json = await reader.ReadToEndAsync(
            cancellationToken);

        ReleaseManifest? manifest =
            JsonSerializer.Deserialize<ReleaseManifest>(
                json,
                ManifestJsonOptions);

        if (manifest is null)
        {
            throw new InvalidDataException(
                "Le manifeste de release est vide ou illisible.");
        }

        return manifest;
    }

    /// <summary>
    /// Validates release identity, target, version and workspace compatibility before any application shutdown.
    /// </summary>
    /// <param name="manifest">The release manifest.</param>
    /// <param name="currentVersion">The running application version.</param>
    /// <param name="workspaceSchemaVersion">The supported workspace schema.</param>
    private static void ValidateManifest(
            ReleaseManifest manifest,
            string currentVersion,
            int workspaceSchemaVersion)
    {
        if (manifest.ManifestSchemaVersion !=
            ReleaseManifest.CurrentManifestSchemaVersion)
        {
            throw new InvalidDataException(
                $"Version de manifeste non supportée : {manifest.ManifestSchemaVersion}.");
        }

        if (!string.Equals(
                manifest.Product,
                ExpectedProduct,
                StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                $"Produit de release invalide : « {manifest.Product} ».");
        }

        if (!string.Equals(
                manifest.TargetRid,
                ExpectedTargetRid,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                $"Cette archive cible « {manifest.TargetRid} » au lieu de « {ExpectedTargetRid} ».");
        }

        if (string.IsNullOrWhiteSpace(
                manifest.Version))
        {
            throw new InvalidDataException(
                "Le manifeste ne contient pas de version.");
        }

        Version? candidateVersion =
            ParseLooseVersion(
                manifest.Version);
        Version? runningVersion =
            ParseLooseVersion(
                currentVersion);

        if (candidateVersion is null)
        {
            throw new InvalidDataException(
                $"Version de release invalide : « {manifest.Version} ».");
        }

        if (runningVersion is not null &&
            candidateVersion <=
            runningVersion)
        {
            throw new InvalidDataException(
                $"La release {manifest.Version} n'est pas plus récente que la version installée {currentVersion}. Utilisez le rollback pour revenir à une version précédente.");
        }

        if (manifest.MinimumWorkspaceSchemaVersion >
            manifest.MaximumWorkspaceSchemaVersion)
        {
            throw new InvalidDataException(
                "La plage de schémas de workspace du manifeste est invalide.");
        }

        bool directlyCompatible =
            workspaceSchemaVersion >=
                manifest.MinimumWorkspaceSchemaVersion &&
            workspaceSchemaVersion <=
                manifest.MaximumWorkspaceSchemaVersion;
        bool migratable =
            manifest.MigratableWorkspaceSchemaVersions.Contains(
                workspaceSchemaVersion);

        if (!directlyCompatible &&
            !migratable)
        {
            throw new InvalidDataException(
                $"La release {manifest.Version} ne sait ni ouvrir directement ni migrer le schéma de workspace {workspaceSchemaVersion}.");
        }

        if (manifest.MigratableWorkspaceSchemaVersions.Any(
                version => version < 0) ||
            manifest.MigratableWorkspaceSchemaVersions.Count !=
                manifest.MigratableWorkspaceSchemaVersions.Distinct().Count())
        {
            throw new InvalidDataException(
                "La liste des schémas migrables du manifeste est invalide.");
        }

        if (manifest.Files.Count == 0)
        {
            throw new InvalidDataException(
                "Le manifeste ne déclare aucun fichier applicatif.");
        }

        NormalizePayloadDirectory(
            manifest.PayloadDirectory);
    }

    /// <summary>
    /// Extracts one ZIP entry while computing its SHA-256.
    /// </summary>
    /// <param name="entry">The source ZIP entry.</param>
    /// <param name="targetPath">The destination path.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The lowercase hexadecimal SHA-256.</returns>
    private static async Task<string> ExtractAndHashAsync(
            ZipArchiveEntry entry,
            string targetPath,
            CancellationToken cancellationToken)
    {
        await using Stream source =
            entry.Open();
        await using FileStream target = new FileStream(
            targetPath,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 81920,
            useAsync: true);
        using IncrementalHash hash =
            IncrementalHash.CreateHash(
                HashAlgorithmName.SHA256);

        byte[] buffer = new byte[81920];

        while (true)
        {
            int read = await source.ReadAsync(
                buffer,
                cancellationToken);

            if (read == 0)
            {
                break;
            }

            hash.AppendData(
                buffer,
                0,
                read);

            await target.WriteAsync(
                buffer.AsMemory(
                    0,
                    read),
                cancellationToken);
        }

        byte[] digest =
            hash.GetHashAndReset();

        return Convert.ToHexString(
                digest)
            .ToLowerInvariant();
    }

    /// <summary>
    /// Normalizes and validates the payload directory name.
    /// </summary>
    /// <param name="payloadDirectory">The manifest payload directory.</param>
    /// <returns>The normalized ZIP path.</returns>
    private static string NormalizePayloadDirectory(
            string payloadDirectory)
    {
        if (string.IsNullOrWhiteSpace(
                payloadDirectory))
        {
            throw new InvalidDataException(
                "Le manifeste ne définit pas le dossier de payload.");
        }

        string normalized =
            payloadDirectory
                .Replace(
                    '\\',
                    '/')
                .Trim('/');

        if (normalized.Length == 0 ||
            normalized == "." ||
            normalized == ".." ||
            normalized.Contains(
                "../",
                StringComparison.Ordinal) ||
            normalized.Contains(
                "/..",
                StringComparison.Ordinal) ||
            Path.IsPathRooted(
                normalized))
        {
            throw new InvalidDataException(
                "Le dossier de payload du manifeste est invalide.");
        }

        return normalized;
    }

    /// <summary>
    /// Normalizes and validates a file path declared by the release manifest.
    /// </summary>
    /// <param name="relativePath">The manifest path.</param>
    /// <returns>The normalized ZIP-relative path.</returns>
    private static string NormalizeRelativeFilePath(
            string relativePath)
    {
        if (string.IsNullOrWhiteSpace(
                relativePath))
        {
            throw new InvalidDataException(
                "Le manifeste contient un chemin de fichier vide.");
        }

        string normalized =
            relativePath
                .Replace(
                    '\\',
                    '/')
                .TrimStart('/');

        string[] segments =
            normalized.Split(
                '/',
                StringSplitOptions.RemoveEmptyEntries);

        if (segments.Length == 0 ||
            segments.Any(segment =>
                segment == "." ||
                segment == "..") ||
            Path.IsPathRooted(
                relativePath))
        {
            throw new InvalidDataException(
                $"Chemin de release invalide : « {relativePath} ».");
        }

        return string.Join(
            '/',
            segments);
    }

    /// <summary>
    /// Builds a staging path and guarantees it remains inside the staging directory.
    /// </summary>
    /// <param name="stagingDirectory">The staging root.</param>
    /// <param name="relativePath">The normalized relative path.</param>
    /// <returns>The safe destination path.</returns>
    private static string GetSafeStagingPath(
            string stagingDirectory,
            string relativePath)
    {
        string root =
            EnsureTrailingSeparator(
                Path.GetFullPath(
                    stagingDirectory));

        string candidate =
            Path.GetFullPath(
                Path.Combine(
                    root,
                    relativePath.Replace(
                        '/',
                        Path.DirectorySeparatorChar)));

        if (!candidate.StartsWith(
                root,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                $"Le chemin « {relativePath} » sort du dossier de staging.");
        }

        return candidate;
    }

    /// <summary>
    /// Normalizes and validates a SHA-256 value.
    /// </summary>
    /// <param name="sha256">The expected hash.</param>
    /// <param name="relativePath">The associated file path for diagnostics.</param>
    /// <returns>The lowercase hexadecimal hash.</returns>
    private static string NormalizeSha256(
            string sha256,
            string relativePath)
    {
        string normalized =
            sha256.Trim()
                .ToLowerInvariant();

        if (normalized.Length != 64 ||
            normalized.Any(character =>
                !Uri.IsHexDigit(
                    character)))
        {
            throw new InvalidDataException(
                $"SHA-256 invalide pour « {relativePath} ».");
        }

        return normalized;
    }

    /// <summary>
    /// Ensures a mandatory executable exists in the staged payload.
    /// </summary>
    /// <param name="stagingDirectory">The staging root.</param>
    /// <param name="fileName">The required file.</param>
    private static void EnsureRequiredPayloadFile(
            string stagingDirectory,
            string fileName)
    {
        if (!File.Exists(
                Path.Combine(
                    stagingDirectory,
                    fileName)))
        {
            throw new InvalidDataException(
                $"La release ne contient pas « {fileName} ».");
        }
    }

    /// <summary>
    /// Parses the numeric core of a semantic-style version.
    /// </summary>
    /// <param name="value">The version text.</param>
    /// <returns>The parsed version, or <see langword="null"/> when invalid.</returns>
    private static Version? ParseLooseVersion(
            string value)
    {
        string core = value
            .Split(
                ['-', '+'],
                2,
                StringSplitOptions.TrimEntries)[0];

        return Version.TryParse(
            core,
            out Version? version)
                ? version
                : null;
    }

    /// <summary>
    /// Adds a directory separator to a normalized directory path.
    /// </summary>
    /// <param name="path">The source path.</param>
    /// <returns>The normalized path with one trailing separator.</returns>
    private static string EnsureTrailingSeparator(
            string path) =>
        path.TrimEnd(
            Path.DirectorySeparatorChar,
            Path.AltDirectorySeparatorChar) +
        Path.DirectorySeparatorChar;

    /// <summary>
    /// Best-effort removal for a failed staging directory.
    /// </summary>
    /// <param name="directory">The directory to remove.</param>
    private static void TryDeleteDirectory(
            string directory)
    {
        try
        {
            if (Directory.Exists(
                    directory))
            {
                Directory.Delete(
                    directory,
                    recursive: true);
            }
        }
        catch
        {
        }
    }
}
