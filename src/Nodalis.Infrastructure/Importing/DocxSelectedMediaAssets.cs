using System.IO.Compression;
using System.Security.Cryptography;
using Nodalis.Core.Importing;

namespace Nodalis.Infrastructure.Importing;

/// <summary>
/// Plans and copies selected embedded Word images into a project's shared local media folder.
/// </summary>
public static class DocxSelectedMediaAssets
{
    public const string MediaFolder = "Media-Docx";

    /// <summary>Selects unique supported resources from retained Word paragraph blocks only.</summary>
    public static IReadOnlyList<DocxInlineMedia> Select(
        IEnumerable<DocxBlock> selectedBlocks)
    {
        ArgumentNullException.ThrowIfNull(selectedBlocks);
        return selectedBlocks
            .Where(block => block.Paragraph is not null)
            .SelectMany(block => block.Paragraph!.Media)
            .Where(media => media.IsSupported)
            .DistinctBy(media => media.MarkdownRelativePath, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    /// <summary>Returns every explicit fidelity warning attached to selected Word media.</summary>
    public static IReadOnlyList<string> Warnings(IEnumerable<DocxBlock> selectedBlocks) =>
        selectedBlocks.Where(block => block.Paragraph is not null)
            .SelectMany(block => block.Paragraph!.Media)
            .Where(media => !string.IsNullOrWhiteSpace(media.Warning))
            .Select(media => media.Warning!)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

    /// <summary>
    /// Copies approved selected images, verifies source SHA and reuses identical existing files.
    /// Registers every new file immediately for caller rollback after any exception.
    /// </summary>
    public static async Task CopyAsync(
        string stagedDocx,
        string projectDirectory,
        IReadOnlyList<DocxInlineMedia> media,
        ICollection<string> createdFiles,
        ICollection<string> createdDirectories,
        CancellationToken cancellationToken = default)
    {
        if (media.Count == 0)
        {
            return;
        }

        // Validate the ZIP before creating any workspace directories or copying media.
        using ZipArchive archive = ZipFile.OpenRead(stagedDocx);
        DocxPackageGuard.Validate(archive);

        string mediaDirectory = Path.Combine(projectDirectory, MediaFolder);
        if (Directory.Exists(mediaDirectory))
        {
            if ((File.GetAttributes(mediaDirectory) & FileAttributes.ReparsePoint) != 0)
            {
                throw new InvalidDataException("Dossier des médias Word redirigé : copie refusée.");
            }
        }
        else
        {
            Directory.CreateDirectory(mediaDirectory);
            createdDirectories.Add(mediaDirectory);
        }
        foreach (DocxInlineMedia item in media)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!item.IsSupported ||
                item.Sha256 is null || item.Sha256.Length != 64 ||
                !item.Sha256.All(Uri.IsHexDigit) ||
                item.ArchiveEntryPath is null ||
                !item.ArchiveEntryPath.StartsWith("word/media/", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("Descripteur d'image Word invalide.");
            }

            string ext = Path.GetExtension(item.ArchiveEntryPath).ToLowerInvariant();
            if (ext is not (".png" or ".jpeg" or ".jpg" or ".gif" or ".bmp"))
            {
                throw new InvalidDataException("Extension d'image Word non autorisée.");
            }

            string fileName = item.Sha256 + ext;
            string destination = Path.Combine(mediaDirectory, fileName);
            if (!string.Equals(item.MarkdownRelativePath, "../" + MediaFolder + "/" + fileName,
                    StringComparison.Ordinal))
            {
                throw new InvalidDataException("Référence Markdown d'image Word incohérente.");
            }

            if (File.Exists(destination))
            {
                if ((File.GetAttributes(destination) & FileAttributes.ReparsePoint) != 0)
                {
                    throw new InvalidDataException("Image Word existante redirigée : copie refusée.");
                }

                await using FileStream previous = new FileStream(destination, FileMode.Open, FileAccess.Read);
                if (previous.Length > 16L * 1024 * 1024 ||
                    !string.Equals(Convert.ToHexString(
                        await SHA256.HashDataAsync(previous, cancellationToken)),
                        item.Sha256, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException("Collision ou altération d'une image déjà importée.");
                }

                continue;
            }

            ZipArchiveEntry entry = archive.GetEntry(item.ArchiveEntryPath) ??
                throw new InvalidDataException("Image DOCX absente lors de l'import.");
            if (entry.Length > 16L * 1024 * 1024)
            {
                throw new InvalidDataException("Image DOCX trop volumineuse lors de la copie.");
            }

            // A crash must not leave a half-written file under its final
            // SHA-based name: stream to a temporary sibling, then move once
            // its bytes have been checked and synchronized.
            string temporary = Path.Combine(mediaDirectory,
                "." + fileName + "." + Guid.NewGuid().ToString("N") + ".tmp");
            try
            {
                using IncrementalHash hash =
                    IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                using Stream source = entry.Open();
                await using (FileStream output = new FileStream(temporary,
                    FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920,
                    FileOptions.Asynchronous | FileOptions.WriteThrough))
                {
                    byte[] buffer = new byte[81920];
                    long copied = 0;
                    int count;
                    while ((count = await source.ReadAsync(buffer, cancellationToken)) > 0)
                    {
                        copied += count;
                        if (copied > 16L * 1024 * 1024)
                        {
                            throw new InvalidDataException(
                                "ZIP Word dépassant la limite d'image pendant copie.");
                        }

                        await output.WriteAsync(buffer.AsMemory(0, count), cancellationToken);
                        hash.AppendData(buffer, 0, count);
                    }

                    await output.FlushAsync(cancellationToken);
                    output.Flush(flushToDisk: true);
                }

                if (!string.Equals(Convert.ToHexString(hash.GetHashAndReset()),
                        item.Sha256, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException(
                        "Empreinte de média DOCX modifiée depuis le plan.");
                }

                File.Move(temporary, destination);
                createdFiles.Add(destination);
            }
            finally
            {
                if (File.Exists(temporary))
                {
                    File.Delete(temporary);
                }
            }
        }
    }
}
