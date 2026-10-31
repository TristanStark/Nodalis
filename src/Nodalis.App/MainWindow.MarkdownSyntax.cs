using System.Windows;
using Nodalis.App.Editor;

namespace Nodalis.App;

public partial class MainWindow
{
    private MarkdownSyntaxOverlay? _primarySyntaxOverlay;
    private MarkdownSyntaxOverlay? _secondarySyntaxOverlay;

    /// <summary>
    /// Starts optional source syntax coloring in the two existing native TextBoxes.
    /// </summary>
    /// <param name="enabled">The user's persisted coloring preference.</param>
    private void InitializeMarkdownSyntaxOverlays(bool enabled)
    {
        _primarySyntaxOverlay = new MarkdownSyntaxOverlay(
            MarkdownEditorTextBox,
            PrimarySyntaxOverlay);
        _secondarySyntaxOverlay = new MarkdownSyntaxOverlay(
            SecondaryMarkdownEditorTextBox,
            SecondarySyntaxOverlay);
        SyntaxHighlightingToggle.IsChecked = enabled;
        _primarySyntaxOverlay.SetEnabled(enabled);
        _secondarySyntaxOverlay.SetEnabled(enabled);
    }

    /// <summary>
    /// Handles the source-coloring toggle without changing document contents.
    /// </summary>
    /// <param name="sender">The syntax-coloring checkbox.</param>
    /// <param name="e">The routed toggle event.</param>
    private void SyntaxHighlightingToggle_Changed(
        object sender,
        RoutedEventArgs e)
    {
        bool enabled = SyntaxHighlightingToggle.IsChecked == true;
        _primarySyntaxOverlay?.SetEnabled(enabled);
        _secondarySyntaxOverlay?.SetEnabled(enabled);
    }

    /// <summary>
    /// Releases both syntax mirrors and restores the native TextBox foregrounds.
    /// </summary>
    private void DisposeMarkdownSyntaxOverlays()
    {
        _primarySyntaxOverlay?.Dispose();
        _secondarySyntaxOverlay?.Dispose();
        _primarySyntaxOverlay = null;
        _secondarySyntaxOverlay = null;
    }
}
