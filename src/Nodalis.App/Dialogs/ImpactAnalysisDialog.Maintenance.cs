using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Nodalis.Core.Impact;

namespace Nodalis.App.Dialogs;

/// <summary>Maintains the noncausal maintenance-reference tab of the impact explorer.</summary>
public partial class ImpactAnalysisDialog
{
    private sealed record MaintenanceRow(ImpactMaintenanceContext Context,
        string Title, string Subtitle);

    /// <summary>Filters maintenance references by the visible analyzed documents, never by guessed text matches.</summary>
    private void RefreshMaintenanceReferences()
    {
        if (_analysis is null || MaintenanceList is null)
        {
            return;
        }

        HashSet<Guid> ids = [_origin.Id];
        if (ResultsList.ItemsSource is IEnumerable<ImpactFindingRow> rows)
        {
            foreach (ImpactFindingRow row in rows)
            {
                ids.Add(row.Finding.Target.Id);
            }
        }

        MaintenanceRow[] references = ImpactMaintenanceQuery.Filter(_maintenance, ids)
            .Select(item => new MaintenanceRow(item,
                item.Record.KindLabel + " · " + item.Record.Summary,
                item.RelatedDocument.DisplayName + " · " + item.Explanation +
                    " · " + item.SourceDocument.QualifiedName +
                    ", ligne " + item.Record.LineNumber +
                    (string.IsNullOrWhiteSpace(item.Record.Resolution) ? "" :
                        " · Résolution : " + item.Record.Resolution)))
            .ToArray();
        MaintenanceList.ItemsSource = references;
        MaintenanceStatusText.Text = references.Length + " relation(s) de maintenance explicites." +
            " Double-clic ou Entrée pour ouvrir le bloc source.";
        OpenMaintenanceButton.IsEnabled = false;
    }

    /// <summary>Keeps the contextual navigation button enabled only for a real indexed match.</summary>
    /// <param name="sender">Maintenance list.</param>
    /// <param name="e">Selection change.</param>
    private void MaintenanceList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        OpenMaintenanceButton.IsEnabled = MaintenanceList.SelectedItem is MaintenanceRow;
    }

    /// <summary>Opens an explicitly referenced incident/intervention source on double click.</summary>
    /// <param name="sender">Maintenance list.</param>
    /// <param name="e">Mouse event.</param>
    private void MaintenanceList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        OpenMaintenanceButton_Click(sender, e);
    }

    /// <summary>Returns source GUID and block line for navigation after closing the dialog.</summary>
    /// <param name="sender">Open button.</param>
    /// <param name="e">Routed event.</param>
    private void OpenMaintenanceButton_Click(object sender, RoutedEventArgs e)
    {
        if (MaintenanceList.SelectedItem is not MaintenanceRow row)
        {
            return;
        }

        SelectedMaintenance = row.Context;
        DialogResult = true;
    }
}
