using System.Windows;
using Nodalis.App.Dialogs;
using Nodalis.Core.Links;

namespace Nodalis.App;

public partial class MainWindow
{
    /// <summary>
    /// Opens the advanced incoming-reference explorer for the selected element.
    /// </summary>
    /// <param name="sender">The explorer button.</param>
    /// <param name="e">The routed event arguments.</param>
    private async void ExploreReferencesButton_Click(
            object sender,
            RoutedEventArgs e) =>
            await ShowReferenceExplorerAsync();

    /// <summary>
    /// Refreshes the derived index and lets the user filter incoming links and typed relations.
    /// </summary>
    /// <returns>A task representing index refresh and optional source navigation.</returns>
    private async Task ShowReferenceExplorerAsync()
    {
        await RunUiActionAsync(
            "Références · exploration",
            async () =>
            {
            if (_selectedNode is null)
            {
                StatusText.Text =
                    "Sélectionnez un élément avant d'explorer ses références.";
                return;
            }
    
            await RefreshLinkIndexAsync();
    
            LinkTargetEntry? target =
                FindIndexedTarget(
                    _selectedNode);
    
            if (target is null)
            {
                ExploreReferencesButton.IsEnabled =
                    false;
                StatusText.Text =
                    "Cet élément n'est pas une cible indexée.";
                return;
            }
    
            IReadOnlyList<ReferenceSearchEntry> entries =
                ReferenceIndexQuery.GetIncoming(
                    _linkIndex,
                    target.Id);
    
            ReferenceExplorerDialog dialog =
                new ReferenceExplorerDialog(
                    target,
                    entries)
                {
                    Owner =
                        this
                };
    
            if (dialog.ShowDialog() != true ||
                dialog.SelectedEntry is null)
            {
                return;
            }
    
            LinkTargetEntry? source =
                _linkIndex.Targets.FirstOrDefault(candidate =>
                    candidate.Id ==
                    dialog.SelectedEntry.Source.Id);
    
            if (source is null)
            {
                StatusText.Text =
                    $"Source de référence introuvable : {dialog.SelectedEntry.Source.DisplayName}";
                return;
            }
    
            await NavigateToLinkTargetAsync(
                source,
                dialog.SelectedEntry.LineNumber);
            });
    }
}
