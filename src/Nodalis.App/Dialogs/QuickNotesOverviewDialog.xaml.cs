using System.Windows;
using Nodalis.App.Markdown;
using Nodalis.Core.Notes;
using Nodalis.Infrastructure.Notes;

namespace Nodalis.App.Dialogs;

public partial class QuickNotesOverviewDialog : Window
{
    /// <summary>
    /// Initializes a new instance of <see cref="QuickNotesOverviewDialog"/>.
    /// </summary>
    /// <param name="snapshots">The <c>snapshots</c> value.</param>
    /// <param name="workspaceRoot">The <c>workspaceRoot</c> value.</param>
public QuickNotesOverviewDialog(
        IReadOnlyList<QuickNotesSnapshot> snapshots,
        string workspaceRoot)
    {
        ArgumentNullException.ThrowIfNull(snapshots);

        InitializeComponent();

        string markdown = QuickNotesService.FormatAggregateMarkdown(
            snapshots);

        Preview.Document = MarkdownFlowDocumentRenderer.Render(
            markdown,
            workspaceRoot);
    }
}
