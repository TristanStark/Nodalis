using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Nodalis.Core.Tasks;
using Nodalis.Infrastructure.Projects;

namespace Nodalis.App.Dialogs;

/// <summary>
/// Read-only task-to-risk association browser. Navigation always flows through
/// the normal, source-validated Markdown editor in the owning MainWindow.
/// </summary>
public partial class ProjectRiskTaskBacklinksDialog : Window
{
    /// <summary>Builds a view from a frozen verified catalog, without I/O.</summary>
    /// <param name="task">Selected, persistently identified task.</param>
    /// <param name="catalog">Explicitly refreshed risk backlink projection.</param>
    public ProjectRiskTaskBacklinksDialog(
        TaskItem task, ProjectRiskTaskBacklinkCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(task);
        ArgumentNullException.ThrowIfNull(catalog);
        InitializeComponent();
        HeadingText.Text = "Risques liés · " + task.Text;
        SummaryText.Text = "ID de tâche : " +
            (catalog.TaskId?.ToString("D") ?? "non vérifié") +
            Environment.NewLine + task.SourceRelativePath +
            Environment.NewLine +
            "Les liens des anciens plans et les relations déliées sont " +
            "conservés à titre historique, jamais réactivés automatiquement.";
        BacklinksList.ItemsSource = catalog.Links;
        BacklinksList.SelectedIndex =
            catalog.Links.Count == 0 ? -1 : 0;
        DiagnosticsText.Text = catalog.Diagnostics.Count == 0
            ? "Aucun diagnostic de source."
            : "Sources à vérifier : " +
              string.Join(Environment.NewLine, catalog.Diagnostics.Take(5));
        StatusText.Text = catalog.Links.Count + " relation(s) trouvée(s).";
        OpenRiskButton.IsEnabled = catalog.TaskId is not null &&
            BacklinksList.SelectedItem is ProjectRiskTaskBacklink;
    }

    /// <summary>Selected verified RIS reference for the canonical editor.</summary>
    public string? SelectedRiskPath { get; private set; }

    /// <summary>Enables safe navigation only on an available catalog entry.</summary>
    /// <param name="sender">Selection source.</param>
    /// <param name="e">WPF event.</param>
    private void Backlinks_SelectionChanged(
        object sender, SelectionChangedEventArgs e)
    {
        if (OpenRiskButton is not null)
        {
            OpenRiskButton.IsEnabled =
                BacklinksList.SelectedItem is ProjectRiskTaskBacklink;
        }
    }

    /// <summary>Returns the selected stored RIS path to the parent dialog.</summary>
    /// <param name="sender">Open command.</param>
    /// <param name="e">WPF event.</param>
    private void OpenRisk_Click(object sender, RoutedEventArgs e)
    {
        if (BacklinksList.SelectedItem is ProjectRiskTaskBacklink backlink)
        {
            SelectedRiskPath = backlink.RiskRelativePath;
            DialogResult = true;
        }
    }

    /// <summary>Supports double-click navigation using the same source path.</summary>
    /// <param name="sender">Backlink list.</param>
    /// <param name="e">Mouse click.</param>
    private void Backlinks_MouseDoubleClick(
        object sender, MouseButtonEventArgs e)
    {
        OpenRisk_Click(sender, e);
    }

    /// <summary>Dismisses the read-only lookup without navigation.</summary>
    /// <param name="sender">Cancel button.</param>
    /// <param name="e">WPF event.</param>
    private void Close_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}
