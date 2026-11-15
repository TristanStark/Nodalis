using System.Security.Cryptography;
using System.Text;
using Nodalis.Infrastructure.Reliability;

namespace Nodalis.Infrastructure.Projects;

/// <summary>
/// Immutable byte-revision guard for updates to SFD Markdown documents.
/// Never overwrites manual edits made after the user started an operation.
/// </summary>
internal static class SfdMarkdownRevisionWriter
{
    /// <summary>Reads a capped, valid UTF-8 source and its exact byte hash.</summary>
    /// <param name="path">Already validated canonical source path.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    /// <returns>Source text with exact byte revision.</returns>
    public static async Task<SfdTextRevision> ReadAsync(string path,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(path) || new FileInfo(path).Length > 1024 * 1024)
        {
            throw new InvalidDataException("Document SFD introuvable ou trop volumineux.");
        }

        byte[] data = await File.ReadAllBytesAsync(path, cancellationToken);
        if (data.Length > 1024 * 1024)
        {
            throw new InvalidDataException("Document SFD trop volumineux.");
        }

        string source;
        try
        {
            source = new UTF8Encoding(false, true).GetString(data);
        }
        catch (DecoderFallbackException exception)
        {
            throw new InvalidDataException("Le document SFD n'est pas un UTF-8 valide.",
                exception);
        }

        return new SfdTextRevision(source,
            Convert.ToHexString(SHA256.HashData(data)));
    }

    /// <summary>
    /// Replaces an existing source only when its original bytes still match
    /// the revision approved by the caller.
    /// </summary>
    /// <param name="path">Canonical source path.</param>
    /// <param name="newContent">Reviewed changed Markdown.</param>
    /// <param name="expectedHash">Original SHA-256.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    /// <returns>Exact SHA-256 of the published UTF-8 text.</returns>
    public static async Task<string> ReplaceAsync(string path,
        string newContent, string expectedHash, CancellationToken cancellationToken)
    {
        byte[] replacement = new UTF8Encoding(false).GetBytes(newContent);
        if (replacement.Length > 1024 * 1024)
        {
            throw new InvalidDataException("Le document SFD dépasse 1 Mo.");
        }

        string newHash = Convert.ToHexString(SHA256.HashData(replacement));
        string stage = Path.Combine(Path.GetDirectoryName(path)!,
            "." + Path.GetFileName(path) + "." + Guid.NewGuid().ToString("N") + ".stage");
        try
        {
            await AtomicFileWriter.WriteNewAllTextAsync(stage, newContent, cancellationToken);
            await AtomicFileWriter.ReplaceSnapshotIfUnchangedAsync(
                path, expectedHash, stage, newHash, cancellationToken);
            return newHash;
        }
        finally
        {
            if (File.Exists(stage))
            {
                File.Delete(stage);
            }
        }
    }
}

/// <summary>Bounded source body with original SHA-256 revision.</summary>
/// <param name="Content">UTF-8 source.</param>
/// <param name="Sha256">Hash of exact original file bytes.</param>
internal sealed record SfdTextRevision(string Content, string Sha256);
