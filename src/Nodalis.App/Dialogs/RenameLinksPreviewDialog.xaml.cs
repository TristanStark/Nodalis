using System.Windows;
using Nodalis.App.Links;
using Nodalis.Core.Links;

namespace Nodalis.App.Dialogs;

/// <summary>
/// Previews textual internal-link changes and lets the user opt into selected rewrites.
/// </summary>
public partial class RenameLinksPreviewDialog : Window
{
    private readonly IReadOnlyList<RenameLinkFilePlanViewModel> _files;

    /// <summary>
    /// Initializes the smart rename preview.
    /// </summary>
    /// <param name="plan">The rewrite plan generated before the target rename.</param>
    public RenameLinksPreviewDialog(
            RenameLinkRewritePlan plan)
    {
        ArgumentNullException.ThrowIfNull(
            plan);

        InitializeComponent();

        TitleText.Text =
            $"« {plan.OldDisplayName} » → « {plan.NewDisplayName} »";

        _files =
            plan.Files
                .Select(file =>
                    new RenameLinkFilePlanViewModel
                    {
                        Plan =
                            file
                    })
                .ToArray();

        FilesList.ItemsSource =
            _files;
    }

    /// <summary>
    /// Gets whether selected textual links should be rewritten after the target rename.
    /// </summary>
    public bool ApplyRewrites { get; private set; }

    /// <summary>
    /// Gets the stable source IDs explicitly selected for rewriting.
    /// </summary>
    public IReadOnlyList<Guid> SelectedSourceIds { get; private set; } =
        [];

    /// <summary>
    /// Selects every affected source file.
    /// </summary>
    /// <param name="sender">The select-all button.</param>
    /// <param name="e">The routed event arguments.</param>
    private void SelectAll_Click(
            object sender,
            RoutedEventArgs e)
    {
        foreach (RenameLinkFilePlanViewModel file in _files)
        {
            file.IsSelected =
                true;
        }

        FilesList.Items.Refresh();
    }

    /// <summary>
    /// Deselects every affected source file.
    /// </summary>
    /// <param name="sender">The select-none button.</param>
    /// <param name="e">The routed event arguments.</param>
    private void SelectNone_Click(
            object sender,
            RoutedEventArgs e)
    {
        foreach (RenameLinkFilePlanViewModel file in _files)
        {
            file.IsSelected =
                false;
        }

        FilesList.Items.Refresh();
    }

    /// <summary>
    /// Continues the target rename without modifying any Markdown source.
    /// </summary>
    /// <param name="sender">The rename-only button.</param>
    /// <param name="e">The routed event arguments.</param>
    private void RenameWithoutRewrite_Click(
            object sender,
            RoutedEventArgs e)
    {
        ApplyRewrites =
            false;
        SelectedSourceIds =
            [];
        DialogResult =
            true;
    }

    /// <summary>
    /// Continues the target rename and applies the explicitly selected source rewrites.
    /// </summary>
    /// <param name="sender">The apply button.</param>
    /// <param name="e">The routed event arguments.</param>
    private void ApplySelected_Click(
            object sender,
            RoutedEventArgs e)
    {
        ApplyRewrites =
            true;
        SelectedSourceIds =
            _files
                .Where(file =>
                    file.IsSelected)
                .Select(file =>
                    file.Plan.SourceId)
                .ToArray();

        DialogResult =
            true;
    }
}
