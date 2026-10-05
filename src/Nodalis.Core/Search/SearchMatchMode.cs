namespace Nodalis.Core.Search;

/// <summary>
/// Selects how textual search terms are matched.
/// </summary>
public enum SearchMatchMode
{
    /// <summary>
    /// Allows deterministic typo, prefix and partial-word matching.
    /// </summary>
    Fuzzy,

    /// <summary>
    /// Requires an exact case-insensitive substring match.
    /// </summary>
    Exact
}
