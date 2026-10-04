namespace Nodalis.Infrastructure.Persistence;

public static class WindowsPathRules
{
    private static readonly HashSet<string> ReservedNames = new(
        BuildReservedNames(),
        StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Performs the <c>SanitizeSegment</c> operation.
    /// </summary>
    /// <param name="value">The <c>value</c> value.</param>
    /// <returns>The result of the operation.</returns>
public static string SanitizeSegment(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);

        char[] invalidCharacters = Path.GetInvalidFileNameChars();
        string sanitized = new string(
            value.Trim()
                .Select(character => invalidCharacters.Contains(character) ? '_' : character)
                .ToArray())
            .TrimEnd('.', ' ');

        if (string.IsNullOrWhiteSpace(sanitized))
        {
            sanitized = "_";
        }

        string stem = Path.GetFileNameWithoutExtension(sanitized);
        if (ReservedNames.Contains(stem))
        {
            sanitized = $"_{sanitized}";
        }

        return sanitized;
    }

    /// <summary>
    /// Performs the <c>GetUniqueFilePath</c> operation.
    /// </summary>
    /// <param name="parentDirectory">The <c>parentDirectory</c> value.</param>
    /// <param name="desiredFileName">The <c>desiredFileName</c> value.</param>
    /// <returns>The result of the operation.</returns>
public static string GetUniqueFilePath(
        string parentDirectory,
        string desiredFileName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(parentDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(desiredFileName);

        string extension = Path.GetExtension(desiredFileName);
        string stem = Path.GetFileNameWithoutExtension(desiredFileName);

        string safeStem = SanitizeSegment(stem);
        string safeExtension = string.IsNullOrWhiteSpace(extension)
            ? string.Empty
            : new string(
                extension
                    .Select(character =>
                        Path.GetInvalidFileNameChars().Contains(character)
                            ? '_'
                            : character)
                    .ToArray());

        string candidate = Path.Combine(
            parentDirectory,
            safeStem + safeExtension);

        if (!File.Exists(candidate) && !Directory.Exists(candidate))
        {
            return candidate;
        }

        for (int index = 2; ; index++)
        {
            candidate = Path.Combine(
                parentDirectory,
                $"{safeStem} ({index}){safeExtension}");

            if (!File.Exists(candidate) && !Directory.Exists(candidate))
            {
                return candidate;
            }
        }
    }

    /// <summary>
    /// Performs the <c>GetUniqueDirectoryPath</c> operation.
    /// </summary>
    /// <param name="parentDirectory">The <c>parentDirectory</c> value.</param>
    /// <param name="desiredName">The <c>desiredName</c> value.</param>
    /// <returns>The result of the operation.</returns>
public static string GetUniqueDirectoryPath(string parentDirectory, string desiredName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(parentDirectory);

        string safeName = SanitizeSegment(desiredName);
        string candidate = Path.Combine(parentDirectory, safeName);

        if (!Directory.Exists(candidate) && !File.Exists(candidate))
        {
            return candidate;
        }

        for (int index = 2; ; index++)
        {
            candidate = Path.Combine(parentDirectory, $"{safeName} ({index})");
            if (!Directory.Exists(candidate) && !File.Exists(candidate))
            {
                return candidate;
            }
        }
    }

    /// <summary>
    /// Performs the <c>BuildReservedNames</c> operation.
    /// </summary>
    /// <returns>The result of the operation.</returns>
private static IEnumerable<string> BuildReservedNames()
    {
        yield return "CON";
        yield return "PRN";
        yield return "AUX";
        yield return "NUL";

        for (int index = 1; index <= 9; index++)
        {
            yield return $"COM{index}";
            yield return $"LPT{index}";
        }
    }
}
