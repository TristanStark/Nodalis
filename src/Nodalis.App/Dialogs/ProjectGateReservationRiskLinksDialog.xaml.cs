using System.Windows;
using System.Windows.Controls;
using Nodalis.Core.Projects;
using Nodalis.Infrastructure.Projects;

namespace Nodalis.App.Dialogs;

/// <summary>
/// Read-only-first risk/reservation relation picker. No source is written or
/// migrated until the parent explicitly approves a SHA-guarded Gate save.
/// </summary>
public partial class ProjectGateReservationRiskLinksDialog : Window
{
    private readonly bool _readOnly;

    /// <summary>Risk GUID with source label, or unresolved legacy link.</summary>
    /// <param name="Id">Canonical stable risk GUID.</param>
    /// <param name="Label">Display text or missing-source warning.</param>
    private sealed record RiskLinkRow(
        Guid Id, string Label, string? RelativePath);

    /// <summary>Builds candidate and linked lists from one frozen snapshot.</summary>
    /// <param name="description">Original reservation description.</param>
    /// <param name="catalog">Validated same-project risk candidates.</param>
    /// <param name="linked">Current risk GUIDs, even if some became orphaned.</param>
    /// <param name="readOnly">Archive/read-only consultation.</param>
    public ProjectGateReservationRiskLinksDialog(
        string description, ProjectRiskCatalog catalog,
        IReadOnlyList<Guid> linked, bool readOnly)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(linked);
        _readOnly = readOnly;
        InitializeComponent();
        DescriptionText.Text = "Réserve : " + description;
        DiagnosticsText.Text = catalog.Risks.Count +
            " fiche(s) RIS canonique(s) du projet · " +
            catalog.Diagnostics.Count + " source(s) non structurée(s)." +
            (readOnly ? " Lecture seule." :
                " Les liens à des sources absentes restent dissociables.");
        HashSet<Guid> linkedIds = linked.ToHashSet();
        AvailableList.ItemsSource = catalog.Risks.Where(item =>
            !linkedIds.Contains(item.Card.Id)).ToArray();
        LinkedList.ItemsSource = linked.Select(id =>
        {
            ProjectRiskEntry? risk = catalog.Risks.FirstOrDefault(item =>
                item.Card.Id == id);
            return new RiskLinkRow(id, risk is null
                ? id.ToString("D") + " · RIS absent ou ambigu"
                : risk.Label + " · " + risk.Summary,
                risk?.RelativePath);
        }).ToArray();
        if (readOnly)
        {
            ActorBox.IsEnabled = false;
            RationaleBox.IsEnabled = false;
        }

        UpdateButtons();
    }

    /// <summary>Explicitly selected RIS GUID, or null on cancellation.</summary>
    public Guid? RiskId { get; private set; }

    /// <summary>Link or Unlink decision.</summary>
    public ProjectGateReservationRiskLinkKind? ChangeKind { get; private set; }

    /// <summary>Human approving the association.</summary>
    public string? Actor { get; private set; }

    /// <summary>Mandatory human motive.</summary>
    public string? Rationale { get; private set; }

    /// <summary>Validated canonical risk source selected for navigation only.</summary>
    public string? SelectedRiskSourcePath { get; private set; }

    /// <summary>Enables actions only for eligible rows outside archive mode.</summary>
    private void UpdateButtons()
    {
        LinkButton.IsEnabled = !_readOnly &&
            AvailableList.SelectedItem is ProjectRiskEntry;
        UnlinkButton.IsEnabled = !_readOnly &&
            LinkedList.SelectedItem is RiskLinkRow;
        OpenRiskButton.IsEnabled =
            AvailableList.SelectedItem is ProjectRiskEntry ||
            LinkedList.SelectedItem is RiskLinkRow { RelativePath: not null };
    }

    /// <summary>Recomputes buttons on selection only; no filesystem scanning.</summary>
    /// <param name="sender">Risk selection control.</param>
    /// <param name="e">WPF selection event.</param>
    private void Selection_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (sender == AvailableList &&
            AvailableList.SelectedItem is ProjectRiskEntry)
        {
            LinkedList.SelectedItem = null;
        }
        else if (sender == LinkedList &&
            LinkedList.SelectedItem is RiskLinkRow)
        {
            AvailableList.SelectedItem = null;
        }

        UpdateButtons();
    }

    /// <summary>Proposes one canonical-risk link, never by title/filename.</summary>
    /// <param name="sender">Link button.</param>
    /// <param name="e">WPF event.</param>
    private void Link_Click(object sender, RoutedEventArgs e)
    {
        if (AvailableList.SelectedItem is ProjectRiskEntry risk)
        {
            Accept(risk.Card.Id, ProjectGateReservationRiskLinkKind.Linked);
        }
    }

    /// <summary>Proposes explicit removal, including an orphan GUID.</summary>
    /// <param name="sender">Unlink button.</param>
    /// <param name="e">WPF event.</param>
    private void Unlink_Click(object sender, RoutedEventArgs e)
    {
        if (LinkedList.SelectedItem is RiskLinkRow row)
        {
            Accept(row.Id, ProjectGateReservationRiskLinkKind.Unlinked);
        }
    }

    /// <summary>Requires an actor and human motive, without saving anything.</summary>
    /// <param name="riskId">Stable GUID, already linked for unlink.</param>
    /// <param name="kind">Explicit association operation.</param>
    private void Accept(Guid riskId, ProjectGateReservationRiskLinkKind kind)
    {
        if (_readOnly)
        {
            return;
        }

        string actor = ActorBox.Text.Trim();
        string rationale = RationaleBox.Text.Trim();
        if (riskId == Guid.Empty ||
            string.IsNullOrWhiteSpace(actor) || actor.Length > 120 ||
            actor.Any(char.IsControl) ||
            rationale.Length < 10 || rationale.Length > 4000 ||
            rationale.Any(c => char.IsControl(c) && c is not ('\r' or '\n')))
        {
            MessageBox.Show(this,
                "Auteur et justification humaine (10 caractères minimum) " +
                "requis pour cette relation.",
                "Lien risque/réserve à qualifier",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        RiskId = riskId;
        ChangeKind = kind;
        Actor = actor;
        Rationale = rationale;
        DialogResult = true;
    }

    /// <summary>
    /// Returns the selected resolved RIS source for normal Markdown navigation.
    /// Orphan GUIDs are not navigable and never generate fallback paths.
    /// </summary>
    /// <param name="sender">Open source command.</param>
    /// <param name="e">WPF routed event.</param>
    private void OpenRisk_Click(object sender, RoutedEventArgs e)
    {
        string? path = AvailableList.SelectedItem is ProjectRiskEntry risk
            ? risk.RelativePath
            : (LinkedList.SelectedItem as RiskLinkRow)?.RelativePath;
        if (path is not null)
        {
            SelectedRiskSourcePath = path;
            DialogResult = true;
        }
    }

    /// <summary>Leaves every source untouched.</summary>
    /// <param name="sender">Close button.</param>
    /// <param name="e">WPF event.</param>
    private void Close_Click(object sender, RoutedEventArgs e) =>
        DialogResult = false;
}
