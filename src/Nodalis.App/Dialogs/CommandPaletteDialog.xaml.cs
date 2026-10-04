using System.Windows;
using System.Windows.Input;
using Nodalis.App.Commands;

namespace Nodalis.App.Dialogs;

public partial class CommandPaletteDialog : Window
{
    private readonly IReadOnlyList<PaletteCommand> _commands;

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

    private void SearchTextBox_TextChanged(
        object sender,
        System.Windows.Controls.TextChangedEventArgs e) =>
        ApplyFilter();

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

    private void CommandsList_MouseDoubleClick(
        object sender,
        MouseButtonEventArgs e) =>
        AcceptSelection();

    private void ApplyFilter()
    {
        var query = SearchTextBox.Text.Trim();

        var filtered = string.IsNullOrWhiteSpace(query)
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

    private void MoveSelection(int delta)
    {
        if (CommandsList.Items.Count == 0)
        {
            return;
        }

        var current = CommandsList.SelectedIndex;
        var next = Math.Clamp(
            current + delta,
            0,
            CommandsList.Items.Count - 1);

        CommandsList.SelectedIndex = next;
        CommandsList.ScrollIntoView(
            CommandsList.SelectedItem);
    }

    private void AcceptSelection()
    {
        if (CommandsList.SelectedItem is not PaletteCommand command)
        {
            return;
        }

        SelectedCommand = command;
        DialogResult = true;
    }

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
