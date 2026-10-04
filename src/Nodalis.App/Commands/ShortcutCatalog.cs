using System.Windows.Input;

namespace Nodalis.App.Commands;

public sealed record AppShortcut(
    string Id,
    string Display,
    ModifierKeys Modifiers,
    Key Key);

public static class ShortcutCatalog
{
    public static readonly AppShortcut NewNote =
        new("note.new", "Ctrl+N", ModifierKeys.Control, Key.N);

    public static readonly AppShortcut NewProject =
        new(
            "project.new",
            "Ctrl+Shift+N",
            ModifierKeys.Control | ModifierKeys.Shift,
            Key.N);

    public static readonly AppShortcut Search =
        new("search", "Ctrl+F", ModifierKeys.Control, Key.F);

    public static readonly AppShortcut InternalLink =
        new("link.insert", "Ctrl+K", ModifierKeys.Control, Key.K);

    public static readonly AppShortcut CommandPalette =
        new("palette.open", "Ctrl+P", ModifierKeys.Control, Key.P);

    public static readonly AppShortcut QuickNote =
        new(
            "quick-note.capture",
            "Ctrl+Alt+N",
            ModifierKeys.Control | ModifierKeys.Alt,
            Key.N);

    public static readonly AppShortcut QuickNotesOverview =
        new(
            "quick-note.overview",
            "Ctrl+Shift+Q",
            ModifierKeys.Control | ModifierKeys.Shift,
            Key.Q);

    public static readonly AppShortcut Save =
        new("document.save", "Ctrl+S", ModifierKeys.Control, Key.S);

    public static readonly AppShortcut Bold =
        new("format.bold", "Ctrl+B", ModifierKeys.Control, Key.B);

    public static readonly AppShortcut Italic =
        new("format.italic", "Ctrl+I", ModifierKeys.Control, Key.I);

    public static readonly AppShortcut InlineCode =
        new("format.code", "Ctrl+`", ModifierKeys.Control, Key.Oem3);

    public static IReadOnlyList<AppShortcut> All { get; } =
    [
        NewNote,
        NewProject,
        Search,
        InternalLink,
        CommandPalette,
        QuickNote,
        QuickNotesOverview,
        Save,
        Bold,
        Italic,
        InlineCode
    ];

    /// <summary>
    /// Performs the <c>Matches</c> operation.
    /// </summary>
    /// <param name="e">The <c>e</c> value.</param>
    /// <param name="shortcut">The <c>shortcut</c> value.</param>
    /// <returns>The result of the operation.</returns>
public static bool Matches(
        KeyEventArgs e,
        AppShortcut shortcut) =>
        Keyboard.Modifiers == shortcut.Modifiers &&
        e.Key == shortcut.Key;
}
