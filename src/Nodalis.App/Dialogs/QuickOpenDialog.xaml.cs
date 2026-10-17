using System.Windows;
using System.Windows.Input;
using Nodalis.App.Navigation;

namespace Nodalis.App.Dialogs;

public partial class QuickOpenDialog : Window
{
    private readonly IReadOnlyList<QuickOpenEntry> _entries;

    /// <summary>
    /// Initializes a new Quick Open dialog over an already-built in-memory index.
    /// </summary>
    /// <param name="entries">The targets available for navigation.</param>
    public QuickOpenDialog(
            IReadOnlyList<QuickOpenEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);

        _entries = entries;
        InitializeComponent();

        Loaded += (_, _) =>
        {
            QueryTextBox.Focus();
            ApplyFilter();
        };
    }

    /// <summary>
    /// Gets the entry accepted by the user.
    /// </summary>
    public QuickOpenEntry? SelectedEntry { get; private set; }

    /// <summary>
    /// Refreshes results immediately as the query changes.
    /// </summary>
    /// <param name="sender">The query editor.</param>
    /// <param name="e">The text change arguments.</param>
    private void QueryTextBox_TextChanged(
            object sender,
            System.Windows.Controls.TextChangedEventArgs e) =>
            ApplyFilter();

    /// <summary>
    /// Provides complete keyboard navigation while focus remains in the query editor.
    /// </summary>
    /// <param name="sender">The query editor.</param>
    /// <param name="e">The key event arguments.</param>
    private void QueryTextBox_PreviewKeyDown(
            object sender,
            KeyEventArgs e)
    {
        if (e.Key == Key.Down)
        {
            e.Handled = true;
            MoveSelection(1);
            return;
        }

        if (e.Key == Key.Up)
        {
            e.Handled = true;
            MoveSelection(-1);
            return;
        }

        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            AcceptSelection();
            return;
        }

        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            DialogResult = false;
        }
    }

    /// <summary>
    /// Opens the double-clicked Quick Open result.
    /// </summary>
    /// <param name="sender">The results list.</param>
    /// <param name="e">The mouse event arguments.</param>
    private void ResultsList_MouseDoubleClick(
            object sender,
            MouseButtonEventArgs e) =>
            AcceptSelection();

    /// <summary>
    /// Applies the in-memory filter and deterministic ranking.
    /// </summary>
    private void ApplyFilter()
    {
        string query =
            QueryTextBox.Text.Trim();

        global::Nodalis.App.Navigation.QuickOpenEntry[] filtered =
            string.IsNullOrWhiteSpace(query)
                ? _entries
                    .OrderBy(entry =>
                        entry.IsFavorite
                            ? 0
                            : 1)
                    .ThenBy(entry =>
                        entry.RecentRank < 0
                            ? int.MaxValue
                            : entry.RecentRank)
                    .ThenBy(
                        entry => entry.DisplayName,
                        StringComparer.CurrentCultureIgnoreCase)
                    .Take(100)
                    .ToArray()
                : _entries
                    .Where(entry =>
                        Matches(
                            entry,
                            query))
                    .OrderBy(entry =>
                        Score(
                            entry,
                            query))
                    .ThenBy(
                        entry => entry.DisplayName,
                        StringComparer.CurrentCultureIgnoreCase)
                    .Take(100)
                    .ToArray();

        ResultsList.ItemsSource =
            filtered;

        ResultsList.SelectedIndex =
            filtered.Length > 0
                ? 0
                : -1;

        StatusText.Text =
            filtered.Length == 0
                ? "Aucun résultat · Échap fermer"
                : $"{filtered.Length} résultat(s) · ↑ ↓ naviguer · Entrée ouvrir · Échap fermer";
    }

    /// <summary>
    /// Moves the current list selection while keeping it visible.
    /// </summary>
    /// <param name="delta">Positive for down, negative for up.</param>
    private void MoveSelection(
            int delta)
    {
        if (ResultsList.Items.Count == 0)
        {
            return;
        }

        int current =
            ResultsList.SelectedIndex;

        int next = Math.Clamp(
            current + delta,
            0,
            ResultsList.Items.Count - 1);

        ResultsList.SelectedIndex =
            next;

        ResultsList.ScrollIntoView(
            ResultsList.SelectedItem);
    }

    /// <summary>
    /// Accepts the currently selected entry.
    /// </summary>
    private void AcceptSelection()
    {
        if (ResultsList.SelectedItem is not
            QuickOpenEntry entry)
        {
            return;
        }

        SelectedEntry = entry;
        DialogResult = true;
    }

    /// <summary>
    /// Determines whether an entry matches all query tokens.
    /// </summary>
    /// <param name="entry">The candidate target.</param>
    /// <param name="query">The user query.</param>
    /// <returns>True when every token is present in the name, kind or path.</returns>
    private static bool Matches(
            QuickOpenEntry entry,
            string query)
    {
        string searchable =
            $"{entry.DisplayName} {entry.KindLabel} {entry.QualifiedPath}";

        string[] tokens = query.Split(
            ' ',
            StringSplitOptions.RemoveEmptyEntries |
            StringSplitOptions.TrimEntries);

        return tokens.All(token =>
            searchable.Contains(
                token,
                StringComparison.CurrentCultureIgnoreCase));
    }

    /// <summary>
    /// Scores one matching entry, favoring exact/prefix names, favorites and recency.
    /// </summary>
    /// <param name="entry">The candidate target.</param>
    /// <param name="query">The user query.</param>
    /// <returns>A lower score for a better result.</returns>
    private static int Score(
            QuickOpenEntry entry,
            string query)
    {
        int score;

        if (entry.DisplayName.Equals(
                query,
                StringComparison.CurrentCultureIgnoreCase))
        {
            score = 0;
        }
        else if (entry.DisplayName.StartsWith(
                     query,
                     StringComparison.CurrentCultureIgnoreCase))
        {
            score = 20;
        }
        else if (entry.DisplayName.Contains(
                     query,
                     StringComparison.CurrentCultureIgnoreCase))
        {
            score = 60;
        }
        else if (entry.QualifiedPath.StartsWith(
                     query,
                     StringComparison.CurrentCultureIgnoreCase))
        {
            score = 90;
        }
        else
        {
            score = 120;
        }

        if (entry.IsFavorite)
        {
            score -= 15;
        }

        if (entry.RecentRank >= 0)
        {
            score -= Math.Max(
                1,
                12 - Math.Min(
                    11,
                    entry.RecentRank));
        }

        return score;
    }
}
