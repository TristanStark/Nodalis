namespace Nodalis.Core.Inbox;

/// <summary>
/// Shared safety limits for manually selected Inbox attachments. Attachments
/// are inert copied files and must not become executable shortcuts.
/// </summary>
public static class InboxAttachmentRules
{
    public const long MaximumFileBytes = 25L * 1024 * 1024;
    public const int MaximumFileCount = 10;
    private static readonly HashSet<string> BlockedExtensions =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".exe", ".com", ".scr", ".pif", ".msi", ".msp",
            ".bat", ".cmd", ".ps1", ".psm1", ".vbs", ".vbe",
            ".js", ".jse", ".wsf", ".wsh", ".hta", ".lnk",
            ".url", ".scf", ".reg", ".dll", ".cpl"
        };

    /// <summary>Rejects executable/shortcut extensions and malformed suffixes.</summary>
    /// <param name="fileName">Only the base filename, not an attachment path.</param>
    public static void ValidateFileName(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName) ||
            fileName != Path.GetFileName(fileName) ||
            fileName.EndsWith(' ') || fileName.EndsWith('.') ||
            fileName.IndexOfAny(['\r', '\n', ':']) >= 0)
        {
            throw new InvalidDataException("Nom de pièce jointe invalide.");
        }

        string extension = Path.GetExtension(fileName);
        if (extension.Length > 16 ||
            (extension.Length > 0 && !extension[1..].All(char.IsLetterOrDigit)) ||
            BlockedExtensions.Contains(extension))
        {
            throw new InvalidDataException(
                "Extension de pièce jointe dangereuse ou non prise en charge : " +
                extension);
        }
    }
}
