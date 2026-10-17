using System.Windows;

namespace Nodalis.App.Dialogs;

public partial class BookmarkDialog : Window
{
    /// <summary>
    /// Initializes the bookmark editor for one Markdown heading.
    /// </summary>
    /// <param name="headingTitle">The visible Markdown heading.</param>
    /// <param name="initialName">The current or suggested bookmark name.</param>
    /// <param name="canDelete">Whether an existing bookmark may be removed.</param>
    public BookmarkDialog(
            string headingTitle,
            string initialName,
            bool canDelete)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(headingTitle);
        ArgumentException.ThrowIfNullOrWhiteSpace(initialName);

        InitializeComponent();

        HeadingText.Text =
            headingTitle;
        NameTextBox.Text =
            initialName;
        DeleteButton.Visibility =
            canDelete
                ? Visibility.Visible
                : Visibility.Collapsed;

        Loaded += (_, _) =>
        {
            NameTextBox.Focus();
            NameTextBox.SelectAll();
        };
    }

    /// <summary>
    /// Gets the validated bookmark name accepted by the user.
    /// </summary>
    public string BookmarkName { get; private set; } =
        string.Empty;

    /// <summary>
    /// Gets whether the user requested deletion instead of saving.
    /// </summary>
    public bool DeleteRequested { get; private set; }

    /// <summary>
    /// Validates and accepts the bookmark name.
    /// </summary>
    /// <param name="sender">The save button.</param>
    /// <param name="e">The routed event arguments.</param>
    private void Save_Click(
            object sender,
            RoutedEventArgs e)
    {
        string name =
            NameTextBox.Text.Trim();

        if (string.IsNullOrWhiteSpace(name))
        {
            MessageBox.Show(
                this,
                "Donnez un nom au signet.",
                "Signet de section",
                MessageBoxButton.OK,
                MessageBoxImage.Information);

            NameTextBox.Focus();
            return;
        }

        BookmarkName =
            name;
        DeleteRequested =
            false;
        DialogResult =
            true;
    }

    /// <summary>
    /// Accepts the explicit deletion request for an existing bookmark.
    /// </summary>
    /// <param name="sender">The delete button.</param>
    /// <param name="e">The routed event arguments.</param>
    private void Delete_Click(
            object sender,
            RoutedEventArgs e)
    {
        DeleteRequested =
            true;
        DialogResult =
            true;
    }
}
