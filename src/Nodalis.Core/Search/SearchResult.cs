namespace Nodalis.Core.Search;

/// <summary>
/// Represents one ranked local Markdown search occurrence.
/// </summary>
public sealed record SearchResult
{
    /// <summary>
    /// Gets the contextual scope assigned to this occurrence.
    /// </summary>
    public required SearchScopeKind Scope { get; init; }

    /// <summary>
    /// Gets the absolute source file path.
    /// </summary>
    public required string FilePath { get; init; }

    /// <summary>
    /// Gets the source document display name.
    /// </summary>
    public required string DisplayName { get; init; }

    /// <summary>
    /// Gets the one-based source line number.
    /// </summary>
    public required int LineNumber { get; init; }

    /// <summary>
    /// Gets the compact source excerpt containing or approximating the occurrence.
    /// </summary>
    public required string Excerpt { get; init; }

    /// <summary>
    /// Gets the deterministic ranking score. Higher values are more relevant.
    /// </summary>
    public double Score { get; init; }

    /// <summary>
    /// Gets the location category used to weight this match.
    /// </summary>
    public SearchMatchKind MatchKind { get; init; }

    /// <summary>
    /// Gets a compact localized description for the match location.
    /// </summary>
    public string MatchKindDisplay =>
        MatchKind switch
        {
            SearchMatchKind.FileName => "Titre",
            SearchMatchKind.Heading => "Heading",
            SearchMatchKind.Metadata => "Métadonnée",
            _ => "Corps"
        };
}
