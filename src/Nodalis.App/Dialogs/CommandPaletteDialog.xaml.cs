using System.Windows;
using System.Windows.Input;
using Nodalis.App.Commands;

namespace Nodalis.App.Dialogs;

public partial class CommandPaletteDialog : Window
{
    private readonly IReadOnlyList<PaletteCommand> _commands;

    /// <summary>
    /// Initializes a new instance of <see cref="CommandPaletteDialog"/>.
    /// </summary>
    /// <param name="commands">The <c>commands</c> value.</param>
public CommandPaletteDialog(
        IReadOnlyList<PaletteCommand> commands)
    {
        ArgumentNullException.ThrowIfNull(commands);

        _commands = commands;
        InitializeComponent();

        Loaded += (_, _) =>
        {
            SearchTextBox.Focus();
            ApplyFilter();
        };
    }

    public PaletteCommand? SelectedCommand { get; private set; }

    /// <summary>
    /// Performs the <c>SearchTextBox_TextChanged</c> operation.
    /// </summary>
    /// <param name="sender">The <c>sender</c> value.</param>
    /// <param name="e">The <c>e</c> value.</param>
    /// <returns>The result of the operation.</returns>
private void SearchTextBox_TextChanged(
        object sender,
        System.Windows.Controls.TextChangedEventArgs e) =>
        ApplyFilter();

    /// <summary>
    /// Performs the <c>SearchTextBox_PreviewKeyDown</c> operation.
    /// </summary>
    /// <param name="sender">The <c>sender</c> value.</param>
    /// <param name="e">The <c>e</c> value.</param>
    /// <returns>The result of the operation.</returns>
private void SearchTextBox_PreviewKeyDown(
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
    /// Performs the <c>CommandsList_MouseDoubleClick</c> operation.
    /// </summary>
    /// <param name="sender">The <c>sender</c> value.</param>
    /// <param name="e">The <c>e</c> value.</param>
    /// <returns>The result of the operation.</returns>
private void CommandsList_MouseDoubleClick(
        object sender,
        MouseButtonEventArgs e) =>
        AcceptSelection();

    /// <summary>
    /// Performs the <c>ApplyFilter</c> operation.
    /// </summary>
    /// <returns>The result of the operation.</returns>
private void ApplyFilter()
    {
        string query = SearchTextBox.Text.Trim();

        global::System.Collections.Generic.IReadOnlyList<global::Nodalis.App.Commands.PaletteCommand> filtered = string.IsNullOrWhiteSpace(query)
            ? _commands
            : _commands
                .Where(command => Matches(command, query))
                .OrderBy(command => Score(command, query))
                .ThenBy(command => command.Title, StringComparer.CurrentCultureIgnoreCase)
                .ToArray();

        CommandsList.ItemsSource = filtered;
        CommandsList.SelectedIndex =
            filtered.Count > 0
                ? 0
                : -1;
    }

    /// <summary>
    /// Performs the <c>MoveSelection</c> operation.
    /// </summary>
    /// <param name="delta">The <c>delta</c> value.</param>
    /// <returns>The result of the operation.</returns>
private void MoveSelection(int delta)
    {
        if (CommandsList.Items.Count == 0)
        {
            return;
        }

        int current = CommandsList.SelectedIndex;
        int next = Math.Clamp(
            current + delta,
            0,
            CommandsList.Items.Count - 1);

        CommandsList.SelectedIndex = next;
        CommandsList.ScrollIntoView(
            CommandsList.SelectedItem);
    }

    /// <summary>
    /// Performs the <c>AcceptSelection</c> operation.
    /// </summary>
    /// <returns>The result of the operation.</returns>
private void AcceptSelection()
    {
        if (CommandsList.SelectedItem is not PaletteCommand command)
        {
            return;
        }

        SelectedCommand = command;
        DialogResult = true;
    }

    /// <summary>
    /// Performs the <c>Matches</c> operation.
    /// </summary>
    /// <param name="command">The <c>command</c> value.</param>
    /// <param name="query">The <c>query</c> value.</param>
    /// <returns>The result of the operation.</returns>
private static bool Matches(
        PaletteCommand command,
        string query)
    {
        if (command.Title.Contains(
                query,
                StringComparison.CurrentCultureIgnoreCase))
        {
            return true;
        }

        if (command.Subtitle?.Contains(
                query,
                StringComparison.CurrentCultureIgnoreCase) == true)
        {
            return true;
        }

        return command.Keywords.Any(keyword =>
            keyword.Contains(
                query,
                StringComparison.CurrentCultureIgnoreCase));
    }

    /// <summary>
    /// Performs the <c>Score</c> operation.
    /// </summary>
    /// <param name="command">The <c>command</c> value.</param>
    /// <param name="query">The <c>query</c> value.</param>
    /// <returns>The result of the operation.</returns>
private static int Score(
        PaletteCommand command,
        string query)
    {
        if (command.Title.StartsWith(
                query,
                StringComparison.CurrentCultureIgnoreCase))
        {
            return 0;
        }

        if (command.Title.Contains(
                query,
                StringComparison.CurrentCultureIgnoreCase))
        {
            return 1;
        }

        return 2;
    }
}
