using Nodalis.Core.Links;

namespace Nodalis.App.Links;

/// <summary>
/// Wraps one smart-rename source file with an explicit user selection.
/// </summary>
public sealed class RenameLinkFilePlanViewModel
{
    /// <summary>
    /// Gets the immutable rewrite plan for this source file.
    /// </summary>
    public required RenameLinkFilePlan Plan { get; init; }

    /// <summary>
    /// Gets or sets whether this source file will be rewritten.
    /// </summary>
    public bool IsSelected { get; set; } =
        true;

    /// <summary>
    /// Gets the compact file heading shown in the preview.
    /// </summary>
    public string Header =>
        $"{Plan.SourceDisplayName} · {Plan.Changes.Count} changement(s)";
}
