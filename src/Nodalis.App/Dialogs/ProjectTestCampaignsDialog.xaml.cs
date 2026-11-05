using System.IO;
using System.Windows;
using System.Windows.Controls;
using Nodalis.Core.Links;
using Nodalis.Core.Tests;
using Nodalis.Infrastructure.Links;
using Nodalis.Infrastructure.Projects;

namespace Nodalis.App.Dialogs;

/// <summary>
/// Project-local campaign browser with human-reviewed append commands.
/// Every file operation runs on a worker; lists and filters use cached results.
/// </summary>
public partial class ProjectTestCampaignsDialog : Window
{
    private readonly string _workspaceRoot;
    private readonly string _projectDirectory;
    private readonly bool _readOnly;
    private readonly ProjectTestCampaignService _service;
    private readonly WorkspaceLinkIndexService _indexService;
    private ProjectTestCampaignCatalog? _catalog;
    private bool _busy;
    private bool _painting;
    private long _passageViewRevision;
    private long _sourceValidityRevision;
    private ProjectTestSourceValidityOverview? _sourceValidity;

    /// <summary>Creates a project-specific campaign browser.</summary>
    /// <param name="workspaceRoot">Canonical workspace root.</param>
    /// <param name="projectDirectory">Current project path.</param>
    /// <param name="readOnly">Workspace or archived consultation only.</param>
    public ProjectTestCampaignsDialog(
        string workspaceRoot, string projectDirectory, bool readOnly)
    {
        _workspaceRoot = Path.GetFullPath(workspaceRoot);
        _projectDirectory = Path.GetFullPath(projectDirectory);
        _readOnly = readOnly;
        _service = new ProjectTestCampaignService(_workspaceRoot);
        _indexService = new WorkspaceLinkIndexService(_workspaceRoot);
        InitializeComponent();
        Loaded += async (_, _) => await GuardAsync(
            "Lire les campagnes", () => ReloadAsync(null));
    }

    /// <summary>Optional REC Markdown source chosen for safe navigation.</summary>
    public string? SelectedCampaignPath { get; private set; }

    /// <summary>Whether a user confirmed any new campaign, passage or result.</summary>
    public bool Changed { get; private set; }

    /// <summary>Current validated campaign selection.</summary>
    private CampaignRow? SelectedCampaign() =>
        CampaignsList.SelectedItem as CampaignRow;

    /// <summary>Current frozen passage selection.</summary>
    private PassageRow? SelectedPassage() =>
        PassagesList.SelectedItem as PassageRow;

    /// <summary>Current exact passage-test outcome selection.</summary>
    private ResultRow? SelectedTest() =>
        TestsList.SelectedItem as ResultRow;

    /// <summary>Reads the canonical REC registry once and keeps current selection.</summary>
    /// <param name="selectedId">Campaign ID to reactivate, if any.</param>
    /// <returns>Completed background refresh.</returns>
    private async Task ReloadAsync(Guid? selectedId)
    {
        _passageViewRevision++;
        _sourceValidityRevision++;
        _sourceValidity = null;
        _catalog = await Task.Run(() =>
            _service.ListAsync(_projectDirectory));
        CampaignRow[] rows = _catalog.Campaigns.Select(entry =>
            new CampaignRow(entry,
                entry.Campaign.Reference + " — " + entry.Campaign.Title,
                entry.Campaign.State + " · " +
                entry.Campaign.Passages.Count + " passage(s)"))
            .ToArray();
        _painting = true;
        try
        {
            CampaignsList.ItemsSource = rows;
            CampaignsList.SelectedItem = rows.FirstOrDefault(item =>
                item.Entry.Campaign.Id == selectedId) ??
                rows.FirstOrDefault();
        }
        finally
        {
            _painting = false;
        }

        SummaryText.Text = _catalog.Campaigns.Count +
            " campagne(s) · " + _catalog.Diagnostics.Count +
            " fichier(s) à vérifier · prochaine référence : " +
            (_catalog.NextReference ?? "épuisée");
        StatusText.Text = _readOnly
            ? "Consultation uniquement · aucune écriture autorisée."
            : "Registre lu sans création implicite de dossier.";
        await PaintCampaignAsync();
        await RefreshSourceValidityAsync();
    }

    /// <summary>Shows the passages of the selected campaign in memory.</summary>
    /// <returns>Completed selection.</returns>
    private async Task PaintCampaignAsync()
    {
        _passageViewRevision++;
        // Never briefly display another campaign's cached validity while
        // its own source check is pending.
        _sourceValidityRevision++;
        _sourceValidity = null;
        CampaignRow? selected = SelectedCampaign();
        PassageRow[] rows = selected is null
            ? []
            : selected.Entry.Campaign.Passages.Select(passage =>
                new PassageRow(passage,
                    "Passage " + passage.Number,
                    passage.Environment + " · " +
                    passage.ApplicationVersion + " · " +
                    passage.Tests.Count + " test(s)"))
                .ToArray();
        _painting = true;
        try
        {
            PassagesList.ItemsSource = rows;
            PassagesList.SelectedItem = rows.LastOrDefault();
        }
        finally
        {
            _painting = false;
        }

        await PaintPassageAsync();
    }

    /// <summary>
    /// Compares historic frozen tests with today's sources on a worker.
    /// The refreshed shared index is checked once; each current cahier is
    /// then parsed only once even across many passages.
    /// </summary>
    /// <returns>Completed comparison for the still-selected campaign.</returns>
    private async Task RefreshSourceValidityAsync()
    {
        CampaignRow? chosen = SelectedCampaign();
        long revision = ++_sourceValidityRevision;
        _sourceValidity = null;
        if (chosen is null)
        {
            return;
        }

        StatusText.Text =
            "Contrôle des versions de tests en cours (sans modifier les sources)…";
        ProjectTestSourceValidityOverview overview = await Task.Run(
            async () =>
            {
                LinkIndexCatalog index = await _indexService.RefreshAsync();
                return await _service.AssessSourceValidityAsync(
                    _projectDirectory, chosen.Entry, index);
            });
        if (revision != _sourceValidityRevision ||
            !ReferenceEquals(chosen, SelectedCampaign()))
        {
            return;
        }

        _sourceValidity = overview;
        StatusText.Text = overview.NeedsReview + "/" + overview.Rows.Count +
            " définition(s) historiques à réexaminer ; verdicts inchangés.";
        await PaintPassageAsync();
    }

    /// <summary>
    /// Parses the selected frozen passage on a worker and rejects stale
    /// render completions after any campaign/passage selection change.
    /// </summary>
    /// <returns>Completed current selection render.</returns>
    private async Task PaintPassageAsync()
    {
        long revision = ++_passageViewRevision;
        PassageRow? selected = SelectedPassage();
        CampaignRow? campaign = SelectedCampaign();
        TestsList.ItemsSource = null;
        if (selected is null || campaign is null)
        {
            DetailsBox.Text = campaign is null
                ? "Sélectionnez une campagne ou créez-en une."
                : "Campagne sans passage. Aucun résultat n'a été exécuté.";
            UpdateButtons();
            return;
        }

        IReadOnlyList<ProjectTestPassageResultRow> results = await Task.Run(
            () => ProjectTestCampaignProjection.GetPassageRows(
                campaign.Entry.Campaign, selected.Passage.Id));
        if (revision != _passageViewRevision ||
            !ReferenceEquals(campaign, SelectedCampaign()) ||
            !ReferenceEquals(selected, SelectedPassage()))
        {
            return;
        }

        Dictionary<Guid, ProjectTestSourceValidityRow> validity =
            _sourceValidity?.Rows.Where(row =>
                row.PassageId == selected.Passage.Id).ToDictionary(
                    row => row.TestId) ?? [];
        ResultRow[] rows = results.Select(result =>
        {
            validity.TryGetValue(result.TestId,
                out ProjectTestSourceValidityRow? current);
            return new ResultRow(result,
                result.Reference + " · " +
                (result.Outcome is ProjectTestOutcome outcome
                    ? DescribeOutcome(outcome) : "NON EXÉCUTÉ"),
                result.Environment + " / " + result.ApplicationVersion +
                " · " + (current is null
                    ? "Source non contrôlée"
                    : DescribeValidity(current.Validity)),
                current);
        }).ToArray();
        _painting = true;
        try
        {
            TestsList.ItemsSource = rows;
            TestsList.SelectedItem = rows.FirstOrDefault();
        }
        finally
        {
            _painting = false;
        }

        RenderDetails();
    }

    /// <summary>Displays the selected result and immutable source metadata.</summary>
    private void RenderDetails()
    {
        CampaignRow? campaign = SelectedCampaign();
        PassageRow? passage = SelectedPassage();
        ResultRow? test = SelectedTest();
        if (campaign is null || passage is null)
        {
            ProjectTestCampaignDecisionEvent? latest =
                campaign?.Entry.Campaign.DecisionHistory?.LastOrDefault();
            DetailsBox.Text = campaign is null
                ? "Aucune campagne sélectionnée."
                : "Campagne : " + campaign.Entry.Campaign.Reference +
                  "\nÉtat : " + campaign.Entry.Campaign.State +
                  "\nPassage : aucun sélectionné." +
                  (latest is null ? string.Empty :
                      "\nDernière décision : " + latest.Kind +
                      "\nAuteur : " + latest.Actor +
                      "\nMotif : " + latest.Rationale);
            UpdateButtons();
            return;
        }

        List<string> details =
        [
            "Campagne : " + campaign.Entry.Campaign.Reference,
            "Passage : " + passage.Passage.Number + " · " +
                passage.Passage.Id.ToString("D"),
            "Environnement : " + passage.Passage.Environment,
            "Version : " + passage.Passage.ApplicationVersion,
            "Autorisé par : " + passage.Passage.StartedBy,
            "Lancé le : " + passage.Passage.StartedUtc.ToLocalTime()
                .ToString("dd/MM/yyyy HH:mm"),
            "Tests figés : " + passage.Passage.Tests.Count,
            "Résultats déclarés : " + passage.Passage.Results.Count,
            "Non exécutés : " + (passage.Passage.Tests.Count -
                passage.Passage.Results.Count),
            "",
            "Les données du passage sont historiques et ne sont pas" +
                " corrigées automatiquement après modification du cahier."
        ];
        ProjectTestCampaignDecisionEvent? decision =
            campaign.Entry.Campaign.DecisionHistory?.LastOrDefault();
        if (decision is not null)
        {
            details.AddRange(
            [
                "",
                "DERNIÈRE DÉCISION HUMAINE : " + decision.Kind,
                "État REC : " + decision.ToState,
                "Auteur : " + decision.Actor,
                "Motif : " + decision.Rationale,
                "Verdict de clôture : " +
                    (decision.Verdict?.ToString() ?? "aucun"),
                "Réserves : " +
                    (decision.Reservations is { Count: > 0 }
                        ? string.Join("; ", decision.Reservations)
                        : "aucune")
            ]);
        }
        if (test is not null)
        {
            ProjectTestFrozenDefinition? source = passage.Passage.Tests
                .FirstOrDefault(item => item.TestId == test.Result.TestId);
            ProjectTestResultEvent? outcome = passage.Passage.Results
                .FirstOrDefault(item => item.TestId == test.Result.TestId);
            details.AddRange(
            [
                "",
                "TEST : " + test.Result.Reference,
                "GUID : " + test.Result.TestId.ToString("D"),
                "Définition : " + (source?.SourceRelativePath ?? "absente"),
                "Empreinte figée : " + test.Result.FrozenSha256,
                "SOURCE AUJOURD'HUI : " + (test.Validity is null
                    ? "Contrôle non effectué"
                    : DescribeValidity(test.Validity.Validity)),
                "Empreinte actuelle : " +
                    (test.Validity?.CurrentSha256 ?? "indisponible"),
                "Diagnostic : " +
                    (test.Validity?.Detail ?? "Contrôle requis"),
                "Résultat historique inchangé : " +
                    "aucune requalification automatique.",
                "Résultat :" + (outcome is null
                    ? "NON EXÉCUTÉ" : DescribeOutcome(outcome.Outcome)),
                "Exécutant : " + (outcome?.Actor ?? "aucun"),
                "Justification : " + (outcome?.Rationale ?? "aucune"),
                "Preuve déclarée : " +
                    (outcome?.EvidenceNote ?? "aucune / non vérifiée"),
                "Aucune conformité ni validation métier n'est déduite."
            ]);
        }

        DetailsBox.Text = string.Join(Environment.NewLine, details);
        UpdateButtons();
    }

    /// <summary>Short UI labels for human-declared, nonqualified outcomes.</summary>
    /// <param name="outcome">Result event.</param>
    /// <returns>Readable label.</returns>
    private static string DescribeOutcome(ProjectTestOutcome outcome) =>
        outcome switch
        {
            ProjectTestOutcome.Passed => "Conforme déclaré",
            ProjectTestOutcome.Failed => "Non conforme",
            ProjectTestOutcome.Blocked => "Bloqué",
            ProjectTestOutcome.NotApplicable => "Non applicable",
            _ => "Verdict inconnu"
        };

    /// <summary>Distinguishes historic results from live source validity.</summary>
    /// <param name="validity">Conservative source comparison.</param>
    /// <returns>French current-source label.</returns>
    private static string DescribeValidity(ProjectTestSourceValidity validity) =>
        validity switch
        {
            ProjectTestSourceValidity.Current => "Définition inchangée",
            ProjectTestSourceValidity.Modified =>
                "MODIFIÉE · à requalifier",
            ProjectTestSourceValidity.Missing =>
                "SOURCE ABSENTE · à examiner",
            ProjectTestSourceValidity.Ambiguous =>
                "IDENTITÉ AMBIGUË · à examiner",
            ProjectTestSourceValidity.IndexOutOfDate =>
                "INDEX OBSOLÈTE · actualiser",
            _ => "Contrôle indisponible"
        };

    /// <summary>Enables commands only for the current safe selection.</summary>
    private void UpdateButtons()
    {
        if (CreateButton is null)
        {
            return;
        }

        CampaignRow? campaign = SelectedCampaign();
        PassageRow? passage = SelectedPassage();
        ResultRow? result = SelectedTest();
        bool writable = !_readOnly && !_busy;
        CreateButton.IsEnabled = writable &&
            _catalog?.NextReference is not null;
        StartButton.IsEnabled = writable && campaign is not null &&
            campaign.Entry.Campaign.State is
                ProjectTestCampaignState.Draft or
                ProjectTestCampaignState.InProgress;
        RetestButton.IsEnabled = writable && campaign is not null &&
            campaign.Entry.Campaign.State ==
                ProjectTestCampaignState.InProgress &&
            campaign.Entry.Campaign.Passages.Count > 0;
        CaRetestButton.IsEnabled = writable && campaign is not null &&
            campaign.Entry.Campaign.State is
                ProjectTestCampaignState.Draft or
                ProjectTestCampaignState.InProgress;
        CaFollowupButton.IsEnabled = !_busy && campaign is not null;
        RecordButton.IsEnabled = writable && campaign is not null &&
            campaign.Entry.Campaign.State ==
                ProjectTestCampaignState.InProgress &&
            passage is not null && result is not null &&
            result.Result.Outcome is null &&
            campaign.Entry.Campaign.Passages.LastOrDefault()?.Id ==
                passage.Passage.Id;
        OpenButton.IsEnabled = campaign is not null;
        RefreshButton.IsEnabled = !_busy;
        ProjectTestCampaignState? state = campaign?.Entry.Campaign.State;
        QualificationButton.IsEnabled = writable &&
            state is (ProjectTestCampaignState.Draft or
                ProjectTestCampaignState.InProgress);
        CloseCampaignButton.IsEnabled = writable &&
            state == ProjectTestCampaignState.InQualification;
        ReopenCampaignButton.IsEnabled = writable &&
            state is (ProjectTestCampaignState.Closed or
                ProjectTestCampaignState.Canceled);
        CancelCampaignButton.IsEnabled = writable &&
            state is (ProjectTestCampaignState.Draft or
                ProjectTestCampaignState.InProgress or
                ProjectTestCampaignState.InQualification);
    }

    /// <summary>Only one user-driven mutation executes at a time.</summary>
    /// <param name="label">Action label.</param>
    /// <param name="action">Guarded asynchronous action.</param>
    /// <returns>Completed UI action.</returns>
    private async Task GuardAsync(string label, Func<Task> action)
    {
        if (_busy)
        {
            return;
        }

        _busy = true;
        UpdateButtons();
        try
        {
            await action();
        }
        catch (Exception exception) when (exception is
            IOException or InvalidDataException or ArgumentException or
            InvalidOperationException or UnauthorizedAccessException)
        {
            MessageBox.Show(this, exception.Message, label,
                MessageBoxButton.OK, MessageBoxImage.Warning);
            StatusText.Text = label + " : action refusée, aucune validation implicite.";
        }
        finally
        {
            _busy = false;
            UpdateButtons();
        }
    }

    /// <summary>Refreshes campaign selection after an explicit click.</summary>
    /// <param name="sender">Reload button.</param>
    /// <param name="e">Click.</param>
    private async void Refresh_Click(object sender, RoutedEventArgs e)
    {
        Guid? id = SelectedCampaign()?.Entry.Campaign.Id;
        await GuardAsync("Actualiser les campagnes",
            () => ReloadAsync(id));
    }

    /// <summary>Updates selected passage list when campaign changes.</summary>
    /// <param name="sender">Campaign list.</param>
    /// <param name="e">Selection event.</param>
    private async void CampaignsList_SelectionChanged(
        object sender, SelectionChangedEventArgs e)
    {
        if (!_painting)
        {
            await PaintCampaignAsync();
            await GuardAsync("Contrôler les définitions actuelles",
                RefreshSourceValidityAsync);
        }
    }

    /// <summary>Projects a selected exact passage off the UI thread.</summary>
    /// <param name="sender">Passages list.</param>
    /// <param name="e">Selection event.</param>
    private async void PassagesList_SelectionChanged(
        object sender, SelectionChangedEventArgs e)
    {
        if (!_painting)
        {
            await PaintPassageAsync();
        }
    }

    /// <summary>Shows current selected human result without I/O.</summary>
    /// <param name="sender">Results list.</param>
    /// <param name="e">Selection event.</param>
    private void TestsList_SelectionChanged(
        object sender, SelectionChangedEventArgs e)
    {
        if (!_painting)
        {
            RenderDetails();
        }
    }

    /// <summary>Opens a title prompt, then confirms exactly one new REC.</summary>
    /// <param name="sender">Create button.</param>
    /// <param name="e">Click.</param>
    private async void Create_Click(object sender, RoutedEventArgs e) =>
        await GuardAsync("Créer une campagne", CreateCampaignAsync);

    /// <summary>Creates one title-approved project-local campaign draft.</summary>
    /// <returns>Completed reviewed creation.</returns>
    private async Task CreateCampaignAsync()
    {
        if (_readOnly || _catalog?.NextReference is not string next)
        {
            return;
        }

        TextPromptDialog titleDialog = new(
            "Nouvelle campagne · Nodalis 2.0",
            "Titre de la campagne de recette (240 caractères maximum) :")
        {
            Owner = this
        };
        if (titleDialog.ShowDialog() != true)
        {
            return;
        }

        string title = titleDialog.Value;
        if (title.Length > 240 || title.Any(char.IsControl))
        {
            throw new InvalidDataException("Titre de campagne trop long.");
        }

        if (MessageBox.Show(this,
            "Créer " + next + " — " + title + " ?\n\n" +
            "Fichier : Tests/Campagnes/" + next + ".md\n" +
            "État : brouillon · aucun résultat\n\n" +
            "Aucun cahier de test ne sera modifié.",
            "Nouvelle campagne · confirmation",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question) != MessageBoxResult.Yes)
        {
            return;
        }

        ProjectTestCampaignEntry entry = await Task.Run(
            () => _service.CreateAsync(_projectDirectory, title, next));
        Changed = true;
        WorkspaceLinkIndexService.NotifyWorkspaceChanged(_workspaceRoot);
        await ReloadAsync(entry.Campaign.Id);
        StatusText.Text = "Campagne créée : " + entry.Campaign.Reference;
    }

    /// <summary>Previews selected source GUIDs before freezing a new passage.</summary>
    /// <param name="sender">Start button.</param>
    /// <param name="e">Click.</param>
    private async void Start_Click(object sender, RoutedEventArgs e) =>
        await GuardAsync("Démarrer un passage",
            () => StartPassageAsync(false));

    /// <summary>Proposes a human-editable selection for a new REC passage.</summary>
    /// <param name="sender">Targeted retest button.</param>
    /// <param name="e">Button event.</param>
    private async void Retest_Click(object sender, RoutedEventArgs e) =>
        await GuardAsync("Proposer une reprise de tests",
            () => StartPassageAsync(true));

    /// <summary>
    /// Uses the project's explicitly recorded CA baseline and verified
    /// test links to suggest only safely selectable current test GUIDs.
    /// The existing editable passage selector retains final human control.
    /// </summary>
    /// <param name="sender">Explicit CA-driven replay command.</param>
    /// <param name="e">User click event.</param>
    private async void CaRetest_Click(object sender, RoutedEventArgs e) =>
        await GuardAsync("Proposer les tests des CA modifiés",
            () => StartPassageAsync(false, true));

    /// <summary>
    /// Displays current CA-driven test inclusion and explicitly declared
    /// latest-passage results, without starting, closing or editing REC.
    /// An older Passed never implies a new CA has been requalified.
    /// </summary>
    /// <param name="sender">Read-only follow-up action.</param>
    /// <param name="e">Human click event.</param>
    private async void CaFollowup_Click(object sender, RoutedEventArgs e) =>
        await GuardAsync("Suivi des reprises CA",
            RefreshCaFollowupAsync);

    /// <summary>
    /// Reads a project-local CA baseline, a refreshed link index, selectable
    /// tests and the latest on-disk REC campaign snapshot on a worker. The
    /// result is an advisory only, not evidence or an authorization.
    /// </summary>
    /// <returns>Display of current, conservatively interpreted progress.</returns>
    private async Task RefreshCaFollowupAsync()
    {
        CampaignRow? chosen = SelectedCampaign();
        if (chosen is null)
        {
            return;
        }

        Guid selectedId = chosen.Entry.Campaign.Id;
        StatusText.Text = "Lecture CA→tests et dernier passage REC en cours…";
        (
            ProjectCriterionBaselineOverview Baseline,
            ProjectCriterionRetestProposal Proposal,
            ProjectTestCampaign Campaign,
            ProjectTestSourceValidityOverview? SourceValidity,
            string? SourceWarning
        ) snapshot = await Task.Run(async () =>
        {
            LinkIndexCatalog index = await _indexService.RefreshAsync();
            ProjectRequirementCatalog requirements =
                await new ProjectRequirementRegistryService(_workspaceRoot)
                    .ListAsync(_projectDirectory);
            ProjectCriterionBaselineOverview baseline =
                await new ProjectCriterionBaselineService(_workspaceRoot)
                    .InspectAsync(_projectDirectory, requirements);
            ProjectRequirementTraceabilityOverview links =
                ProjectRequirementTraceability.Build(requirements, index);
            ProjectCriterionSelectableTest[] selectable =
                FindAvailableTests(index, _workspaceRoot, _projectDirectory)
                    .Select(item => new ProjectCriterionSelectableTest(
                        item.Id, item.Label, item.RelativePath)).ToArray();
            ProjectCriterionRetestProposal proposal =
                ProjectCriterionRetestPlanner.Propose(
                    baseline, links, selectable);
            ProjectTestCampaignCatalog rec =
                await _service.ListAsync(_projectDirectory);
            ProjectTestCampaignEntry[] selected = rec.Campaigns
                .Where(item => item.Campaign.Id == selectedId).ToArray();
            if (selected.Length != 1 ||
                selected[0].Campaign.ProjectId != requirements.ProjectId)
            {
                throw new InvalidDataException(
                    "Campagne REC absente, dupliquée ou hors du projet.");
            }

            ProjectTestSourceValidityOverview? validity = null;
            string? sourceWarning = null;
            try
            {
                validity = await _service.AssessSourceValidityAsync(
                    _projectDirectory, selected[0], index);
            }
            catch (Exception exception) when (exception is IOException or
                UnauthorizedAccessException or InvalidDataException or
                InvalidOperationException)
            {
                sourceWarning = "Vérification des définitions TEST " +
                    "indisponible (" + exception.GetType().Name +
                    ") : relire les sources avant toute décision humaine.";
            }

            return (baseline, proposal, selected[0].Campaign,
                validity, sourceWarning);
        });

        if (SelectedCampaign()?.Entry.Campaign.Id != selectedId)
        {
            StatusText.Text = "Campagne modifiée pendant le contrôle : " +
                "relancer le suivi CA.";
            return;
        }

        if (snapshot.Baseline.Snapshot is null)
        {
            DetailsBox.Text =
                "SUIVI CA NON INITIALISÉ\n\n" +
                string.Join(Environment.NewLine,
                    snapshot.Proposal.Warnings) +
                "\n\nActivez une référence humaine dans la matrice " +
                "des exigences avant les éditions à surveiller. " +
                "Aucun ancien passage ou résultat n'est modifié.";
            StatusText.Text = "Référence CA absente · aucun changement " +
                "historique ne peut être reconstitué.";
            return;
        }

        ProjectCriterionRetestProgressOverview progress =
            await Task.Run(() => ProjectCriterionRetestProgress.Build(
                snapshot.Baseline, snapshot.Proposal, snapshot.Campaign,
                snapshot.SourceValidity));
        if (snapshot.SourceWarning is string warning)
        {
            progress = progress with
            {
                Warnings = [.. progress.Warnings, warning]
            };
        }

        if (SelectedCampaign()?.Entry.Campaign.Id != selectedId)
        {
            return;
        }

        DetailsBox.Text = progress.Describe();
        StatusText.Text = "Suivi CA " + progress.Rows.Count +
            " test(s) : " + progress.NotSelectedCount +
            " absent(s) ou non comparable(s), " +
            progress.WithoutResultCount + " figé(s) sans résultat, " +
            progress.HumanResultCount + " verdict(s) humain(s) " +
            "historiques et " + progress.SourceReviewCount +
            " source(s) à contrôler. Aucun état métier modifié.";
    }

    /// <summary>
    /// Refreshes the shared index once, then permits an explicit, human-owned
    /// passage selection. Any suggested retests are nonbinding and editable.
    /// The service revalidates every source hash at the final commit.
    /// </summary>
    /// <param name="targetedReplay">Whether to propose a last-passage rerun.</param>
    /// <param name="changedCriteria">Whether to use human CA baseline drift.</param>
    /// <returns>Completed user-approved passage creation.</returns>
    private async Task StartPassageAsync(bool targetedReplay,
        bool changedCriteria = false)
    {
        CampaignRow? chosen = SelectedCampaign();
        if (_readOnly || chosen is null ||
            targetedReplay && (chosen.Entry.Campaign.Passages.Count == 0 ||
                chosen.Entry.Campaign.State !=
                    ProjectTestCampaignState.InProgress) ||
            changedCriteria && chosen.Entry.Campaign.State is not
                (ProjectTestCampaignState.Draft or
                    ProjectTestCampaignState.InProgress))
        {
            return;
        }

        LinkIndexCatalog index = await Task.Run(
            () => _indexService.RefreshAsync());
        ProjectTestCandidate[] candidates = await Task.Run(
            () => FindAvailableTests(index, _workspaceRoot, _projectDirectory));
        if (candidates.Length == 0)
        {
            MessageBox.Show(this,
                "Aucun bloc TEST-/TST- identifié et unique dans les dossiers " +
                "Tests/Recette du projet. Identifiez d'abord les titres " +
                "du cahier avec la commande d'attribution de GUID.",
                "Tests à identifier",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        IReadOnlyCollection<Guid>? suggested = null;
        if (targetedReplay)
        {
            // A source that has become missing, modified or ambiguous is
            // suggested for HUMAN requalification, never declared Passed.
            ProjectTestSourceValidityOverview validity = await Task.Run(
                () => _service.AssessSourceValidityAsync(
                    _projectDirectory, chosen.Entry, index));
            Guid latestPassageId = chosen.Entry.Campaign.Passages[^1].Id;
            HashSet<Guid> sourceReviewIds = validity.Rows.Where(row =>
                row.PassageId == latestPassageId &&
                row.Validity != ProjectTestSourceValidity.Current)
                .Select(row => row.TestId).ToHashSet();
            ProjectTestRetestProposal proposal = await Task.Run(() =>
                ProjectTestRetestPlanner.Propose(
                    chosen.Entry.Campaign, sourceReviewIds));
            HashSet<Guid> available = candidates.Select(item => item.Id)
                .ToHashSet();
            Guid[] preselection = proposal.Candidates.Where(item =>
                available.Contains(item.TestId)).Select(item => item.TestId)
                .ToArray();
            int unavailable = proposal.Candidates.Count -
                preselection.Length;
            if (preselection.Length == 0)
            {
                MessageBox.Show(this,
                    "Aucun test de la reprise proposée n'est disponible " +
                    "avec une identité sûre. Utilisez la sélection manuelle " +
                    "après correction des sources/identifiants.\n\n" +
                    unavailable + " test(s) proposé(s) indisponible(s).",
                    "Reprise impossible sans test identifiable",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            string[] reasons = proposal.Candidates.Take(12)
                .Select(item => item.Reference + " : " +
                    string.Join(", ", item.Reasons))
                .ToArray();
            string summary = string.Join("\n", reasons);
            if (proposal.Candidates.Count > 12)
            {
                summary += "\n... et " +
                    (proposal.Candidates.Count - 12) + " autre(s).";
            }

            if (MessageBox.Show(this,
                "Proposition de reprise du dernier passage de " +
                    chosen.Entry.Campaign.Reference + " :\n\n" +
                summary + "\n\n" +
                preselection.Length + " test(s) disponibles en présélection ; " +
                unavailable + " test(s) indisponibles à examiner.\n\n" +
                "La présélection est modifiable : vous pouvez retirer ou " +
                "ajouter des tests. Aucun nouveau résultat n'est déduit.\n\n" +
                "Ouvrir le sélecteur pour examen ?",
                "Reprise ciblée · validation de la proposition",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question) != MessageBoxResult.Yes)
            {
                return;
            }

            suggested = preselection;
        }

        if (changedCriteria)
        {
            // This read-only projection uses one refreshed link index and
            // one same-project EXI registry snapshot. No per-test filesystem
            // scan or dispatcher-blocking Markdown parsing is introduced.
            ProjectCriterionRetestProposal proposal = await Task.Run(
                async () =>
                {
                    ProjectRequirementCatalog requirements =
                        await new ProjectRequirementRegistryService(
                            _workspaceRoot).ListAsync(_projectDirectory);
                    ProjectCriterionBaselineOverview baseline =
                        await new ProjectCriterionBaselineService(
                            _workspaceRoot).InspectAsync(_projectDirectory,
                                requirements);
                    ProjectRequirementTraceabilityOverview traceability =
                        ProjectRequirementTraceability.Build(
                            requirements, index);
                    ProjectCriterionSelectableTest[] selectable = candidates
                        .Select(item => new ProjectCriterionSelectableTest(
                            item.Id, item.Label, item.RelativePath)).ToArray();
                    return ProjectCriterionRetestPlanner.Propose(
                        baseline, traceability, selectable);
                });

            string warningSummary = string.Join("\n",
                proposal.Warnings.Take(7));
            if (proposal.Warnings.Count > 7)
            {
                warningSummary += "\n… " +
                    (proposal.Warnings.Count - 7) +
                    " autre(s) avertissement(s).";
            }

            if (proposal.Candidates.Count == 0)
            {
                MessageBox.Show(this,
                    "Aucune présélection sûre à partir des critères CA " +
                    "modifiés depuis la référence humaine.\n\n" +
                    warningSummary +
                    "\n\nVous pouvez toujours utiliser « Lancer un " +
                    "passage… » avec une sélection manuelle.",
                    "Reprise CA · aucun test présélectionnable",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            string[] reasons = proposal.Candidates.Take(12).Select(item =>
                item.Label + " — " +
                string.Join("; ", item.Reasons.Take(2))).ToArray();
            string details = string.Join("\n", reasons);
            if (proposal.Candidates.Count > 12)
            {
                details += "\n… " +
                    (proposal.Candidates.Count - 12) + " autre(s) test(s).";
            }

            if (MessageBox.Show(this,
                "Référence CA n°" + proposal.BaselineSequence +
                " · " + proposal.ChangedCriterionCount +
                " critère(s) modifié(s), ajouté(s), disparu(s) ou " +
                "incertain(s).\n\n" +
                proposal.Candidates.Count +
                " test(s) actuel(s) uniques proposés :\n" +
                details + "\n\n" +
                "LIMITES DE LA PROPOSITION :\n" + warningSummary +
                "\n\nLes anciens verdicts REC restent inchangés. " +
                "La présélection est entièrement modifiable et " +
                "nécessite une nouvelle confirmation humaine.\n\n" +
                "Ouvrir le sélecteur de tests ?",
                "Reprise ciblée depuis les critères CA",
                MessageBoxButton.YesNo, MessageBoxImage.Warning) !=
                MessageBoxResult.Yes)
            {
                return;
            }

            suggested = proposal.Candidates.Select(item => item.TestId)
                .ToArray();
        }

        // The user can navigate while the registry/index worker runs.
        // Do not display or commit a proposal for a campaign that is no
        // longer the selected one. Historical REC rows remain untouched.
        if (!ReferenceEquals(chosen, SelectedCampaign()))
        {
            StatusText.Text = "Campagne changée pendant l'analyse : " +
                "relancer la présélection des tests.";
            return;
        }

        ProjectTestPassageStartDialog picker = new(
            candidates, suggested)
        {
            Owner = this
        };
        if (picker.ShowDialog() != true)
        {
            return;
        }

        if (MessageBox.Show(this,
            "Figer le passage " +
            (chosen.Entry.Campaign.Passages.Count + 1) +
            " dans " + chosen.Entry.Campaign.Reference + " ?\n\n" +
            picker.SelectedTestIds.Count + " test(s)\n" +
            "Environnement : " + picker.SelectedEnvironment + "\n" +
            "Version : " + picker.SelectedVersion + "\n\n" +
            "La sélection et le contenu exact des tests seront conservés.",
            "Passage · confirmation humaine",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question) != MessageBoxResult.Yes)
        {
            return;
        }

        ProjectTestCampaignEntry saved = await Task.Run(() =>
            _service.StartPassageAsync(
                _projectDirectory, chosen.Entry, index,
                picker.SelectedTestIds, picker.SelectedEnvironment,
                picker.SelectedVersion, picker.SelectedActor));
        Changed = true;
        WorkspaceLinkIndexService.NotifyWorkspaceChanged(_workspaceRoot);
        await ReloadAsync(saved.Campaign.Id);
        StatusText.Text = changedCriteria
            ? "Passage REC figé depuis la proposition CA, aucun résultat " +
                "déduit. Utiliser « Suivi des reprises CA… » pour " +
                "constater les tests inclus et ceux sans résultat."
            : "Passage figé, aucun résultat encore déduit.";
    }

    /// <summary>
    /// Offers only globally unique indexed test blocks from this project,
    /// keeping source lists read-only; the service verifies again at write.
    /// </summary>
    /// <param name="index">Shared index snapshot.</param>
    /// <param name="root">Workspace path.</param>
    /// <param name="project">Owning project folder.</param>
    /// <returns>Safe displayed candidates, not an authoritative approval.</returns>
    private static ProjectTestCandidate[] FindAvailableTests(
        LinkIndexCatalog index, string root, string project)
    {
        string prefix = Path.GetRelativePath(root, project)
            .Replace('\\', '/') + "/";
        HashSet<Guid> duplicatedBlocks = index.Blocks.GroupBy(
            block => block.Id).Where(group => group.Count() > 1)
            .Select(group => group.Key).ToHashSet();
        Dictionary<Guid, LinkTargetEntry[]> owners = index.Targets
            .GroupBy(target => target.Id)
            .ToDictionary(group => group.Key, group => group.ToArray());
        List<ProjectTestCandidate> candidates = [];
        foreach (IndexedMarkdownBlock block in index.Blocks)
        {
            if (duplicatedBlocks.Contains(block.Id) ||
                owners.ContainsKey(block.Id) ||
                !owners.TryGetValue(block.DocumentId,
                    out LinkTargetEntry[]? sourceDocuments) ||
                sourceDocuments.Length != 1 ||
                sourceDocuments[0].Kind != LinkTargetKind.Document)
            {
                continue;
            }

            string path = sourceDocuments[0].RelativePath.Replace('\\', '/');
            if (!path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string local = path[prefix.Length..];
            string[] parts = local.Split('/');
            if (!parts.SkipLast(1).Any(segment =>
                    string.Equals(segment, "Tests",
                        StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(segment, "Recette",
                        StringComparison.OrdinalIgnoreCase)) ||
                parts.SkipLast(1).Any(segment =>
                    string.Equals(segment, "Campagnes",
                        StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            int end = block.HeadingTitle.IndexOfAny([' ', '\t', '—', '–', ':']);
            if (end < 0 ||
                !ProjectTestDefinitionCodec.IsReference(
                    block.HeadingTitle[..end]))
            {
                continue;
            }

            candidates.Add(new ProjectTestCandidate(
                block.Id, block.HeadingTitle, path));
        }

        return candidates.OrderBy(item => item.Label,
            StringComparer.CurrentCultureIgnoreCase).ToArray();
    }

    /// <summary>Appends one explicit result to only the latest passage.</summary>
    /// <param name="sender">Record result button.</param>
    /// <param name="e">Click.</param>
    private async void Record_Click(object sender, RoutedEventArgs e) =>
        await GuardAsync("Enregistrer le résultat", RecordResultAsync);

    /// <summary>Requires explicit human verdict and exact revision.</summary>
    /// <returns>Saved result or safe cancellation.</returns>
    private async Task RecordResultAsync()
    {
        CampaignRow? campaign = SelectedCampaign();
        PassageRow? passage = SelectedPassage();
        ResultRow? test = SelectedTest();
        if (_readOnly || campaign is null || passage is null ||
            test is null || test.Result.Outcome is not null ||
            campaign.Entry.Campaign.Passages.LastOrDefault()?.Id !=
                passage.Passage.Id)
        {
            return;
        }

        ProjectTestResultDialog editor = new(
            test.Result.Reference, passage.Passage.Number)
        {
            Owner = this
        };
        if (editor.ShowDialog() != true)
        {
            return;
        }

        if (MessageBox.Show(this,
            "Confirmer le résultat du test " + test.Result.Reference + " ?\n" +
            "Passage " + passage.Passage.Number + " · " +
            passage.Passage.Environment + " / " +
            passage.Passage.ApplicationVersion + "\n" +
            "Résultat : " + DescribeOutcome(editor.SelectedOutcome) + "\n" +
            "Exécutant : " + editor.SelectedActor + "\n" +
            "Justification : " + editor.SelectedRationale + "\n\n" +
            "Ce résultat ne pourra pas être remplacé dans ce passage.",
            "Résultat · confirmation humaine",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question) != MessageBoxResult.Yes)
        {
            return;
        }

        ProjectTestCampaignEntry saved = await Task.Run(() =>
            _service.RecordResultAsync(
                _projectDirectory, campaign.Entry, passage.Passage.Id,
                test.Result.TestId, editor.SelectedOutcome,
                editor.SelectedActor, editor.SelectedRationale,
                editor.SelectedEvidence));
        Changed = true;
        WorkspaceLinkIndexService.NotifyWorkspaceChanged(_workspaceRoot);
        await ReloadAsync(saved.Campaign.Id);
        StatusText.Text = "Résultat enregistré, historique du passage préservé.";
    }

    /// <summary>Initiates a human-approved qualification review.</summary>
    /// <param name="sender">Qualification button.</param>
    /// <param name="e">Button event.</param>
    private async void Qualification_Click(object sender, RoutedEventArgs e) =>
        await GuardAsync("Soumettre à qualification",
            () => DecideAsync(ProjectTestCampaignDecisionKind.EnterQualification));

    /// <summary>Requests a human verdict before closing a campaign.</summary>
    /// <param name="sender">Close campaign button.</param>
    /// <param name="e">Button event.</param>
    private async void CloseCampaign_Click(object sender, RoutedEventArgs e) =>
        await GuardAsync("Clore la campagne",
            () => DecideAsync(ProjectTestCampaignDecisionKind.Close));

    /// <summary>Reopens without deleting any prior decision or test result.</summary>
    /// <param name="sender">Reopen button.</param>
    /// <param name="e">Button event.</param>
    private async void ReopenCampaign_Click(object sender, RoutedEventArgs e) =>
        await GuardAsync("Réouvrir la campagne",
            () => DecideAsync(ProjectTestCampaignDecisionKind.Reopen));

    /// <summary>Explicit cancellation retains all decisions and passes.</summary>
    /// <param name="sender">Cancel campaign button.</param>
    /// <param name="e">Button event.</param>
    private async void CancelCampaign_Click(object sender, RoutedEventArgs e) =>
        await GuardAsync("Annuler la campagne",
            () => DecideAsync(ProjectTestCampaignDecisionKind.Cancel));

    /// <summary>
    /// Displays warnings but never silently blocks closing for open results.
    /// Requires an author, a rationale and a second explicit confirmation.
    /// </summary>
    /// <param name="kind">Human-requested REC lifecycle transition.</param>
    /// <returns>Completed user decision, or unchanged campaign.</returns>
    private async Task DecideAsync(ProjectTestCampaignDecisionKind kind)
    {
        CampaignRow? chosen = SelectedCampaign();
        if (_readOnly || chosen is null)
        {
            return;
        }

        ProjectTestCampaignState destination =
            ProjectTestCampaignDecisionPolicy.Transition(
                kind, chosen.Entry.Campaign.State);
        Guid? lastPassageId =
            chosen.Entry.Campaign.Passages.LastOrDefault()?.Id;
        int? sourceWarnings = _sourceValidity is null
            ? null
            : _sourceValidity.Rows.Count(item =>
                item.PassageId == lastPassageId &&
                item.Validity != ProjectTestSourceValidity.Current);
        ProjectTestQualificationSummary guidance =
            ProjectTestCampaignQualification.Review(
                chosen.Entry.Campaign, sourceWarnings);

        ProjectTestCampaignDecisionDialog dialog = new(
            kind, chosen.Entry.Campaign.Reference + " — " +
                chosen.Entry.Campaign.Title, guidance.Warnings)
        {
            Owner = this
        };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        string warnings = guidance.Warnings.Count == 0
            ? "Aucun avertissement."
            : string.Join("\n• ", guidance.Warnings);
        string verdict = dialog.SelectedVerdict?.ToString() ??
            "aucun (transition sans clôture)";
        string reservationText = dialog.SelectedReservations.Count == 0
            ? "aucune"
            : string.Join("; ", dialog.SelectedReservations);
        if (MessageBox.Show(this,
            "Confirmer la décision REC ?\n\n" +
            "Campagne : " + chosen.Entry.Campaign.Reference + "\n" +
            "État : " + chosen.Entry.Campaign.State + " → " +
                destination + "\n" +
            "Décideur : " + dialog.SelectedActor + "\n" +
            "Motif : " + dialog.SelectedRationale + "\n" +
            "Verdict : " + verdict + "\n" +
            "Réserves : " + reservationText + "\n\n" +
            "Points à examiner (non bloquants) :\n• " + warnings +
            "\n\nL'événement sera historisé sans changer les tests.",
            "Décision REC · confirmation définitive",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question) != MessageBoxResult.Yes)
        {
            return;
        }

        ProjectTestCampaignEntry saved = await Task.Run(() =>
            _service.RecordDecisionAsync(
                _projectDirectory, chosen.Entry, kind,
                dialog.SelectedActor, dialog.SelectedRationale,
                dialog.SelectedVerdict, dialog.SelectedReservations));
        Changed = true;
        WorkspaceLinkIndexService.NotifyWorkspaceChanged(_workspaceRoot);
        await ReloadAsync(saved.Campaign.Id);
        StatusText.Text =
            "Décision REC historisée : " + saved.Campaign.State +
            " · aucun verdict de test ni état EXI modifié.";
    }

    /// <summary>Opens the selected original Markdown REC file in Nodalis.</summary>
    /// <param name="sender">Open source button.</param>
    /// <param name="e">Click.</param>
    private void Open_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedCampaign() is not CampaignRow selected)
        {
            return;
        }

        SelectedCampaignPath = selected.Entry.RelativePath;
        DialogResult = true;
    }

    /// <summary>Closes without additional modifications.</summary>
    /// <param name="sender">Close button.</param>
    /// <param name="e">Click.</param>
    private void Close_Click(object sender, RoutedEventArgs e) =>
        DialogResult = false;

    private sealed record CampaignRow(
        ProjectTestCampaignEntry Entry, string Label, string Summary);

    private sealed record PassageRow(
        ProjectTestPassage Passage, string Label, string Detail);

    private sealed record ResultRow(
        ProjectTestPassageResultRow Result, string Label, string Summary,
        ProjectTestSourceValidityRow? Validity);
}
