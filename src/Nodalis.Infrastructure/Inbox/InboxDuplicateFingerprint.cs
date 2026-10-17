using System.Security.Cryptography;
using System.Text;
using Nodalis.Core.Inbox;

namespace Nodalis.Infrastructure.Inbox;

/// <summary>
/// Stable SHA-256 fingerprint of intentionally captured content and file
/// bytes, independent of random Inbox attachment storage names and origin.
/// </summary>
internal static class InboxDuplicateFingerprint
{
    /// <summary>Hashes selected sources or stored attachments in canonical order.</summary>
    /// <param name="kind">Capture kind.</param>
    /// <param name="markdown">Captured text.</param>
    /// <param name="files">Original names and real file paths.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    /// <returns>Uppercase SHA-256 digest.</returns>
    public static async Task<string> ComputeAsync(
        InboxCaptureKind kind,
        string markdown,
        IReadOnlyList<(string Name, string Path)> files,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(markdown);
        ArgumentNullException.ThrowIfNull(files);
        if (files.Count > InboxAttachmentRules.MaximumFileCount)
        {
            throw new InvalidDataException("Trop de pièces jointes Inbox.");
        }

        // Cross-platform canonicalization ignores Windows vs Unix line endings,
        // but never strips meaningful internal whitespace from captured content.
        string normalized = markdown.Trim().Replace("\r\n", "\n")
            .Replace('\r', '\n');
        if (Encoding.UTF8.GetByteCount(normalized) > 2 * 1024 * 1024)
        {
            throw new InvalidDataException("La capture texte dépasse 2 Mio.");
        }

        using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        AddString(hash, "nodalis-inbox-sha256-v1");
        AddString(hash, kind.ToString());
        AddString(hash, normalized);
        AddString(hash, files.Count.ToString(System.Globalization.CultureInfo.InvariantCulture));

        List<(string Name, string Hash)> digests =
            new List<(string Name, string Hash)>(files.Count);
        foreach ((string fileName, string path) in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            InboxAttachmentRules.ValidateFileName(fileName);
            FileInfo file = new FileInfo(Path.GetFullPath(path));
            if (!file.Exists ||
                (file.Attributes & FileAttributes.ReparsePoint) != 0 ||
                file.Length > InboxAttachmentRules.MaximumFileBytes)
            {
                throw new IOException("Pièce jointe manquante, redirigée ou trop grande : " +
                    fileName);
            }

            await using FileStream stream = new FileStream(file.FullName,
                FileMode.Open, FileAccess.Read, FileShare.Read,
                81920, FileOptions.Asynchronous);
            byte[] fileDigest = await SHA256.HashDataAsync(stream, cancellationToken);
            if (stream.Length > InboxAttachmentRules.MaximumFileBytes)
            {
                throw new IOException("Pièce jointe agrandie pendant la lecture.");
            }

            digests.Add((fileName.ToUpperInvariant(), Convert.ToHexString(fileDigest)));
        }

        foreach ((string name, string digest) in digests
            .OrderBy(file => file.Name, StringComparer.Ordinal)
            .ThenBy(file => file.Hash, StringComparer.Ordinal))
        {
            AddString(hash, name);
            AddString(hash, digest);
        }

        return Convert.ToHexString(hash.GetHashAndReset());
    }

    /// <summary>Appends one length-prefixed UTF-8 value, preventing ambiguity.</summary>
    /// <param name="hash">Running digest.</param>
    /// <param name="value">Value to hash.</param>
    private static void AddString(IncrementalHash hash, string value)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(value);
        hash.AppendData(BitConverter.GetBytes(bytes.Length));
        hash.AppendData(bytes);
    }
}
