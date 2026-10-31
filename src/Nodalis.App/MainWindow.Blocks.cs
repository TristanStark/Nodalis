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
    /// Shows an explicit single-marker preview for the heading nearest the
    /// caret, then inserts only the new marker using the existing editor
    /// autosave/conflict machinery. No separate file writer touches open tabs.
    /// </summary>
    /// <returns>An asynchronous user-controlled Markdown edit.</returns>
    private async Task AssignCurrentHeadingIdentityAsync()
    {
        if (_workspaceReadOnly)
        {
            StatusText.Text = "Workspace en lecture seule : attribution impossible.";
            return;
        }

        DocumentTabViewModel? tab = GetFocusedDocumentTab();
        if (tab is null || tab.IsMissing)
        {
            StatusText.Text = "Ouvrez un document Markdown pour identifier un titre.";
            return;
        }

        TextBox editor = GetFocusedEditorTextBox();
        string source = editor.Text;
        int caret = editor.CaretIndex;
        MarkdownBlockIdentityScan scan = await Task.Run(
            () => MarkdownBlockIdentityCodec.Inspect(source));

        if (!ReferenceEquals(tab, GetFocusedDocumentTab()) ||
            !string.Equals(source, editor.Text, StringComparison.Ordinal))
        {
            StatusText.Text = "Document modifié : recommencez la préparation.";
            return;
        }

        if (scan.Issues.Count != 0)
        {
            StatusText.Text = "Corrigez les identifiants de blocs invalides ou dupliqués avant d'en créer un.";
            return;
        }

        MarkdownBlockIdentityEntry? heading = scan.Blocks
            .LastOrDefault(item => item.HeadingOffset <= caret);

        if (heading is null)
        {
            StatusText.Text = "Placez le curseur sous un titre Markdown valide.";
            return;
        }

        if (heading.Id is Guid existing)
        {
            MessageBox.Show(this,
                $"Le titre « {heading.Title} » possède déjà l'identifiant :\n{existing:D}",
                "Bloc déjà identifié", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        LinkIndexCatalog catalog = await _linkIndexService.RefreshAsync();
        Guid blockId = Guid.NewGuid();
        while (catalog.Targets.Any(target => target.Id == blockId) ||
               catalog.Blocks.Any(block => block.Id == blockId))
        {
            blockId = Guid.NewGuid();
        }

        string proposed = await Task.Run(() =>
            MarkdownBlockIdentityCodec.InsertMarker(source, heading.HeadingLine, blockId));

        // The codec performs one insertion only. Calculate the minimal text
        // addition off-thread, preserving undo history and cursor position.
        (int Offset, string InsertedText) insertion = await Task.Run(() =>
        {
            int prefix = 0;
            while (prefix < source.Length && source[prefix] == proposed[prefix])
            {
                prefix++;
            }

            int length = proposed.Length - source.Length;
            if (length <= 0 ||
                !source.AsSpan(prefix).SequenceEqual(proposed.AsSpan(prefix + length)))
            {
                throw new InvalidDataException("Aucune insertion minimale sûre n'a été trouvée.");
            }

            return (prefix, proposed.Substring(prefix, length));
        });

        MessageBoxResult answer = MessageBox.Show(this,
            $"Identifier le titre « {heading.Title} » (ligne {heading.HeadingLine}) ?\n\n" +
            $"Ajouter dans ce Markdown :\n{MarkdownBlockIdentityCodec.FormatMarker(blockId)}\n\n" +
            "Aucun autre contenu ne sera modifié. Confirmer l'insertion ?",
            "Nodalis 2.0 · attribution explicite",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (answer != MessageBoxResult.Yes)
        {
            return;
        }

        if (!ReferenceEquals(tab, GetFocusedDocumentTab()) ||
            !string.Equals(source, editor.Text, StringComparison.Ordinal))
        {
            StatusText.Text = "Document modifié après l'aperçu : aucune insertion effectuée.";
            return;
        }

        editor.Select(insertion.Offset, 0);
        editor.SelectedText = insertion.InsertedText;
        await SaveFocusedDocumentAsync();
        await RefreshLinkIndexAndContextAsync();
        StatusText.Text = $"Bloc identifié · {heading.Title} · {blockId:D}";
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
        RefreshBlockRelationContext(index, id);
    }
}
