namespace Nodalis.Infrastructure.Persistence;

public static class WindowsPathRules
{
    private static readonly HashSet<string> ReservedNames = new(
        BuildReservedNames(),
        StringComparer.OrdinalIgnoreCase);

    public static string SanitizeSegment(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);

        var invalidCharacters = Path.GetInvalidFileNameChars();
        var sanitized = new string(
            value.Trim()
                .Select(character => invalidCharacters.Contains(character) ? '_' : character)
                .ToArray())
            .TrimEnd('.', ' ');

        if (string.IsNullOrWhiteSpace(sanitized))
        {
            sanitized = "_";
        }

        var stem = Path.GetFileNameWithoutExtension(sanitized);
        if (ReservedNames.Contains(stem))
        {
            sanitized = $"_{sanitized}";
        }

        return sanitized;
    }

    public static string GetUniqueFilePath(
        string parentDirectory,
        string desiredFileName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(parentDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(desiredFileName);

        var extension = Path.GetExtension(desiredFileName);
        var stem = Path.GetFileNameWithoutExtension(desiredFileName);

        var safeStem = SanitizeSegment(stem);
        var safeExtension = string.IsNullOrWhiteSpace(extension)
            ? string.Empty
            : new string(
                extension
                    .Select(character =>
                        Path.GetInvalidFileNameChars().Contains(character)
                            ? '_'
                            : character)
                    .ToArray());

        var candidate = Path.Combine(
            parentDirectory,
            safeStem + safeExtension);

        if (!File.Exists(candidate) && !Directory.Exists(candidate))
        {
            return candidate;
        }

        for (var index = 2; ; index++)
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

    public static string GetUniqueDirectoryPath(string parentDirectory, string desiredName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(parentDirectory);

        var safeName = SanitizeSegment(desiredName);
        var candidate = Path.Combine(parentDirectory, safeName);

        if (!Directory.Exists(candidate) && !File.Exists(candidate))
        {
            return candidate;
        }

        for (var index = 2; ; index++)
        {
            candidate = Path.Combine(parentDirectory, $"{safeName} ({index})");
            if (!Directory.Exists(candidate) && !File.Exists(candidate))
            {
                return candidate;
            }
        }
    }

    private static IEnumerable<string> BuildReservedNames()
    {
        yield return "CON";
        yield return "PRN";
        yield return "AUX";
        yield return "NUL";

        for (var index = 1; index <= 9; index++)
        {
            yield return $"COM{index}";
            yield return $"LPT{index}";
        }
    }
}
