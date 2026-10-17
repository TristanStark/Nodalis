using System.Windows;
using System.Windows.Input;
using Nodalis.Core.Maintenance;

namespace Nodalis.App.Dialogs;

/// <summary>Allows keyboard-first selection of an existing intervention to edit.</summary>
public partial class MaintenanceInterventionPickerDialog : Window
{
    /// <summary>Initializes the selection list with recorded interventions in date order.</summary>
    /// <param name="entries">Valid intervention records.</param>
    public MaintenanceInterventionPickerDialog(IReadOnlyList<MaintenanceRecord> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        InitializeComponent();
        InterventionsList.ItemsSource = entries
            .Where(item => item.Kind == MaintenanceRecordKind.Intervention)
            .OrderByDescending(item => item.Date)
            .ToArray();
        if (InterventionsList.Items.Count > 0)
        {
            InterventionsList.SelectedIndex = 0;
        }

        Loaded += (_, _) => InterventionsList.Focus();
    }

    /// <summary>Gets the intervention selected by the user.</summary>
    public MaintenanceRecord? SelectedIntervention { get; private set; }

    /// <summary>Accepts the selected intervention on button press.</summary>
    /// <param name="sender">Button.</param>
    /// <param name="e">Click event.</param>
    private void Select_Click(object sender, RoutedEventArgs e)
    {
        SelectCurrent();
    }

    /// <summary>Accepts selection by double click, supporting mouse and keyboard workflows.</summary>
    /// <param name="sender">List control.</param>
    /// <param name="e">Mouse event.</param>
    private void Select_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        SelectCurrent();
    }

    /// <summary>Returns the selected record without modifying any source document.</summary>
    private void SelectCurrent()
    {
        if (InterventionsList.SelectedItem is not MaintenanceRecord entry)
        {
            return;
        }

        SelectedIntervention = entry;
        DialogResult = true;
    }
}
