using System.IO;
using Microsoft.Win32;
using Nodalis.Core.Links;
using Nodalis.Infrastructure.Composites;

namespace Nodalis.App;

/// <summary>Exports one composite snapshot via the existing Command Palette.</summary>
public partial class MainWindow
{
    /// <summary>Prompts for a safe local Markdown, HTML or DOCX export destination.</summary>
    /// <returns>Completed export or cancelled user selection.</returns>
    private async Task ExportCompositeDocumentAsync()
    {
        if (_activeDocumentTab is null)
        {
            StatusText.Text = "Ouvrez un document Markdown avant de l'exporter.";
            return;
        }

        string parent = _activeDocumentTab.FullPath;
        string markdown = MarkdownEditorTextBox.Text;
        SaveFileDialog dialog = new()
        {
            Title = "Exporter le document composite",
            FileName = Path.GetFileNameWithoutExtension(parent),
            Filter = "Markdown avec références (*.md)|*.md|" +
                "Markdown résolu (*.md)|*.md|" +
                "HTML autonome résolu (*.html)|*.html|" +
                "Word DOCX résolu (*.docx)|*.docx",
            FilterIndex = 2,
            OverwritePrompt = true
        };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        if (string.Equals(Path.GetFullPath(dialog.FileName),
            Path.GetFullPath(parent), StringComparison.OrdinalIgnoreCase))
        {
            StatusText.Text = "Impossible de remplacer le document source pendant l'export.";
            return;
        }

        int selected = dialog.FilterIndex;
        CompositeExportFormat format = selected switch
        {
            3 => CompositeExportFormat.Html,
            4 => CompositeExportFormat.Docx,
            _ => CompositeExportFormat.Markdown
        };
        CompositeExportMode mode = selected == 1
            ? CompositeExportMode.References : CompositeExportMode.Resolved;
        LinkIndexCatalog index = await _linkIndexService.LoadAsync();
        CompositeDocumentExportService export = new(_root.FullPath, index);
        int diagnostics = await export.ExportAsync(parent, markdown,
            dialog.FileName, format, mode);
        StatusText.Text = "Export composite : " + dialog.FileName +
            (diagnostics == 0 ? "" : " · " + diagnostics + " inclusion(s) indisponible(s)");
    }
}
