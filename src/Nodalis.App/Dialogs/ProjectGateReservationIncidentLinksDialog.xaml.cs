using System.Windows;
using System.Windows.Controls;
using Nodalis.Core.Projects;
using Nodalis.Infrastructure.Projects;

namespace Nodalis.App.Dialogs;

/// <summary>
/// Human-only selector for creating/removing canonical incident associations.
/// This dialog never writes data; the parent uses SHA-checked confirmation.
/// </summary>
public partial class ProjectGateReservationIncidentLinksDialog : Window
{
    private readonly bool _readOnly;

    /// <summary>Creates a project-scoped picker with existing link states.</summary>
    /// <param name="description">Historical reservation.</param>
    /// <param name="catalog">On-demand canonical incident candidates.</param>
    /// <param name="linked">Current incident identity projection.</param>
    /// <param name="readOnly">View-only mode for archived/read-only projects.</param>
    public ProjectGateReservationIncidentLinksDialog(
        string description, ProjectGateIncidentCatalog catalog,
        IReadOnlyList<string> linked, bool readOnly)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(linked);
        _readOnly = readOnly;
        InitializeComponent();
        DescriptionText.Text = "Réserve : " + description;
        DiagnosticsText.Text = catalog.Candidates.Count +
            " incident(s) canonique(s) du projet · " +
            catalog.ExcludedCount + " incident(s) non qualifiable(s)" +
            (readOnly ? " · consultation seule." :
                " · les références absentes restent dissociables.");
        HashSet<string> linkedIds = linked.ToHashSet(
            StringComparer.OrdinalIgnoreCase);
        AvailableList.ItemsSource = catalog.Candidates
            .Where(item => !linkedIds.Contains(item.Id)).ToArray();
        LinkedList.ItemsSource = linked.Select(id =>
        {
            ProjectGateIncidentCandidate? candidate = catalog.Candidates
                .FirstOrDefault(item => string.Equals(item.Id, id,
                    StringComparison.OrdinalIgnoreCase));
            return new IncidentLinkRow(id, candidate is null
                ? id + " · source disparue ou ambiguë"
                : candidate.Label + " · " + candidate.Location);
        }).ToArray();
        if (readOnly)
        {
            ActorBox.IsEnabled = false;
            RationaleBox.IsEnabled = false;
        }

        UpdateButtons();
    }

    /// <summary>Canonical ID returned only after an explicit human choice.</summary>
    public string? IncidentId { get; private set; }

    /// <summary>Verified action kind, or null when canceled.</summary>
    public ProjectGateReservationIncidentLinkKind? ChangeKind { get; private set; }

    /// <summary>Human actor accepting the link decision.</summary>
    public string? Actor { get; private set; }

    /// <summary>Human reason recorded in append-only audit.</summary>
    public string? Rationale { get; private set; }

    /// <summary>Enables actions according to selection and read-only mode.</summary>
    private void UpdateButtons()
    {
        LinkButton.IsEnabled = !_readOnly &&
            AvailableList.SelectedItem is ProjectGateIncidentCandidate;
        UnlinkButton.IsEnabled = !_readOnly &&
            LinkedList.SelectedItem is IncidentLinkRow;
    }

    /// <summary>Updates link choices when list selection changes.</summary>
    /// <param name="sender">List control.</param>
    /// <param name="e">Selection event.</param>
    private void Selection_Changed(object sender, SelectionChangedEventArgs e)
    {
        UpdateButtons();
    }

    /// <summary>Prepares an explicit incident link proposal.</summary>
    /// <param name="sender">Link button.</param>
    /// <param name="e">Click event.</param>
    private void Link_Click(object sender, RoutedEventArgs e)
    {
        if (AvailableList.SelectedItem is ProjectGateIncidentCandidate candidate)
        {
            Accept(candidate.Id, ProjectGateReservationIncidentLinkKind.Linked);
        }
    }

    /// <summary>Prepares a human unlink proposal, even for stale links.</summary>
    /// <param name="sender">Unlink button.</param>
    /// <param name="e">Click event.</param>
    private void Unlink_Click(object sender, RoutedEventArgs e)
    {
        if (LinkedList.SelectedItem is IncidentLinkRow row)
        {
            Accept(row.Id, ProjectGateReservationIncidentLinkKind.Unlinked);
        }
    }

    /// <summary>Checks human provenance before returning an action.</summary>
    /// <param name="incidentId">Stable incident identity.</param>
    /// <param name="kind">Requested change.</param>
    private void Accept(string incidentId,
        ProjectGateReservationIncidentLinkKind kind)
    {
        if (_readOnly)
        {
            return;
        }

        string actor = ActorBox.Text.Trim();
        string rationale = RationaleBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(actor) || actor.Length > 120 ||
            rationale.Length < 10 || rationale.Length > 4000)
        {
            MessageBox.Show(this,
                "Auteur requis et justification humaine d'au moins " +
                "10 caractères avant toute modification.",
                "Qualification du lien", MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        IncidentId = incidentId;
        ChangeKind = kind;
        Actor = actor;
        Rationale = rationale;
        DialogResult = true;
    }

    /// <summary>Closes the view without writing to source files.</summary>
    /// <param name="sender">Close button.</param>
    /// <param name="e">Click event.</param>
    private void Close_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }

    private sealed record IncidentLinkRow(string Id, string Label);
}
