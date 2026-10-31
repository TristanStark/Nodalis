using System.Security.Cryptography;
using System.Text;

namespace Nodalis.Infrastructure.Projects;

/// <summary>
/// Applies only pre-approved literal link changes to an isolated staging copy.
/// All source bytes are hashed before edits, so stale offsets fail closed.
/// </summary>
internal static class ComplexProjectMigrationLinkRewriteApplier
{
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    /// <summary>Transforms already-copied project documents, never live files.</summary>
    /// <param name="stageRoot">Transaction-private staging copy.</param>
    /// <param name="rewrites">Exact, fingerprinted source spans.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public static async Task ApplyAsync(
        string stageRoot,
        IReadOnlyList<ComplexMigrationLinkRewrite> rewrites,
        CancellationToken cancellationToken)
    {
        string root = Path.TrimEndingDirectorySeparator(
            Path.GetFullPath(stageRoot));
        foreach (IGrouping<string, ComplexMigrationLinkRewrite> group in
                 rewrites.GroupBy(item => item.ProjectRelativePath,
                     StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();
            string relative = group.Key.Replace('/', Path.DirectorySeparatorChar);
            string path = Path.GetFullPath(Path.Combine(root, relative));
            if (!path.StartsWith(root + Path.DirectorySeparatorChar,
                    StringComparison.OrdinalIgnoreCase) ||
                !File.Exists(path) ||
                (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            {
                throw new InvalidDataException("Fichier de staging hors périmètre.");
            }

            byte[] bytes = await File.ReadAllBytesAsync(path, cancellationToken);
            string hash = Convert.ToHexString(SHA256.HashData(bytes));
            string text = StrictUtf8.GetString(bytes);
            ComplexMigrationLinkRewrite[] edits = group.OrderByDescending(
                item => item.Offset).ToArray();
            int next = text.Length;
            foreach (ComplexMigrationLinkRewrite edit in edits)
            {
                if (!string.Equals(hash, edit.SourceHash,
                        StringComparison.OrdinalIgnoreCase) ||
                    edit.Offset < 0 ||
                    edit.Offset + edit.OriginalTarget.Length > next ||
                    !string.Equals(text.Substring(edit.Offset,
                        edit.OriginalTarget.Length),
                        edit.OriginalTarget, StringComparison.Ordinal))
                {
                    throw new InvalidDataException(
                        "Réécriture refusée : source ou positions modifiées : " +
                        edit.WorkspaceRelativePath);
                }

                next = edit.Offset;
                text = text[..edit.Offset] + edit.ReplacementTarget +
                    text[(edit.Offset + edit.OriginalTarget.Length)..];
            }

            await File.WriteAllBytesAsync(path,
                StrictUtf8.GetBytes(text), cancellationToken);
        }
    }
}
