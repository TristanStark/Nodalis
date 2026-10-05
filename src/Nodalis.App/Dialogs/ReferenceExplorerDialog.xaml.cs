using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Nodalis.Core.Links;

namespace Nodalis.App.Dialogs;

/// <summary>
/// Explores incoming links and typed relations for one indexed workspace element.
/// </summary>
public partial class ReferenceExplorerDialog : Window
{
    private const string AllTypes = "Tous les types";
    private const string AllScopes = "Tous les scopes";
    private const string SameScope = "Même scope";
    private const string OtherScopes = "Autres scopes";

    private readonly IReadOnlyList<ReferenceSearchEntry> _entries;

    /// <summary>
    /// Initializes the reference explorer.
    /// </summary>
    /// <param name="target">The target whose incoming references are displayed.</param>
    /// <param name="entries">The incoming references from the current derived index.</param>
    public ReferenceExplorerDialog(
            LinkTargetEntry target,
            IReadOnlyList<ReferenceSearchEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(
            target);
        ArgumentNullException.ThrowIfNull(
            entries);

        _entries =
            entries;

        InitializeComponent();

        TargetText.Text =
            target.QualifiedName;

        global::System.Collections.Generic.List<string> types =
            new List<string>
            {
                AllTypes
            };

        types.AddRange(
            entries
                .Select(entry =>
                    entry.TypeLabel)
                .Distinct(
                    StringComparer.CurrentCultureIgnoreCase)
                .OrderBy(
                    value => value,
                    StringComparer.CurrentCultureIgnoreCase));

        TypeFilterComboBox.ItemsSource =
            types;
        TypeFilterComboBox.SelectedIndex =
            0;

        ScopeFilterComboBox.ItemsSource =
            new[]
            {
                AllScopes,
                SameScope,
                OtherScopes
            };
        ScopeFilterComboBox.SelectedIndex =
            0;

        Loaded += (_, _) =>
        {
            RefreshResults();
            SourceFilterTextBox.Focus();
        };
    }

    /// <summary>
    /// Gets the source reference accepted by the user.
    /// </summary>
    public ReferenceSearchEntry? SelectedEntry { get; private set; }

    /// <summary>
    /// Refreshes the result list whenever a filter changes.
    /// </summary>
    /// <param name="sender">The filter control.</param>
    /// <param name="e">The routed event arguments.</param>
    private void Filters_Changed(
            object sender,
            RoutedEventArgs e)
    {
        if (IsLoaded)
        {
            RefreshResults();
        }
    }

    /// <summary>
    /// Applies source, reference-type and scope filters to the immutable result snapshot.
    /// </summary>
    private void RefreshResults()
    {
        string sourceFilter =
            SourceFilterTextBox.Text.Trim();
        string typeFilter =
            TypeFilterComboBox.SelectedItem as string ??
            AllTypes;
        string scopeFilter =
            ScopeFilterComboBox.SelectedItem as string ??
            AllScopes;

        ReferenceSearchEntry[] filtered =
            _entries
                .Where(entry =>
                    string.IsNullOrWhiteSpace(
                        sourceFilter) ||
                    entry.Source.DisplayName.Contains(
                        sourceFilter,
                        StringComparison.CurrentCultureIgnoreCase) ||
                    entry.Source.QualifiedName.Contains(
                        sourceFilter,
                        StringComparison.CurrentCultureIgnoreCase) ||
                    entry.Excerpt.Contains(
                        sourceFilter,
                        StringComparison.CurrentCultureIgnoreCase))
                .Where(entry =>
                    string.Equals(
                        typeFilter,
                        AllTypes,
                        StringComparison.Ordinal) ||
                    string.Equals(
                        entry.TypeLabel,
                        typeFilter,
                        StringComparison.CurrentCultureIgnoreCase))
                .Where(entry =>
                    string.Equals(
                        scopeFilter,
                        AllScopes,
                        StringComparison.Ordinal) ||
                    string.Equals(
                        scopeFilter,
                        SameScope,
                        StringComparison.Ordinal) &&
                    entry.IsSameScope ||
                    string.Equals(
                        scopeFilter,
                        OtherScopes,
                        StringComparison.Ordinal) &&
                    !entry.IsSameScope)
                .ToArray();

        ResultsList.ItemsSource =
            filtered;
        StatusText.Text =
            $"{filtered.Length} référence(s) · " +
            "double-clic ou Entrée pour ouvrir la source à l'occurrence";
    }

    /// <summary>
    /// Accepts a reference on double-click.
    /// </summary>
    /// <param name="sender">The result list.</param>
    /// <param name="e">The mouse event arguments.</param>
    private void ResultsList_MouseDoubleClick(
            object sender,
            MouseButtonEventArgs e) =>
        AcceptSelectedEntry();

    /// <summary>
    /// Handles keyboard navigation for the explorer.
    /// </summary>
    /// <param name="sender">The window.</param>
    /// <param name="e">The key event arguments.</param>
    private void Window_PreviewKeyDown(
            object sender,
            KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            e.Handled =
                true;
            DialogResult =
                false;
            return;
        }

        if (e.Key == Key.Enter &&
            ResultsList.IsKeyboardFocusWithin)
        {
            e.Handled =
                true;
            AcceptSelectedEntry();
        }
    }

    /// <summary>
    /// Accepts the currently selected source reference.
    /// </summary>
    private void AcceptSelectedEntry()
    {
        if (ResultsList.SelectedItem is not
            ReferenceSearchEntry selected)
        {
            return;
        }

        SelectedEntry =
            selected;
        DialogResult =
            true;
    }
}
