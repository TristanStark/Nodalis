using System.Windows;
using Nodalis.App.Markdown;
using Nodalis.Core.Notes;
using Nodalis.Infrastructure.Notes;

namespace Nodalis.App.Dialogs;

public partial class QuickNotesOverviewDialog : Window
{
    public QuickNotesOverviewDialog(
        IReadOnlyList<QuickNotesSnapshot> snapshots,
        string workspaceRoot)
    {
        ArgumentNullException.ThrowIfNull(snapshots);

        InitializeComponent();

        var markdown = QuickNotesService.FormatAggregateMarkdown(
            snapshots);

        Preview.Document = MarkdownFlowDocumentRenderer.Render(
            markdown,
            workspaceRoot);
    }
}
