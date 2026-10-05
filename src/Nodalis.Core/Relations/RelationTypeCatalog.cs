namespace Nodalis.Core.Relations;

/// <summary>
/// Provides the initial relation vocabulary offered by the Nodalis UI.
/// Parsers intentionally accept values outside this catalog.
/// </summary>
public static class RelationTypeCatalog
{
    private static readonly string[] InitialTypes =
    [
        "dépend de",
        "remplace",
        "implémente",
        "teste",
        "documente",
        "bloque",
        "est lié à"
    ];

    /// <summary>
    /// Gets the initial relation types proposed by the UI.
    /// </summary>
    public static IReadOnlyList<string> KnownTypes =>
        InitialTypes;
}
