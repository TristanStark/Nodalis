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
    }

    /// <summary>Shows the passages of the selected campaign in memory.</summary>
    /// <returns>Completed selection.</returns>
    private async Task PaintCampaignAsync()
    {
        _passageViewRevision++;
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

        ResultRow[] rows = results.Select(result =>
            new ResultRow(result,
                result.Reference + " · " +
                (result.Outcome is ProjectTestOutcome outcome
                    ? DescribeOutcome(outcome) : "NON EXÉCUTÉ"),
                result.Environment + " / " + result.ApplicationVersion +
                " · empreinte " + result.FrozenSha256[..12]))
            .ToArray();
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
            DetailsBox.Text = "Aucun passage sélectionné.";
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
                "Résultat : " + (outcome is null
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
        RecordButton.IsEnabled = writable && campaign is not null &&
            campaign.Entry.Campaign.State ==
                ProjectTestCampaignState.InProgress &&
            passage is not null && result is not null &&
            result.Result.Outcome is null &&
            campaign.Entry.Campaign.Passages.LastOrDefault()?.Id ==
                passage.Passage.Id;
        OpenButton.IsEnabled = campaign is not null;
        RefreshButton.IsEnabled = !_busy;
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
        await GuardAsync("Démarrer un passage", StartPassageAsync);

    /// <summary>
    /// Refreshes the shared index once, then permits an explicit, human-owned
    /// passage selection. The service revalidates all source hashes at commit.
    /// </summary>
    /// <returns>Completed passage creation.</returns>
    private async Task StartPassageAsync()
    {
        CampaignRow? chosen = SelectedCampaign();
        if (_readOnly || chosen is null)
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

        ProjectTestPassageStartDialog picker = new(candidates)
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
        StatusText.Text = "Passage figé, aucun résultat encore déduit.";
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
        ProjectTestPassageResultRow Result, string Label, string Summary);
}
