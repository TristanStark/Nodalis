using System.IO;
using System.Windows;
using System.Windows.Controls;
using Nodalis.App.Dialogs;
using Nodalis.App.Editor;
using Nodalis.Core.Links;
using Nodalis.Core.Markdown;

namespace Nodalis.App;

public partial class MainWindow
{
    /// <summary>
    /// Requests a stable Markdown block GUID and opens its current heading.
    /// </summary>
    /// <returns>A task representing read-only navigation.</returns>
    private async Task ShowBlockNavigationAsync()
    {
        TextPromptDialog dialog = new TextPromptDialog(
            "Atteindre un bloc Markdown",
            "Saisissez l'identifiant GUID du critère ou du test à ouvrir.")
        {
            Owner = this
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        if (!Guid.TryParse(dialog.Value, out Guid id) || id == Guid.Empty)
        {
            StatusText.Text = "Identifiant de bloc invalide : un GUID est attendu.";
            return;
        }

        await NavigateToMarkdownBlockAsync(id);
    }

    /// <summary>
    /// Resolves a block against the shared index, opens its document and
    /// revalidates the live text before positioning the caret.
    /// </summary>
    /// <param name="id">Stable GUID of the target block.</param>
    /// <returns>Read-only navigation with explicit ambiguity diagnostics.</returns>
    private async Task NavigateToMarkdownBlockAsync(Guid id)
    {
        LinkIndexCatalog index = await _linkIndexService.RefreshAsync();
        MarkdownBlockIndexResolution resolution = BlockIndexQuery.Resolve(index, id);

        if (resolution.Status != LinkResolutionStatus.Resolved ||
            resolution.Document is null)
        {
            StatusText.Text = resolution.Status == LinkResolutionStatus.Ambiguous
                ? $"⚠ Bloc {id:D} ambigu : plusieurs identifiants en collision."
                : $"⚠ Bloc {id:D} introuvable.";
            return;
        }

        LinkTargetEntry document = resolution.Document;
        string fullPath = Path.GetFullPath(Path.Combine(
            _root.FullPath,
            document.RelativePath.Replace('/', Path.DirectorySeparatorChar)));

        await NavigateToLinkTargetAsync(document);

        DocumentTabViewModel? active = GetFocusedDocumentTab();
        if (active is null ||
            !string.Equals(Path.GetFullPath(active.FullPath), fullPath,
                StringComparison.OrdinalIgnoreCase))
        {
            // Opening may be refused due to an unsaved-document confirmation.
            return;
        }

        TextBox editor = GetFocusedEditorTextBox();
        string snapshot = editor.Text;
        MarkdownBlockIdentityScan scan = await Task.Run(
            () => MarkdownBlockIdentityCodec.Inspect(snapshot));

        if (!ReferenceEquals(active, GetFocusedDocumentTab()) ||
            !string.Equals(snapshot, editor.Text, StringComparison.Ordinal))
        {
            StatusText.Text = "Document modifié pendant la navigation ; réessayez.";
            return;
        }

        MarkdownBlockIdentityEntry[] matches = scan.Blocks
            .Where(block => block.Id == id)
            .ToArray();

        if (matches.Length != 1 || scan.Issues.Any(issue => issue.Id == id))
        {
            StatusText.Text = matches.Length > 1
                ? "⚠ Plusieurs blocs utilisent ce même identifiant."
                : "⚠ Identifiant absent ou modifié dans le document affiché.";
            return;
        }

        MarkdownBlockIdentityEntry heading = matches[0];
        editor.Focus();
        editor.CaretIndex = Math.Clamp(heading.HeadingOffset, 0, editor.Text.Length);
        editor.SelectionLength = 0;
        editor.ScrollToLine(Math.Max(0, heading.HeadingLine - 1));

        StatusText.Text = $"Bloc · {heading.Title} · ligne {heading.HeadingLine}";
    }
}
