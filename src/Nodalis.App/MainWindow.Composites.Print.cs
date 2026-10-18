using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using Nodalis.App.Markdown;
using Nodalis.Core.Composites;
using Nodalis.Core.Links;
using Nodalis.Infrastructure.Composites;

namespace Nodalis.App;

/// <summary>Offers an offline WPF printout of the currently resolved composite document.</summary>
public partial class MainWindow
{
    /// <summary>Builds the latest snapshot and opens the standard Windows print dialog.</summary>
    /// <returns>Completion or user cancellation of the native print dialog.</returns>
    private async Task PrintCompositeDocumentAsync()
    {
        if (_activeDocumentTab is null)
        {
            StatusText.Text = "Ouvrez un document avant de lancer l'impression.";
            return;
        }

        string path = _activeDocumentTab.FullPath;
        string text = MarkdownEditorTextBox.Text;
        LinkIndexCatalog index = await _linkIndexService.LoadAsync();
        CompositeWorkspaceRenderer renderer = new(_root.FullPath, index);
        CompositeRenderResult result = await renderer.RenderAsync(path, text);
        FlowDocument flow = MarkdownFlowDocumentRenderer.Render(
            result.Markdown, Path.GetDirectoryName(path),
            OnCompositeSourceClicked, OnMarkdownLinkClicked);

        PrintDialog dialog = new();
        if (dialog.ShowDialog() == true)
        {
            flow.PageWidth = dialog.PrintableAreaWidth;
            flow.PageHeight = dialog.PrintableAreaHeight;
            flow.ColumnWidth = dialog.PrintableAreaWidth;
            IDocumentPaginatorSource source = flow;
            dialog.PrintDocument(source.DocumentPaginator,
                "Nodalis — " + Path.GetFileNameWithoutExtension(path));
            StatusText.Text = "Impression demandée" +
                (result.Diagnostics.Count == 0 ? "." :
                    " (" + result.Diagnostics.Count + " inclusion(s) indisponible(s)).");
        }
    }
}
