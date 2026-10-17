using System.Windows;
using Nodalis.App.Dialogs;
using Nodalis.Core.Impact;
using Nodalis.Core.Links;
using Nodalis.Core.Navigation;

namespace Nodalis.App;

public partial class MainWindow
{
    /// <summary>
    /// Opens the impact analysis command for the current Markdown document.
    /// </summary>
    /// <param name="sender">Analysis button.</param>
    /// <param name="e">Routed event.</param>
    private async void ImpactAnalysisButton_Click(object sender, RoutedEventArgs e)
    {
        await ShowImpactAnalysisAsync();
    }

    /// <summary>
    /// Rebuilds the local read-only graph and offers bounded analysis from the active document.
    /// </summary>
    /// <returns>Completion of analysis and optional navigation.</returns>
    private async Task ShowImpactAnalysisAsync()
    {
        await RunUiActionAsync(
            "Impacts · analyse",
            async () =>
            {
                if (_selectedNode?.Kind != WorkspaceNodeKind.Document)
                {
                    StatusText.Text = "Sélectionnez un document Markdown pour analyser ses impacts.";
                    return;
                }

                // Index refresh reads the canonical Markdown and preserves existing stable IDs.
                await RefreshLinkIndexAsync();
                LinkTargetEntry? origin = FindIndexedTarget(_selectedNode);
                if (origin is null)
                {
                    StatusText.Text = "Le document actif n'a pas d'identifiant d'index utilisable.";
                    return;
                }

                // The graph is entirely derived from the refreshed index; no workspace writes.
                LinkIndexCatalog snapshot = _linkIndex;
                ImpactGraphIndex graph = await Task.Run(() => ImpactGraphIndex.Build(snapshot));
                ImpactAnalysisDialog dialog = new(origin, graph)
                {
                    Owner = this
                };

                if (dialog.ShowDialog() != true || dialog.SelectedTarget is null)
                {
                    return;
                }

                await NavigateToLinkTargetAsync(dialog.SelectedTarget, dialog.SelectedLineNumber);
            });
    }
}
