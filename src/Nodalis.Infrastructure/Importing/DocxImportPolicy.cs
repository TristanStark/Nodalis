namespace Nodalis.Infrastructure.Importing;

/// <summary>
/// Fail-closed restriction: DOCX import, reading, reimport and conversion
/// are forbidden. No preference, environment switch or test-only bypass.
/// Historical recovery of incomplete imports remains available.
/// </summary>
public static class DocxImportPolicy
{
    /// <summary>Displayed when an obsolete entry point is invoked.</summary>
    public const string DisabledMessage =
        "L'importation et la conversion des fichiers DOCX sont désactivées pour des raisons de conformité.";

    /// <summary>Refuses access before opening, parsing, staging or writing a DOCX.</summary>
    /// <exception cref="NotSupportedException">DOCX import and conversion are disabled.</exception>
    public static void EnsureAllowed()
    {
        throw new NotSupportedException(DisabledMessage);
    }
}
