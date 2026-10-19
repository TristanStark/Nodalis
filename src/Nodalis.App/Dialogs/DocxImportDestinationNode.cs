using System.Collections.ObjectModel;

namespace Nodalis.App.Dialogs;

/// <summary>
/// A destination tree node shown by the structured import mapping UI.
/// </summary>
public sealed class DocxImportDestinationNode
{
    public required string Label { get; init; }

    /// <summary>
    /// Non-null only for sections actually selectable as mapping targets.
    /// </summary>
    public string? TargetSection { get; init; }

    public ObservableCollection<DocxImportDestinationNode> Children { get; } = [];
}
