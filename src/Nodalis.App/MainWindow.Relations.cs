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

                IReadOnlyList<RelationTargetOption> choices =
                    RelationTargetIndexQuery.GetSelectable(_linkIndex, source.Id);

                if (choices.Count == 0)
                {
                    StatusText.Text =
                        "Aucune cible identifiée et non ambiguë.";
                    return;
                }

                RelationDialog dialog =
                    new RelationDialog(
                        targets, choices)
                    {
                        Owner =
                            this
                    };

                if (dialog.ShowDialog() != true ||
                    dialog.SelectedTargetOption is not RelationTargetOption selected)
                {
                    return;
                }

                TextBox editor =
                    GetFocusedEditorTextBox();

                string relationLine =
                    MarkdownRelationParser.Format(
                        dialog.RelationType,
                        selected.Id,
                        selected.DisplayName);

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
                    $"Relation ajoutée · {dialog.RelationType} → {selected.DisplayName}" +
                    (selected.IsBlock ? " (bloc)" : string.Empty);
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
        ImpactAnalysisButton.IsEnabled =
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
        MarkdownBlockIndexResolution? block = relation.TargetId is Guid id
            ? BlockIndexQuery.Resolve(_linkIndex, id)
            : null;

        LinkTargetEntry[] entityCandidates =
            relation.TargetId is Guid targetId &&
            block?.Status != LinkResolutionStatus.Ambiguous &&
            block?.Status != LinkResolutionStatus.Resolved
                ? _linkIndex.Targets.Where(target =>
                    target.Id == targetId).Take(2).ToArray()
                : [];

        LinkTargetEntry? entity = entityCandidates.Length == 1
            ? entityCandidates[0]
            : null;

        bool resolvedBlock = block?.Status == LinkResolutionStatus.Resolved;
        bool ambiguous = block?.Status == LinkResolutionStatus.Ambiguous ||
            entityCandidates.Length > 1;
        bool broken = !resolvedBlock && entity is null;

        return new RelationContextItemViewModel
        {
            RelationType = relation.RelationType,
            Direction = "→",
            DisplayName = resolvedBlock
                ? block!.Block!.HeadingTitle
                : entity?.DisplayName ??
                  relation.TargetLabel ??
                  relation.RawTargetId,
            Detail = ambiguous
                ? $"GUID ambigu · {relation.RawTargetId}"
                : broken
                    ? $"Cible introuvable · {relation.RawTargetId}"
                    : resolvedBlock
                        ? $"{block!.Document!.QualifiedName} › {block.Block!.HeadingTitle}"
                        : entity!.QualifiedName,
            NavigationTargetId = broken ? null : relation.TargetId,
            NavigationLineNumber = null,
            IsBroken = broken
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

                MarkdownBlockIndexResolution block =
                    BlockIndexQuery.Resolve(_linkIndex, navigationTargetId);
                if (block.Status == LinkResolutionStatus.Ambiguous)
                {
                    StatusText.Text =
                        $"⚠ Cible de relation ambiguë · {item.DisplayName}";
                    return;
                }

                if (block.Status == LinkResolutionStatus.Resolved)
                {
                    await NavigateToMarkdownBlockAsync(navigationTargetId);
                    return;
                }

                LinkTargetEntry[] candidates = _linkIndex.Targets
                    .Where(candidate => candidate.Id == navigationTargetId)
                    .Take(2).ToArray();

                if (candidates.Length != 1)
                {
                    StatusText.Text =
                        $"⚠ Cible de relation introuvable ou ambiguë · {item.DisplayName}";
                    return;
                }

                await NavigateToLinkTargetAsync(
                    candidates[0],
                    item.NavigationLineNumber);
            });
    }

    /// <summary>
    /// Shows incoming and outgoing relations of one validated block in the
    /// existing context panel without creating a second persisted relation view.
    /// </summary>
    /// <param name="index">Fresh shared index snapshot used for navigation.</param>
    /// <param name="blockId">Stable block identity just navigated to.</param>
    private void RefreshBlockRelationContext(LinkIndexCatalog index, Guid blockId)
    {
        BlockIncomingRelationResult incoming =
            BlockRelationIndexQuery.GetIncoming(index, blockId);
        BlockOutgoingRelationResult outgoing =
            BlockRelationIndexQuery.GetOutgoing(index, blockId);

        if (incoming.Resolution.Status != LinkResolutionStatus.Resolved ||
            outgoing.Source.Status != LinkResolutionStatus.Resolved)
        {
            return;
        }

        List<RelationContextItemViewModel> items = [];

        foreach (BlockOutgoingRelationEntry entry in outgoing.Relations)
        {
            items.Add(new RelationContextItemViewModel
            {
                RelationType = entry.RelationType,
                Direction = "→",
                DisplayName = entry.TargetLabel,
                Detail = entry.TargetStatus == LinkResolutionStatus.Resolved
                    ? entry.TargetBlock is not null
                        ? $"{entry.TargetEntity!.QualifiedName} · bloc"
                        : entry.TargetEntity?.QualifiedName ?? "Cible"
                    : entry.TargetStatus == LinkResolutionStatus.Ambiguous
                        ? "Cible ambiguë"
                        : "Cible introuvable",
                NavigationTargetId =
                    entry.TargetStatus == LinkResolutionStatus.Resolved
                        ? entry.TargetId : null,
                IsBroken = entry.TargetStatus != LinkResolutionStatus.Resolved
            });
        }

        foreach (ReferenceSearchEntry reference in incoming.Relations)
        {
            items.Add(new RelationContextItemViewModel
            {
                RelationType = reference.RelationType ?? reference.TypeLabel,
                Direction = "←",
                DisplayName = reference.Source.DisplayName,
                Detail = reference.SourceBlockId is Guid
                    ? $"{reference.Source.QualifiedName} · bloc source · ligne {reference.LineNumber}"
                    : $"{reference.Source.QualifiedName} · ligne {reference.LineNumber}",
                NavigationTargetId = reference.Source.Id,
                NavigationLineNumber = reference.LineNumber,
                IsBroken = false
            });
        }

        RelationContextItemViewModel[] ordered = items
            .OrderBy(entry => entry.IsBroken)
            .ThenBy(entry => entry.RelationType,
                StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(entry => entry.DisplayName,
                StringComparer.CurrentCultureIgnoreCase)
            .ToArray();

        RelationsList.ItemsSource = ordered;
        RelationsEmptyText.Visibility =
            ordered.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        RelationsEmptyText.Text = "Aucune relation pour ce bloc.";
    }

}
