using System.Windows;
using System.Windows.Controls;
using Nodalis.Core.AI;

namespace Nodalis.App.Dialogs;

/// <summary>
/// Lets the user inspect and explicitly choose every source sent to a local AI request.
/// </summary>
public partial class AiContextSelectionDialog : Window
{
    private readonly List<AiContextSelectionItemViewModel> _items;

    /// <summary>
    /// Initializes one context-selection dialog.
    /// </summary>
    /// <param name="candidates">The readable source candidates.</param>
    public AiContextSelectionDialog(
            IReadOnlyList<LocalAiContextItem> candidates)
    {
        ArgumentNullException.ThrowIfNull(
            candidates);

        InitializeComponent();

        _items =
            candidates
                .Select(
                    (item, index) =>
                        new AiContextSelectionItemViewModel(
                            item,
                            selected:
                                index ==
                                0))
                .ToList();

        SourcesListBox.ItemsSource =
            _items;

        if (_items.Count >
            0)
        {
            SourcesListBox.SelectedIndex =
                0;
        }
    }

    /// <summary>Gets the exact context items explicitly selected by the user.</summary>
    public IReadOnlyList<LocalAiContextItem> SelectedContext { get; private set; } = [];

    /// <summary>
    /// Updates the exact-content preview when the highlighted source changes.
    /// </summary>
    /// <param name="sender">The source list.</param>
    /// <param name="e">The selection event arguments.</param>
    private void SourcesListBox_SelectionChanged(
            object sender,
            SelectionChangedEventArgs e)
    {
        if (SourcesListBox.SelectedItem is not
            AiContextSelectionItemViewModel item)
        {
            PreviewLabelText.Text =
                "Aperçu";
            PreviewTextBox.Text =
                string.Empty;
            return;
        }

        PreviewLabelText.Text =
            item.Label;
        PreviewTextBox.Text =
            item.Content;
    }

    /// <summary>
    /// Validates that at least one visible source is selected and returns the exact selected items.
    /// </summary>
    /// <param name="sender">The continue button.</param>
    /// <param name="e">The routed event arguments.</param>
    private void Continue_Click(
            object sender,
            RoutedEventArgs e)
    {
        LocalAiContextItem[] selected =
            _items
                .Where(item =>
                    item.IsSelected)
                .Select(item =>
                    item.Item)
                .ToArray();

        if (selected.Length ==
            0)
        {
            MessageBox.Show(
                this,
                "Sélectionnez au moins un document à transmettre au moteur local.",
                "Sélection du contexte IA",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        SelectedContext =
            selected;
        DialogResult =
            true;
    }
}
