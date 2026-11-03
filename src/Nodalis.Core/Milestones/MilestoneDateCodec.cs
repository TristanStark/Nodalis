using System.Globalization;

namespace Nodalis.Core.Milestones;

/// <summary>
/// One shared milestone date contract for editing, validation and coverage.
/// Persisted rows use the French display format; ISO rows remain readable.
/// </summary>
public static class MilestoneDateCodec
{
    private static readonly string[] AcceptedFormats =
        ["dd/MM/yyyy", "d/M/yyyy", "yyyy-MM-dd"];

    /// <summary>Reads a project milestone date without culture guessing.</summary>
    /// <param name="value">Date cell from Markdown.</param>
    /// <param name="date">Parsed Gregorian date.</param>
    /// <returns>True when the cell contains an accepted real calendar date.</returns>
    public static bool TryParse(string? value, out DateOnly date) =>
        DateOnly.TryParseExact(value?.Trim(), AcceptedFormats,
            CultureInfo.InvariantCulture, DateTimeStyles.None, out date);

    /// <summary>Formats a saved milestone date as day/month/year.</summary>
    /// <param name="date">Calendar date.</param>
    /// <returns>Stable canonical date representation.</returns>
    public static string Format(DateOnly date) =>
        date.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);
}
