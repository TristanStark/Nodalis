using System.IO;
using System.Windows;
using System.Windows.Controls;
using Nodalis.App.Reliability;
using Nodalis.Core.Risks;
using Nodalis.Infrastructure.Projects;

namespace Nodalis.App.Dialogs;

/// <summary>
/// Human-driven project risk browser with read-only discovery and explicit
/// creation of one canonical Markdown file. It never infers risk scores.
/// </summary>
public partial class ProjectRiskRegistryDialog : Window
{
    private readonly ProjectRiskRegistryService _service;
    private readonly string _projectDirectory;
    private readonly bool _readOnly;
    private ProjectRiskCatalog? _catalog;
    private bool _busy;

    /// <summary>Initializes a safe project-scoped risk manager.</summary>
    /// <param name="workspaceRoot">Single workspace.</param>
    /// <param name="projectDirectory">Canonical project location.</param>
    /// <param name="readOnly">Workspace or archive consultation mode.</param>
    public ProjectRiskRegistryDialog(
        string workspaceRoot, string projectDirectory, bool readOnly)
    {
        _service = new ProjectRiskRegistryService(workspaceRoot);
        _projectDirectory = Path.GetFullPath(projectDirectory);
        _readOnly = readOnly;
        InitializeComponent();
        FilterCombo.SelectedIndex = 0;
        Loaded += async (_, _) => await GuardAsync(
            () => ReloadAsync(null), "Charger les risques");
    }

    /// <summary>Existing safe source selected for Markdown navigation.</summary>
    public string? SelectedRiskPath { get; private set; }

    /// <summary>Whether an explicitly approved risk file was created.</summary>
    public bool Changed { get; private set; }

    /// <summary>Selected risk from the current validated in-memory catalog.</summary>
    /// <returns>Risk entry, or null.</returns>
    private ProjectRiskEntry? SelectedRisk() =>
        RisksList.SelectedItem as ProjectRiskEntry;

    /// <summary>Reads risk cards once per command and retains identity selection.</summary>
    /// <param name="selectedId">Previously selected stable risk identity.</param>
    /// <returns>Completed read without side effects.</returns>
    private async Task ReloadAsync(Guid? selectedId)
    {
        _catalog = await _service.ListAsync(_projectDirectory);
        RefreshFilteredRows(selectedId);
        DiagnosticsText.Text = _catalog.Diagnostics.Count == 0
            ? "Aucun document ignoré."
            : _catalog.Diagnostics.Count +
              " document(s) ignoré(s) :\n" +
              string.Join(Environment.NewLine, _catalog.Diagnostics.Take(6));
        StatusText.Text = _readOnly
            ? "Projet en lecture seule · aucun fichier modifiable."
            : _catalog.Risks.Count + " risque(s) avec GUID stable · prochaine référence : " +
              (_catalog.NextReference ?? "aucune");
        UpdateButtons();
    }

    /// <summary>
    /// Computes risk subsets from one immutable local catalog without
    /// rescanning the workspace on every typed character or selection.
    /// </summary>
    /// <param name="selectedId">Optional risk identity to keep selected.</param>
    private void RefreshFilteredRows(Guid? selectedId)
    {
        if (_catalog is null)
        {
            return;
        }

        string term = SearchBox.Text.Trim();
        int index = FilterCombo.SelectedIndex;
        ProjectRiskEntry[] entries = _catalog.Risks.Where(item =>
            (term.Length == 0 ||
             item.Card.Reference.Contains(term,
                 StringComparison.CurrentCultureIgnoreCase) ||
             item.Card.Title.Contains(term,
                 StringComparison.CurrentCultureIgnoreCase)) &&
            (index switch
            {
                1 => item.Reviews.Count == 0,
                2 => item.Reviews.Count > 0 &&
                    ProjectRiskMatrix.Evaluate(item.Card.Residual) ==
                        ProjectRiskSeverity.Unassessed,
                3 => ProjectRiskMatrix.Evaluate(item.Card.Initial) ==
                        ProjectRiskSeverity.High ||
                    ProjectRiskMatrix.Evaluate(item.Card.Residual) ==
                        ProjectRiskSeverity.High,
                4 => ProjectRiskMatrix.Evaluate(item.Card.Initial) ==
                        ProjectRiskSeverity.Moderate ||
                    ProjectRiskMatrix.Evaluate(item.Card.Residual) ==
                        ProjectRiskSeverity.Moderate,
                5 => ProjectRiskMatrix.Evaluate(item.Card.Initial) ==
                        ProjectRiskSeverity.Low ||
                    ProjectRiskMatrix.Evaluate(item.Card.Residual) ==
                        ProjectRiskSeverity.Low,
                6 => item.Card.Lifecycle == ProjectRiskLifecycle.Monitoring,
                7 => item.Card.Lifecycle == ProjectRiskLifecycle.Closed,
                8 => item.Card.Qualification == ProjectRiskQualification.ToReview,
                _ => true
            })).ToArray();
        RisksList.ItemsSource = entries;
        RisksList.SelectedItem = entries.FirstOrDefault(item =>
            item.Card.Id == selectedId) ?? entries.FirstOrDefault();
        CountsText.Text = entries.Length + "/" + _catalog.Risks.Count +
            " affiché(s) · " +
            _catalog.Risks.Count(item => item.Reviews.Count == 0) +
            " sans avis initial";
        PaintSelection();
    }

    /// <summary>Updates only the in-memory subset when the filter changes.</summary>
    /// <param name="sender">Filter source control.</param>
    /// <param name="e">Selection or text change event.</param>
    private void Filter_Changed(object sender, RoutedEventArgs e)
    {
        if (!_busy)
        {
            RefreshFilteredRows(SelectedRisk()?.Card.Id);
        }
    }

    /// <summary>Updates right pane without any disk access.</summary>
    private void PaintSelection()
    {
        ProjectRiskEntry? entry = SelectedRisk();
        if (entry is null)
        {
            RiskDetailsBox.Text = "Aucune fiche structurée sélectionnée.\n" +
                "Les anciens documents ne sont jamais convertis automatiquement.";
        }
        else
        {
            ProjectRiskCard card = entry.Card;
            RiskDetailsBox.Text = string.Join(Environment.NewLine,
            [
                "Référence : " + card.Reference,
                "GUID : " + card.Id.ToString("D"),
                "Projet : " + card.ProjectId.ToString("D"),
                "Titre : " + card.Title,
                "Qualification : " + card.Qualification,
                "Cycle de vie : " + card.Lifecycle,
                "Initiale : " + ProjectRiskMatrix.Evaluate(card.Initial),
                "Résiduelle : " + ProjectRiskMatrix.Evaluate(card.Residual),
                "Créée le : " + card.CreatedUtc.ToLocalTime()
                    .ToString("dd/MM/yyyy HH:mm"),
                "",
                "Source : " + entry.RelativePath,
                "",
                "Historique de qualification humaine : " +
                    entry.Reviews.Count + " avis",
                .. entry.Reviews.Select(review =>
                    review.RecordedUtc.ToLocalTime()
                        .ToString("dd/MM/yyyy HH:mm") +
                    " · " + review.Kind +
                    " · P=" + review.Rating.Probability +
                    " × I=" + review.Rating.Impact +
                    " · " + review.Actor +
                    " · " + review.Rationale),
                "",
                "La première évaluation reste figée. Aucun score ni " +
                    "changement de cycle de vie automatique."
            ]);
        }

        UpdateButtons();
    }

    /// <summary>Disables unsafe commands during asynchronous operations.</summary>
    private void UpdateButtons()
    {
        CreateRiskButton.IsEnabled = !_busy && !_readOnly &&
            _catalog?.NextReference is not null;
        ProjectRiskEntry? entry = SelectedRisk();
        OpenRiskButton.IsEnabled = !_busy && entry is not null;
        InitialReviewButton.IsEnabled = !_busy && !_readOnly &&
            entry is not null && entry.Reviews.Count == 0;
        ResidualReviewButton.IsEnabled = !_busy && !_readOnly &&
            entry is not null && entry.Reviews.Count > 0;
    }

    /// <summary>Routes asynchronous errors to the existing WPF error boundary.</summary>
    /// <param name="action">One explicit action.</param>
    /// <param name="context">Action label.</param>
    /// <returns>Completed guarded work.</returns>
    private async Task GuardAsync(Func<Task> action, string context)
    {
        if (_busy)
        {
            return;
        }

        _busy = true;
        UpdateButtons();
        try
        {
            await UiActionGuard.RunAsync(this, "Risques · " + context, action);
        }
        finally
        {
            _busy = false;
            UpdateButtons();
        }
    }

    /// <summary>Proposes creation of one unassessed risk after human confirmation.</summary>
    /// <param name="sender">Create button.</param>
    /// <param name="e">Click event.</param>
    private async void CreateRisk_Click(object sender, RoutedEventArgs e) =>
        await GuardAsync(async () =>
        {
            if (_readOnly || _catalog?.NextReference is not string next)
            {
                return;
            }

            TextPromptDialog prompt = new(
                "Créer une fiche de risque", "Titre du nouveau risque :")
            {
                Owner = this
            };
            if (prompt.ShowDialog() != true || prompt.Value is not string title)
            {
                return;
            }

            if (MessageBox.Show(this,
                "Créer « " + next + " — " + title.Trim() +
                " » dans Analyse/Risques ?\n\n" +
                "Une seule fiche Markdown sera ajoutée. Aucune évaluation " +
                "automatique : criticité initiale et résiduelle restent " +
                "non évaluées. Les anciennes fiches et tous les gates " +
                "restent inchangés.",
                "Créer un risque · confirmer",
                MessageBoxButton.YesNo, MessageBoxImage.Question) !=
                MessageBoxResult.Yes)
            {
                return;
            }

            ProjectRiskEntry created = await _service.CreateAsync(
                _projectDirectory, title);
            Changed = true;
            await ReloadAsync(created.Card.Id);
            StatusText.Text = "Risque " + created.Card.Reference +
                " créé · GUID conservé · criticité non évaluée.";
        }, "Créer un risque");

    /// <summary>
    /// Submits one human assessment through the shared atomic source service.
    /// The user explicitly confirms the numbers and provenance after review;
    /// every old assessment remains in the risk Markdown as a frozen event.
    /// </summary>
    /// <param name="kind">Initial review or residual requalification.</param>
    /// <returns>Completed action or unchanged source on cancellation.</returns>
    private async Task ReviewRiskAsync(ProjectRiskReviewKind kind)
    {
        if (_readOnly || SelectedRisk() is not ProjectRiskEntry selected ||
            kind == ProjectRiskReviewKind.Initial &&
                selected.Reviews.Count != 0 ||
            kind == ProjectRiskReviewKind.Residual &&
                selected.Reviews.Count == 0)
        {
            return;
        }

        ProjectRiskReviewDialog dialog = new(selected, kind) { Owner = this };
        if (dialog.ShowDialog() != true ||
            dialog.Rating is not ProjectRiskRating rating ||
            dialog.Actor is not string actor ||
            dialog.Rationale is not string rationale)
        {
            return;
        }

        if (MessageBox.Show(this,
            "Confirmer l'avis humain sur " + selected.Card.Reference +
            " — " + selected.Card.Title + " ?\n\n" +
            "Type : " + (kind == ProjectRiskReviewKind.Initial
                ? "première qualification (figée définitivement)"
                : "réévaluation résiduelle historisée") +
            "\nProbabilité : " + rating.Probability +
            "\nImpact : " + rating.Impact +
            "\nCriticité : " + ProjectRiskMatrix.Evaluate(rating) +
            "\nAuteur : " + actor +
            "\nJustification : " + rationale +
            "\n\nLe fichier Markdown du risque sera le seul document " +
            "modifié. L'historique précédent restera intact. Aucun gate, " +
            "incident, jalon ni tâche ne sera modifié.",
            "Risque · approbation humaine",
            MessageBoxButton.YesNo, MessageBoxImage.Question) !=
            MessageBoxResult.Yes)
        {
            return;
        }

        ProjectRiskEntry saved = await _service.ReviewAsync(
            _projectDirectory, selected, kind, rating, actor, rationale);
        Changed = true;
        await ReloadAsync(saved.Card.Id);
        StatusText.Text = saved.Card.Reference + " · avis enregistré " +
            "avec contrôle SHA-256 et historique append-only.";
    }

    /// <summary>Requests the immutable first risk assessment.</summary>
    /// <param name="sender">Initial review button.</param>
    /// <param name="e">WPF action.</param>
    private async void InitialReview_Click(object sender, RoutedEventArgs e) =>
        await GuardAsync(() => ReviewRiskAsync(ProjectRiskReviewKind.Initial),
            "Qualifier la criticité initiale");

    /// <summary>Requests a new independent residual rating.</summary>
    /// <param name="sender">Residual review button.</param>
    /// <param name="e">WPF action.</param>
    private async void ResidualReview_Click(object sender, RoutedEventArgs e) =>
        await GuardAsync(() => ReviewRiskAsync(ProjectRiskReviewKind.Residual),
            "Réévaluer la criticité résiduelle");

    /// <summary>Opens the selected Markdown file in the existing editor.</summary>
    /// <param name="sender">Open button.</param>
    /// <param name="e">Click event.</param>
    private void OpenRisk_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedRisk() is not ProjectRiskEntry entry)
        {
            return;
        }

        SelectedRiskPath = entry.RelativePath;
        DialogResult = true;
    }

    /// <summary>Updates the details on in-memory selection change.</summary>
    /// <param name="sender">Risk list.</param>
    /// <param name="e">Selection event.</param>
    private void RisksList_SelectionChanged(
        object sender, SelectionChangedEventArgs e)
    {
        PaintSelection();
    }

    /// <summary>Refreshes only on user command.</summary>
    /// <param name="sender">Refresh button.</param>
    /// <param name="e">Click event.</param>
    private async void Reload_Click(object sender, RoutedEventArgs e) =>
        await GuardAsync(() => ReloadAsync(SelectedRisk()?.Card.Id),
            "Recharger les risques");

    /// <summary>Closes the view without changing sources.</summary>
    /// <param name="sender">Close button.</param>
    /// <param name="e">Click event.</param>
    private void Close_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
