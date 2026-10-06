using Nodalis.Core.AI;

namespace Nodalis.App.Dialogs;

/// <summary>
/// Exposes one selectable local-AI context source and its exact content preview.
/// </summary>
public sealed class AiContextSelectionItemViewModel
{
    /// <summary>
    /// Initializes one context candidate.
    /// </summary>
    /// <param name="item">The exact context item.</param>
    /// <param name="selected">Whether it is initially selected.</param>
    public AiContextSelectionItemViewModel(
            LocalAiContextItem item,
            bool selected)
    {
        ArgumentNullException.ThrowIfNull(
            item);

        Item =
            item;
        IsSelected =
            selected;
    }

    /// <summary>Gets the exact context item.</summary>
    public LocalAiContextItem Item { get; }

    /// <summary>Gets the source label.</summary>
    public string Label =>
        Item.Label;

    /// <summary>Gets the exact source content.</summary>
    public string Content =>
        Item.Content;

    /// <summary>Gets a compact size label.</summary>
    public string SizeLabel =>
        Item.Content.Length.ToString(
            "N0") +
        " caractères";

    /// <summary>Gets or sets whether the source will be included in the request.</summary>
    public bool IsSelected { get; set; }
}
