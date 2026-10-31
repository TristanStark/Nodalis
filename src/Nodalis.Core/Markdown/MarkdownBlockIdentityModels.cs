namespace Nodalis.Core.Markdown;

/// <summary>
/// Identifies one Markdown heading and its optional persistent Nodalis block marker.
/// A heading without a marker remains a valid, unmodified legacy heading.
/// </summary>
/// <param name="HeadingLine">One-based source line of the heading.</param>
/// <param name="Title">Current visible heading title (not used as identity).</param>
/// <param name="Level">ATX heading depth.</param>
/// <param name="HeadingOffset">Zero-based offset in the original source.</param>
/// <param name="Id">Stable identity, or null when absent or malformed.</param>
/// <param name="HasMarker">Whether an identity-like marker immediately follows the heading.</param>
/// <param name="MarkerLine">One-based marker line, when present.</param>
public sealed record MarkdownBlockIdentityEntry(
    int HeadingLine,
    string Title,
    int Level,
    int HeadingOffset,
    Guid? Id,
    bool HasMarker,
    int? MarkerLine);

/// <summary>
/// Describes an invalid or duplicated explicit block identity without repairing it silently.
/// </summary>
public enum MarkdownBlockIdentityIssueKind
{
    MalformedMarker,
    DuplicateId
}

/// <summary>
/// Reports a source problem associated with a block marker.
/// </summary>
/// <param name="Kind">The kind of problem.</param>
/// <param name="HeadingLine">Source heading line.</param>
/// <param name="MarkerLine">Source marker line.</param>
/// <param name="Id">Parsed identifier, if any.</param>
public sealed record MarkdownBlockIdentityIssue(
    MarkdownBlockIdentityIssueKind Kind,
    int HeadingLine,
    int MarkerLine,
    Guid? Id);

/// <summary>
/// Immutable result of inspecting block markers in one Markdown source.
/// </summary>
/// <param name="Blocks">All discovered ATX headings, including unmarked headings.</param>
/// <param name="Issues">Malformed or duplicated marker diagnostics.</param>
public sealed record MarkdownBlockIdentityScan(
    IReadOnlyList<MarkdownBlockIdentityEntry> Blocks,
    IReadOnlyList<MarkdownBlockIdentityIssue> Issues);

/// <summary>
/// Provides a source and logical path for workspace-wide collision analysis.
/// </summary>
/// <param name="RelativePath">Document path relative to the workspace.</param>
/// <param name="Markdown">Current canonical Markdown source.</param>
public sealed record MarkdownBlockIdentitySource(
    string RelativePath,
    string Markdown);

/// <summary>
/// Identifies an identity reused in two different Markdown documents.
/// Neither identity is silently reassigned.
/// </summary>
/// <param name="Id">Repeated block identifier.</param>
/// <param name="FirstPath">First document path.</param>
/// <param name="FirstLine">First heading line.</param>
/// <param name="SecondPath">Conflicting document path.</param>
/// <param name="SecondLine">Conflicting heading line.</param>
public sealed record MarkdownBlockIdentityCollision(
    Guid Id,
    string FirstPath,
    int FirstLine,
    string SecondPath,
    int SecondLine);
