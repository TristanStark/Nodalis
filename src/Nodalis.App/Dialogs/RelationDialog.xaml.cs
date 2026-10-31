using System.Windows;
using System.Windows.Controls;
using Nodalis.Core.Links;
using Nodalis.Core.Relations;

namespace Nodalis.App.Dialogs;

public partial class RelationDialog : Window
{
    private readonly LinkTargetEntry[] _documents;
    private readonly RelationTargetOption[] _options;

    /// <summary>Maintains the existing document-only relation picker.</summary>
    /// <param name="targets">Indexed workspace elements.</param>
    /// <param name="preferredType">Suggested editable relation type.</param>
    public RelationDialog(
        IReadOnlyCollection<LinkTargetEntry> targets,
        string? preferredType = null)
        : this(targets, targets.Select(target => new RelationTargetOption
        {
            Id = target.Id,
            DisplayName = target.DisplayName,
            QualifiedName = target.QualifiedName,
            IsBlock = false
        }).ToArray(), preferredType)
    {
    }

    /// <summary>Allows explicit selection of indexed documents and safe blocks.</summary>
    /// <param name="targets">Existing indexed workspace targets.</param>
    /// <param name="choices">Prevalidated targets from the shared index.</param>
    /// <param name="preferredType">Suggested editable relation type.</param>
    public RelationDialog(
        IReadOnlyCollection<LinkTargetEntry> targets,
        IReadOnlyCollection<RelationTargetOption> choices,
        string? preferredType = null)
    {
        ArgumentNullException.ThrowIfNull(targets);
        ArgumentNullException.ThrowIfNull(choices);
        _documents = targets.ToArray();
        _options = choices.OrderBy(item => item.QualifiedName,
            StringComparer.CurrentCultureIgnoreCase).ToArray();

        InitializeComponent();
        RelationTypeComboBox.ItemsSource = RelationTypeCatalog.KnownTypes;
        RelationTypeComboBox.SelectedItem =
            RelationTypeCatalog.KnownTypes.FirstOrDefault(value =>
                string.Equals(value, preferredType, StringComparison.OrdinalIgnoreCase)) ??
            RelationTypeCatalog.KnownTypes[0];
        UpdateTargets();
        Loaded += (_, _) => RelationTypeComboBox.Focus();
    }

    /// <summary>Accepted type, including user-defined relation types.</summary>
    public string RelationType { get; private set; } = string.Empty;

    /// <summary>Legacy selected entity target; null for block destinations.</summary>
    public LinkTargetEntry? SelectedTarget { get; private set; }

    /// <summary>Explicitly accepted entity or Markdown block.</summary>
    public RelationTargetOption? SelectedTargetOption { get; private set; }

    /// <summary>Filters in memory and bounds the ComboBox to 100 results.</summary>
    private void UpdateTargets()
    {
        if (TargetComboBox is null || TargetSearchTextBox is null)
        {
            return;
        }

        string query = TargetSearchTextBox.Text.Trim();
        RelationTargetOption[] matches = _options
            .Where(item => query.Length == 0 ||
                item.QualifiedName.Contains(query, StringComparison.CurrentCultureIgnoreCase) ||
                item.Id.ToString("D").Contains(query, StringComparison.OrdinalIgnoreCase))
            .Take(100).ToArray();

        TargetComboBox.ItemsSource = matches;
        TargetComboBox.SelectedIndex = matches.Length == 0 ? -1 : 0;
        TargetCountText.Text = matches.Length == 100
            ? "100 premiers résultats : précisez la recherche."
            : $"{matches.Length} cible(s) trouvée(s).";
    }

    /// <summary>Updates candidate selection while searching a heading or GUID.</summary>
    /// <param name="sender">Search input.</param>
    /// <param name="e">WPF text change.</param>
    private void TargetSearchTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        UpdateTargets();
    }

    /// <summary>Shows the selected target kind and persistent GUID.</summary>
    /// <param name="sender">Target selection.</param>
    /// <param name="e">WPF selection change.</param>
    private void TargetComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (TargetDetailsText is null)
        {
            return;
        }

        TargetDetailsText.Text = TargetComboBox.SelectedItem is RelationTargetOption item
            ? $"{(item.IsBlock ? "Bloc Markdown" : "Élément du workspace")} · ID {item.Id:D}"
            : "Sélectionnez une cible.";
    }

    /// <summary>Confirms the relation type and the explicit target.</summary>
    /// <param name="sender">Validation button.</param>
    /// <param name="e">WPF click.</param>
    private void Save_Click(object sender, RoutedEventArgs e)
    {
        string relationType = RelationTypeComboBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(relationType))
        {
            MessageBox.Show(this, "Indiquez un type de relation.", "Ajouter une relation",
                MessageBoxButton.OK, MessageBoxImage.Information);
            RelationTypeComboBox.Focus();
            return;
        }

        if (TargetComboBox.SelectedItem is not RelationTargetOption selected)
        {
            MessageBox.Show(this, "Sélectionnez une cible.", "Ajouter une relation",
                MessageBoxButton.OK, MessageBoxImage.Information);
            TargetSearchTextBox.Focus();
            return;
        }

        RelationType = relationType;
        SelectedTargetOption = selected;
        SelectedTarget = selected.IsBlock ? null :
            _documents.FirstOrDefault(target => target.Id == selected.Id);
        DialogResult = true;
    }
}
