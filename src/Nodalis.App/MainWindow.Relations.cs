using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Nodalis.App.Dialogs;
using Nodalis.App.Relations;
using Nodalis.Core.Links;
using Nodalis.Core.Navigation;
using Nodalis.Core.Relations;

namespace Nodalis.App;

public partial class MainWindow
{
    /// <summary>
    /// Opens the relation editor for the active Markdown document.
    /// </summary>
    /// <param name="sender">The add-relation button.</param>
    /// <param name="e">The routed event arguments.</param>
    private async void AddRelationButton_Click(
            object sender,
            RoutedEventArgs e) =>
            await AddRelationAsync();

    /// <summary>
    /// Adds one readable typed relation at the current editor position.
    /// </summary>
    /// <returns>A task representing the insertion, save and index refresh.</returns>
    private async Task AddRelationAsync()
    {
        await RunUiActionAsync(
            "Relations · ajout",
            async () =>
            {
                if (_selectedNode?.Kind != WorkspaceNodeKind.Document)
                {
                    StatusText.Text =
                        "Les relations sortantes s'ajoutent depuis un document Markdown.";
                    return;
                }

                LinkTargetEntry? source =
                    FindIndexedTarget(
                        _selectedNode);

                if (source is null)
                {
                    await RefreshLinkIndexAsync();
                    source =
                        FindIndexedTarget(
                            _selectedNode);
                }

                if (source is null)
                {
                    StatusText.Text =
                        "Impossible d'indexer le document source de la relation.";
                    return;
                }

                LinkTargetEntry[] targets =
                    _linkIndex.Targets
                        .Where(target =>
                            target.Id != source.Id)
                        .OrderBy(
                            target => target.QualifiedName,
                            StringComparer.CurrentCultureIgnoreCase)
                        .ToArray();

                if (targets.Length == 0)
                {
                    StatusText.Text =
                        "Aucune autre cible indexée dans le workspace.";
                    return;
                }

                RelationDialog dialog =
                    new RelationDialog(
                        targets)
                    {
                        Owner =
                            this
                    };

                if (dialog.ShowDialog() != true ||
                    dialog.SelectedTarget is null)
                {
                    return;
                }

                TextBox editor =
                    GetFocusedEditorTextBox();

                string relationLine =
                    MarkdownRelationParser.Format(
                        dialog.RelationType,
                        dialog.SelectedTarget.Id,
                        dialog.SelectedTarget.DisplayName);

                int insertionIndex =
                    editor.SelectionStart;

                bool needsLeadingBreak =
                    insertionIndex > 0 &&
                    editor.Text[insertionIndex - 1] != '\n';

                bool needsTrailingBreak =
                    insertionIndex < editor.Text.Length &&
                    editor.Text[insertionIndex] != '\r' &&
                    editor.Text[insertionIndex] != '\n';

                string insertedText =
                    (needsLeadingBreak
                        ? Environment.NewLine
                        : string.Empty) +
                    relationLine +
                    (needsTrailingBreak
                        ? Environment.NewLine
                        : string.Empty);

                editor.SelectedText =
                    insertedText;
                editor.CaretIndex =
                    Math.Min(
                        editor.Text.Length,
                        insertionIndex +
                        insertedText.Length);
                editor.SelectionLength =
                    0;

                await SaveFocusedDocumentAsync();
                await RefreshLinkIndexAndContextAsync();

                StatusText.Text =
                    $"Relation ajoutée · {dialog.RelationType} → {dialog.SelectedTarget.DisplayName}";
            });
    }

    /// <summary>
    /// Rebuilds the context-panel relation list for an indexed element.
    /// </summary>
    /// <param name="current">The selected indexed element, or null when no relation context is available.</param>
    private void RefreshRelationsContext(
            LinkTargetEntry? current)
    {
        AddRelationButton.IsEnabled =
            current?.Kind == LinkTargetKind.Document;
        ExploreReferencesButton.IsEnabled =
            current is not null;

        if (current is null)
        {
            RelationsList.ItemsSource =
                null;
            RelationsEmptyText.Visibility =
                Visibility.Visible;
            RelationsEmptyText.Text =
                "Aucune relation dans ce contexte.";
            return;
        }

        global::System.Collections.Generic.List<global::Nodalis.App.Relations.RelationContextItemViewModel> items =
            new List<RelationContextItemViewModel>();

        foreach (TypedRelationEntry relation in _linkIndex.Relations.Where(candidate =>
                     candidate.SourceId == current.Id))
        {
            items.Add(
                CreateOutgoingRelationItem(
                    relation));
        }

        foreach (TypedRelationEntry relation in _linkIndex.Relations.Where(candidate =>
                     candidate.TargetId == current.Id))
        {
            items.Add(
                CreateIncomingRelationItem(
                    relation));
        }

        RelationContextItemViewModel[] ordered =
            items
                .OrderBy(item =>
                    item.IsBroken)
                .ThenBy(
                    item => item.RelationType,
                    StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(
                    item => item.DisplayName,
                    StringComparer.CurrentCultureIgnoreCase)
                .ToArray();

        RelationsList.ItemsSource =
            ordered;
        RelationsEmptyText.Visibility =
            ordered.Length == 0
                ? Visibility.Visible
                : Visibility.Collapsed;
        RelationsEmptyText.Text =
            "Aucune relation.";
    }

    /// <summary>
    /// Creates an outgoing relation row and resolves its target by stable identifier.
    /// </summary>
    /// <param name="relation">The indexed relation.</param>
    /// <returns>The outgoing relation view model.</returns>
    private RelationContextItemViewModel CreateOutgoingRelationItem(
            TypedRelationEntry relation)
    {
        LinkTargetEntry? target =
            relation.TargetId is Guid targetId
                ? _linkIndex.Targets.FirstOrDefault(candidate =>
                    candidate.Id == targetId)
                : null;

        bool broken =
            target is null;

        return new RelationContextItemViewModel
        {
            RelationType =
                relation.RelationType,
            Direction =
                "→",
            DisplayName =
                target?.DisplayName ??
                relation.TargetLabel ??
                relation.RawTargetId,
            Detail =
                broken
                    ? $"Cible introuvable · {relation.RawTargetId}"
                    : target!.QualifiedName,
            NavigationTargetId =
                target?.Id,
            NavigationLineNumber =
                null,
            IsBroken =
                broken
        };
    }

    /// <summary>
    /// Creates an incoming relation row and links it back to the source occurrence.
    /// </summary>
    /// <param name="relation">The indexed relation.</param>
    /// <returns>The incoming relation view model.</returns>
    private RelationContextItemViewModel CreateIncomingRelationItem(
            TypedRelationEntry relation)
    {
        LinkTargetEntry? source =
            _linkIndex.Targets.FirstOrDefault(candidate =>
                candidate.Id == relation.SourceId);

        bool broken =
            source is null;

        return new RelationContextItemViewModel
        {
            RelationType =
                relation.RelationType,
            Direction =
                "←",
            DisplayName =
                source?.DisplayName ??
                relation.SourceId.ToString(
                    "D"),
            Detail =
                broken
                    ? "Document source introuvable"
                    : $"{source!.QualifiedName} · ligne {relation.LineNumber}",
            NavigationTargetId =
                source?.Id,
            NavigationLineNumber =
                source is null
                    ? null
                    : relation.LineNumber,
            IsBroken =
                broken
        };
    }

    /// <summary>
    /// Navigates to the related target or source occurrence selected in the relation panel.
    /// </summary>
    /// <param name="sender">The relations list.</param>
    /// <param name="e">The mouse event arguments.</param>
    private async void RelationsList_MouseDoubleClick(
            object sender,
            MouseButtonEventArgs e)
    {
        await RunUiActionAsync(
            "Relations · navigation",
            async () =>
            {
                if (sender is not ListBox list ||
                    list.SelectedItem is not
                        RelationContextItemViewModel item)
                {
                    return;
                }

                if (item.NavigationTargetId is not
                    Guid navigationTargetId)
                {
                    StatusText.Text =
                        $"⚠ Relation cassée · {item.RelationType} → {item.DisplayName}";
                    return;
                }

                LinkTargetEntry? target =
                    _linkIndex.Targets.FirstOrDefault(candidate =>
                        candidate.Id == navigationTargetId);

                if (target is null)
                {
                    StatusText.Text =
                        $"⚠ Cible de relation introuvable · {item.DisplayName}";
                    return;
                }

                await NavigateToLinkTargetAsync(
                    target,
                    item.NavigationLineNumber);
            });
    }
}
