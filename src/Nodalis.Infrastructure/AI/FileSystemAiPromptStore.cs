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
    /// Lists eligible local USER prompts without loading all their bodies.
    /// The FRT-100 system rules are never offered as a user prompt.
    /// </summary>
    /// <param name="cancellationToken">Cooperative cancellation.</param>
    /// <returns>Ordered local prompt choices, without workspace changes.</returns>
    public async Task<IReadOnlyList<LocalAiPromptDescriptor>> ListAsync(
        CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(_promptRoot))
        {
            return [];
        }

        if ((File.GetAttributes(_promptRoot) & FileAttributes.ReparsePoint) != 0)
        {
            throw new InvalidDataException(
                "Répertoire de prompts sous lien symbolique refusé.");
        }

        List<LocalAiPromptDescriptor> prompts = [];
        foreach (string path in Directory.EnumerateFiles(
            _promptRoot, "*.md", SearchOption.TopDirectoryOnly))
        {
            cancellationToken.ThrowIfCancellationRequested();
            FileInfo file = new(path);
            string id = Path.GetFileNameWithoutExtension(path);
            if (id == "frt100-system-v1" || !IsSafePromptId(id) ||
                (file.Attributes & FileAttributes.ReparsePoint) != 0 ||
                file.Length > 128 * 1024)
            {
                continue;
            }

            using StreamReader reader = new(path);
            string? first = await reader.ReadLineAsync(cancellationToken);
            if (string.IsNullOrWhiteSpace(first))
            {
                continue;
            }

            string title = first.TrimStart('#', ' ');
            if (title.Length > 240)
            {
                title = title[..240];
            }

            prompts.Add(new LocalAiPromptDescriptor(
                id, title, id.StartsWith("frt100-",
                    StringComparison.Ordinal) && id != "frt100-system-v1"));
        }

        return prompts.OrderBy(item => item.IsFrt100 ? 0 : 1)
            .ThenBy(item => item.Id, StringComparer.Ordinal)
            .ToArray();
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

        FileInfo source = new(path);
        if ((source.Attributes & FileAttributes.ReparsePoint) != 0 ||
            source.Length > 128 * 1024)
        {
            throw new InvalidDataException(
                "Prompt non sûr ou trop volumineux (128 Kio max).");
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


/// <summary>One safe locally readable user-prompt entry.</summary>
/// <param name="Id">Stable key without extension.</param>
/// <param name="Title">First Markdown title, for UI selection only.</param>
/// <param name="IsFrt100">Uses the separate FRT-100 system reference.</param>
public sealed record LocalAiPromptDescriptor(
    string Id, string Title, bool IsFrt100)
{
    /// <summary>Readable UI label without exposing source content.</summary>
    public string DisplayName => Id + " — " + Title;
}
