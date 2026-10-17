using System.Windows;
using Nodalis.Core.Links;
using Nodalis.Core.Relations;

namespace Nodalis.App.Dialogs;

public partial class RelationDialog : Window
{
    /// <summary>
    /// Initializes the typed-relation editor.
    /// </summary>
    /// <param name="targets">The available stable workspace targets.</param>
    public RelationDialog(
            IReadOnlyCollection<LinkTargetEntry> targets)
    {
        ArgumentNullException.ThrowIfNull(targets);

        InitializeComponent();

        RelationTypeComboBox.ItemsSource =
            RelationTypeCatalog.KnownTypes;
        RelationTypeComboBox.SelectedIndex =
            0;

        LinkTargetEntry[] orderedTargets =
            targets
                .OrderBy(
                    target => target.QualifiedName,
                    StringComparer.CurrentCultureIgnoreCase)
                .ToArray();

        TargetComboBox.ItemsSource =
            orderedTargets;

        if (orderedTargets.Length > 0)
        {
            TargetComboBox.SelectedIndex =
                0;
        }

        Loaded += (_, _) =>
        {
            RelationTypeComboBox.Focus();
        };
    }

    /// <summary>
    /// Gets the relation type accepted by the user.
    /// </summary>
    public string RelationType { get; private set; } =
        string.Empty;

    /// <summary>
    /// Gets the selected stable target.
    /// </summary>
    public LinkTargetEntry? SelectedTarget { get; private set; }

    /// <summary>
    /// Validates and accepts the relation.
    /// </summary>
    /// <param name="sender">The add button.</param>
    /// <param name="e">The routed event arguments.</param>
    private void Save_Click(
            object sender,
            RoutedEventArgs e)
    {
        string relationType =
            RelationTypeComboBox.Text.Trim();

        if (string.IsNullOrWhiteSpace(
                relationType))
        {
            MessageBox.Show(
                this,
                "Indiquez un type de relation.",
                "Ajouter une relation",
                MessageBoxButton.OK,
                MessageBoxImage.Information);

            RelationTypeComboBox.Focus();
            return;
        }

        if (TargetComboBox.SelectedItem is not
            LinkTargetEntry target)
        {
            MessageBox.Show(
                this,
                "Sélectionnez une cible.",
                "Ajouter une relation",
                MessageBoxButton.OK,
                MessageBoxImage.Information);

            TargetComboBox.Focus();
            return;
        }

        RelationType =
            relationType;
        SelectedTarget =
            target;
        DialogResult =
            true;
    }
}
