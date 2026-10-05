using System.Windows;
using Nodalis.App.Dialogs;
using Nodalis.Core.Links;
using Nodalis.Infrastructure.Links;

namespace Nodalis.App;

public partial class MainWindow
{
    private sealed record SmartRenameWorkflow
    {
        /// <summary>
        /// Gets the service that owns analysis and transactional rewrites.
        /// </summary>
        public required SmartLinkRenameService Service { get; init; }

        /// <summary>
        /// Gets the immutable preview plan captured before the rename.
        /// </summary>
        public required RenameLinkRewritePlan Plan { get; init; }

        /// <summary>
        /// Gets whether selected source files should be rewritten.
        /// </summary>
        public bool ApplyRewrites { get; init; }

        /// <summary>
        /// Gets the selected stable source document IDs.
        /// </summary>
        public IReadOnlyList<Guid> SelectedSourceIds { get; init; } =
            [];
    }

    /// <summary>
    /// Builds and, when needed, displays the explicit smart-rename rewrite preview.
    /// </summary>
    /// <param name="node">The navigation target about to be renamed.</param>
    /// <param name="newDisplayName">The proposed human-readable name.</param>
    /// <returns>The accepted workflow, or <see langword="null"/> when the user cancels.</returns>
    private async Task<SmartRenameWorkflow?> PrepareSmartRenameAsync(
            Navigation.NavigationNodeViewModel node,
            string newDisplayName)
    {
        await RefreshLinkIndexAsync();

        LinkTargetEntry? target =
            FindIndexedTarget(
                node);

        if (target is null)
        {
            MessageBox.Show(
                this,
                "L'élément n'est pas encore indexé. Le renommage intelligent est annulé afin d'éviter une réécriture incertaine.",
                "Renommage intelligent",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return null;
        }

        SmartLinkRenameService service =
            new SmartLinkRenameService(
                _root.FullPath);

        RenameLinkRewritePlan plan =
            await service.AnalyzeAsync(
                target.Id,
                node.DisplayName,
                newDisplayName);

        if (plan.Files.Count == 0)
        {
            return new SmartRenameWorkflow
            {
                Service =
                    service,
                Plan =
                    plan,
                ApplyRewrites =
                    false
            };
        }

        RenameLinksPreviewDialog dialog =
            new RenameLinksPreviewDialog(
                plan)
            {
                Owner =
                    this
            };

        if (dialog.ShowDialog() != true)
        {
            return null;
        }

        return new SmartRenameWorkflow
        {
            Service =
                service,
            Plan =
                plan,
            ApplyRewrites =
                dialog.ApplyRewrites,
            SelectedSourceIds =
                dialog.SelectedSourceIds
        };
    }

    /// <summary>
    /// Applies the accepted rewrite subset after the target rename.
    /// Failures are rolled back by the service while the renamed target remains usable through aliases.
    /// </summary>
    /// <param name="workflow">The accepted smart rename workflow.</param>
    /// <returns>A short status suffix for the completed rename.</returns>
    private async Task<string> CompleteSmartRenameAsync(
            SmartRenameWorkflow workflow)
    {
        if (!workflow.ApplyRewrites ||
            workflow.SelectedSourceIds.Count == 0)
        {
            await RefreshLinkIndexAsync();
            return workflow.Plan.Files.Count == 0
                ? "aucun lien textuel à réécrire"
                : "liens textuels conservés via alias";
        }

        try
        {
            int rewritten =
                await workflow.Service.ApplyAsync(
                    workflow.Plan,
                    workflow.SelectedSourceIds);

            await RefreshLinkIndexAsync();

            return
                $"{rewritten} fichier(s) de liens réécrit(s)";
        }
        catch (Exception exception) when (
            exception is IOException or
            UnauthorizedAccessException or
            InvalidDataException or
            InvalidOperationException or
            AggregateException)
        {
            await RefreshLinkIndexAsync();

            MessageBox.Show(
                this,
                "Le renommage de l'élément est conservé, mais la réécriture des liens a été annulée et rollbackée. " +
                "Les anciens liens continuent de fonctionner via les alias.\n\n" +
                exception.Message,
                "Réécriture des liens annulée",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);

            return
                "réécriture annulée · alias conservés";
        }
    }
}
