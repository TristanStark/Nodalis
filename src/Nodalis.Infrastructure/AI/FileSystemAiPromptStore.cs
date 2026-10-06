namespace Nodalis.Infrastructure.AI;

/// <summary>
/// Loads versioned, readable local AI prompts from Markdown files.
/// </summary>
public sealed class FileSystemAiPromptStore
{
    private readonly string _promptRoot;

    /// <summary>
    /// Initializes a prompt store rooted in one local directory.
    /// </summary>
    /// <param name="promptRoot">Directory containing versioned Markdown prompts.</param>
    public FileSystemAiPromptStore(
            string promptRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(
            promptRoot);

        _promptRoot =
            Path.GetFullPath(
                promptRoot);
    }

    /// <summary>
    /// Loads one prompt by its traversal-safe identifier.
    /// </summary>
    /// <param name="promptId">Prompt file identifier without extension.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The readable prompt content.</returns>
    public async Task<string> LoadAsync(
            string promptId,
            CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(
            promptId);

        if (!IsSafePromptId(
                promptId))
        {
            throw new ArgumentException(
                "L'identifiant de prompt contient des caractères non autorisés.",
                nameof(promptId));
        }

        string path =
            Path.GetFullPath(
                Path.Combine(
                    _promptRoot,
                    promptId +
                    ".md"));

        string rootPrefix =
            _promptRoot.TrimEnd(
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar) +
            Path.DirectorySeparatorChar;

        if (!path.StartsWith(
                rootPrefix,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                "Le prompt demandé se trouve hors du répertoire autorisé.");
        }

        if (!File.Exists(
                path))
        {
            throw new FileNotFoundException(
                "Le prompt IA local est introuvable.",
                path);
        }

        return await File.ReadAllTextAsync(
            path,
            cancellationToken);
    }

    /// <summary>
    /// Verifies that a prompt identifier cannot escape the prompt directory.
    /// </summary>
    /// <param name="promptId">The prompt identifier.</param>
    /// <returns>Whether the identifier is safe.</returns>
    private static bool IsSafePromptId(
            string promptId)
    {
        return promptId.All(character =>
                   char.IsLetterOrDigit(
                       character) ||
                   character is '-' or '_') &&
               !promptId.Contains(
                   "..",
                   StringComparison.Ordinal);
    }
}
