using System.Windows;
using System.Windows.Input;
using Nodalis.Core.Maintenance;

namespace Nodalis.App.Dialogs;

/// <summary>Read-only browser for explicitly declared document links of an incident or intervention.</summary>
public partial class MaintenanceReferencesDialog : Window
{
    /// <summary>Builds the references viewer without inferring undeclared dependencies.</summary>
    /// <param name="references">Resolved Markdown references from the identity registry.</param>
    /// <param name="description">Origin incident or intervention.</param>
    public MaintenanceReferencesDialog(
        IReadOnlyList<MaintenanceDocumentReference> references, string description)
    {
        ArgumentNullException.ThrowIfNull(references);
        InitializeComponent();
        ReferencesList.ItemsSource = references;
        int broken = references.Count(item => item.IsBroken);
        SummaryText.Text = description + " · " + references.Count +
            " référence(s), " + broken + " cassée(s). " +
            "Les références proviennent uniquement du Markdown enregistré.";
        if (references.Count > 0)
        {
            ReferencesList.SelectedIndex = 0;
        }

        Loaded += (_, _) => ReferencesList.Focus();
    }

    /// <summary>Selected live reference after explicit navigation.</summary>
    public MaintenanceDocumentReference? SelectedReference { get; private set; }

    /// <summary>Confirms opening the selected registered target.</summary>
    /// <param name="sender">Button.</param>
    /// <param name="e">Click.</param>
    private void Open_Click(object sender, RoutedEventArgs e)
    {
        Accept();
    }

    /// <summary>Opens a live target by double-clicking a reference row.</summary>
    /// <param name="sender">Reference list.</param>
    /// <param name="e">Mouse action.</param>
    private void References_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        Accept();
    }

    /// <summary>Rejects broken references instead of guessing by filename.</summary>
    private void Accept()
    {
        if (ReferencesList.SelectedItem is not MaintenanceDocumentReference entry)
        {
            return;
        }

        if (entry.IsBroken || entry.TargetId is null)
        {
            MessageBox.Show(this,
                "Cette référence est cassée ou sa cible est absente du registre. " +
                "Restaurez le document ou vérifiez son identifiant ; Nodalis ne redirige " +
                "jamais automatiquement vers un homonyme.",
                "Référence indisponible", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        SelectedReference = entry;
        DialogResult = true;
    }
}
