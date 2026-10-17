namespace Nodalis.Core.Inbox;

/// <summary>Determines safe capture intent from explicit text or file selections.</summary>
public static class InboxCaptureClassifier
{
    /// <summary>
    /// Treats a single absolute HTTP(S) URI as a link; all other text remains
    /// Markdown without interpreting file paths or executable instructions.
    /// </summary>
    /// <param name="text">Explicitly supplied text content.</param>
    /// <returns>The inferred capture kind.</returns>
    public static InboxCaptureKind ClassifyText(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        string candidate = text.Trim();
        if (Uri.TryCreate(candidate, UriKind.Absolute, out Uri? uri) &&
            (uri.Scheme == Uri.UriSchemeHttp ||
             uri.Scheme == Uri.UriSchemeHttps) &&
            candidate.IndexOfAny(['\r', '\n', '\t']) == -1)
        {
            return InboxCaptureKind.Link;
        }

        return InboxCaptureKind.Text;
    }

    /// <summary>
    /// Validates explicitly chosen regular files before displaying a summary.
    /// The persistence service repeats validation before copying each file.
    /// </summary>
    /// <param name="paths">Selected file paths.</param>
    /// <returns>Validated paths, at most ten and in original selection order.</returns>
    public static IReadOnlyList<string> ValidateFiles(
            IEnumerable<string> paths)
    {
        ArgumentNullException.ThrowIfNull(paths);

        string[] selected = paths
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (selected.Length is < 1 or > 10)
        {
            throw new InvalidDataException(
                "Sélectionnez entre un et dix fichiers.");
        }

        foreach (string path in selected)
        {
            FileInfo file = new FileInfo(Path.GetFullPath(path));
            if (!file.Exists ||
                (file.Attributes & FileAttributes.ReparsePoint) != 0)
            {
                throw new IOException(
                    "Fichier absent ou lien symbolique non autorisé : " +
                    Path.GetFileName(path));
            }

            if (file.Length > 25L * 1024 * 1024)
            {
                throw new IOException(
                    "Le fichier dépasse 25 Mio : " + file.Name);
            }
        }

        return selected;
    }

    /// <summary>
    /// Renders an ordinary Markdown summary for filenames without allowing
    /// filenames themselves to be treated as Markdown link targets.
    /// </summary>
    /// <param name="files">Validated source paths.</param>
    /// <returns>A readable summary of the selected files.</returns>
    public static string FormatFileSummary(
            IReadOnlyList<string> files)
    {
        ArgumentNullException.ThrowIfNull(files);

        return "Fichiers sélectionnés :\n\n" +
            string.Join("\n", files.Select(path =>
                "- " + Path.GetFileName(path)
                    .Replace('\\', '_')
                    .Replace('[', '(')
                    .Replace(']', ')')
                    .Replace('\r', ' ')
                    .Replace('\n', ' ')));
    }
}
