using Nodalis.Core.Importing;
using Nodalis.Infrastructure.Persistence;

namespace Nodalis.Infrastructure.Importing;

/// <summary>
/// Persists versioned DOCX mapping profiles in workspace/Templates as readable JSON.
/// Reading an absent store does not create any workspace files.
/// </summary>
public sealed class DocxMappingProfileStore
{
    public const string FileName = "docx-mapping-profiles.json";

    private readonly string _path;

    public DocxMappingProfileStore(string workspaceRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRoot);
        _path = Path.Combine(Path.GetFullPath(workspaceRoot),
            WorkspaceLayout.TemplatesDirectoryName, FileName);
    }

    /// <summary>
    /// Loads profiles, rejecting an unsupported schema or malformed data.
    /// </summary>
    public async Task<DocxMappingProfileCatalog> LoadAsync(
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_path))
        {
            return new DocxMappingProfileCatalog();
        }

        FileInfo info = new FileInfo(_path);
        if (info.Length > 2_000_000)
        {
            throw new InvalidDataException("Fichier de profils DOCX trop volumineux.");
        }

        DocxMappingProfileCatalog catalog =
            await AtomicJsonFile.ReadAsync<DocxMappingProfileCatalog>(_path, cancellationToken);
        ValidateCatalog(catalog);
        return catalog;
    }

    /// <summary>
    /// Saves all profiles atomically; an invalid catalog never replaces a valid one.
    /// </summary>
    public Task SaveAsync(
        DocxMappingProfileCatalog catalog,
        CancellationToken cancellationToken = default)
    {
        ValidateCatalog(catalog);
        return AtomicJsonFile.WriteAsync(_path, catalog, cancellationToken);
    }

    /// <summary>
    /// Reads a portable single-profile JSON export, without importing automatically.
    /// </summary>
    public static async Task<DocxMappingProfile> ImportProfileAsync(
        string sourcePath, CancellationToken cancellationToken = default)
    {
        FileInfo file = new FileInfo(sourcePath);
        if (!file.Exists || file.Length > 1_000_000)
        {
            throw new InvalidDataException("Fichier de profil absent ou trop volumineux.");
        }

        DocxMappingProfile profile = await AtomicJsonFile.ReadAsync<DocxMappingProfile>(
            file.FullName, cancellationToken);
        DocxMappingProfileEngine.Validate(profile);
        return profile;
    }

    /// <summary>
    /// Exports a single validated profile with atomic output and no private source text.
    /// </summary>
    public static Task ExportProfileAsync(
        string path,
        DocxMappingProfile profile,
        CancellationToken cancellationToken = default)
    {
        DocxMappingProfileEngine.Validate(profile);
        return AtomicJsonFile.WriteAsync(Path.GetFullPath(path), profile, cancellationToken);
    }

    /// <summary>
    /// Ensures profile identities and schema are unique and all limits are respected.
    /// </summary>
    public static void ValidateCatalog(DocxMappingProfileCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        if (catalog.SchemaVersion != DocxMappingProfileCatalog.CurrentSchemaVersion ||
            catalog.Profiles.Count > 50)
        {
            throw new InvalidDataException("Catalogue DOCX incompatible ou trop volumineux.");
        }

        HashSet<Guid> ids = [];
        foreach (DocxMappingProfile profile in catalog.Profiles)
        {
            DocxMappingProfileEngine.Validate(profile);
            if (!ids.Add(profile.Id))
            {
                throw new InvalidDataException("Deux profils DOCX portent le même identifiant.");
            }
        }
    }
}
