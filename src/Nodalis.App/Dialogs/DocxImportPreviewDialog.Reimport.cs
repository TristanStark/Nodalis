using System.IO;
using System.Windows;
using System.Windows.Controls;
using Nodalis.App.Reliability;
using Nodalis.Core.Importing;
using Nodalis.Infrastructure.Importing;

namespace Nodalis.App.Dialogs;

/// <summary>
/// Read-only DOCX reimport inspection. This intentionally does not expose a
/// commit button: file replacement and conflict resolution remain gated by
/// a separate explicitly approved transaction in #164.
/// </summary>
public partial class DocxImportPreviewDialog
{
    private DocxImportManifestStore _reimportStore = null!;
    private string _reimportWorkspaceRoot = string.Empty;
    private readonly Dictionary<Guid, int?> _reimportOverrideChoices = [];

    /// <summary>Initializes local-only reimport discovery without any disk writes.</summary>
    private void InitializeReimportControls(string workspaceRoot)
    {
        _reimportWorkspaceRoot = Path.GetFullPath(workspaceRoot);
        _reimportStore = new DocxImportManifestStore(workspaceRoot);
    }

    /// <summary>Discovers only matching historical sources in the selected project.</summary>
    private async void ReimportLoadHistory_Click(object sender, RoutedEventArgs e)
    {
        await UiActionGuard.RunAsync(this, "Réimport DOCX · historiques", async () =>
        {
            ReimportHistoryComboBox.ItemsSource = null;
            _reimportOverrideChoices.Clear();
            ReimportMatchComboBox.ItemsSource = null;
            ReimportFragmentsList.ItemsSource = null;
            ClearReimportText();

            if (CreateNewProjectCheckBox.IsChecked == true ||
                ProjectComboBox.SelectedItem is not DocxImportTargetOption selectedProject)
            {
                ReimportStatusText.Text =
                    "Choisissez un projet existant dans l'onglet Cible pour inspecter ses anciens imports.";
                return;
            }

            string projectDirectory = _reimportStore.ResolveWorkspaceRelativePath(
                selectedProject.RelativePath);
            string sourceHash = await DocxImportManifestStore.ComputeFileSha256Async(
                _preview.StagedImport.StagedCopyPath);
            IReadOnlyList<DocxImportManifestMatch> candidates =
                await _reimportStore.FindCandidatesAsync(
                    projectDirectory,
                    Path.GetFileName(_preview.StagedImport.OriginalSourcePath),
                    sourceHash);

            List<ReimportHistoryChoice> histories = candidates
                .Select(match => new ReimportHistoryChoice(
                    match.Manifest,
                    match.Manifest.ImportedAtUtc.ToLocalTime().ToString("g") +
                    " · " + match.Manifest.SourceFileName +
                    (match.ExactSourceHash ? " · SHA-256 identique"
                        : " · même nom, contenu différent")))
                .ToList();

            ReimportHistoryComboBox.ItemsSource = histories;
            if (histories.Count > 0)
            {
                ReimportHistoryComboBox.SelectedIndex = 0;
            }

            ReimportStatusText.Text = histories.Count == 0
                ? "Aucun ancien import correspondant par nom ou empreinte. Il n'est pas possible d'identifier automatiquement ce Word."
                : histories.Count + " historique(s) candidat(s). Choisissez explicitement lequel comparer ; " +
                  "une correspondance de nom ne garantit pas une source identique.";
        });
    }

    /// <summary>Discards previous decisions when choosing a different import history.</summary>
    private void ReimportHistory_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _reimportOverrideChoices.Clear();
        if (ReimportFragmentsList is not null)
        {
            ReimportFragmentsList.ItemsSource = null;
        }

        if (ReimportMatchComboBox is not null)
        {
            ReimportMatchComboBox.ItemsSource = null;
        }

        if (ReimportBaseBox is not null)
        {
            ClearReimportText();
        }
    }

    /// <summary>Builds a three-way inspection, showing no results if the project has changed.</summary>
    private async void ReimportCompare_Click(object sender, RoutedEventArgs e)
    {
        await UiActionGuard.RunAsync(this, "Réimport DOCX · comparer", async () =>
        {
            if (!TryGetReimportTarget(out string projectDirectory,
                    out ReimportHistoryChoice? history))
            {
                return;
            }

            await DisplayReimportReportAsync(projectDirectory, history!,
                _reimportOverrideChoices);
        });
    }

    /// <summary>
    /// Applies a session-only, one-to-one manual identity association.
    /// Validate before modifying the user's existing set of choices; an
    /// ambiguous or overlapping assignment never leaks into the report.
    /// </summary>
    private async void ReimportAssignSection_Click(object sender, RoutedEventArgs e)
    {
        await UiActionGuard.RunAsync(this, "Réimport DOCX · associer", async () =>
        {
            if (ReimportFragmentsList.SelectedItem is not ReimportFragmentChoice row ||
                ReimportMatchComboBox.SelectedItem is not ReimportSectionOption chosen ||
                !TryGetReimportTarget(out string projectDirectory,
                    out ReimportHistoryChoice? history))
            {
                ReimportStatusText.Text =
                    "Sélectionnez un fragment et choisissez explicitement une section Word correspondante.";
                return;
            }

            Dictionary<Guid, int?> trial = new Dictionary<Guid, int?>(_reimportOverrideChoices)
            {
                [row.Fragment.Association.FragmentId] = chosen.Index
            };

            await DisplayReimportReportAsync(projectDirectory, history!, trial,
                row.Fragment.Association.FragmentId);
            _reimportOverrideChoices.Clear();
            foreach (KeyValuePair<Guid, int?> pair in trial)
            {
                _reimportOverrideChoices.Add(pair.Key, pair.Value);
            }
        });
    }

    /// <summary>Resolves the chosen project and historical import without guessing identity.</summary>
    private bool TryGetReimportTarget(out string projectDirectory,
        out ReimportHistoryChoice? history)
    {
        projectDirectory = string.Empty;
        history = ReimportHistoryComboBox.SelectedItem as ReimportHistoryChoice;
        if (CreateNewProjectCheckBox.IsChecked == true ||
            ProjectComboBox.SelectedItem is not DocxImportTargetOption project ||
            history is null)
        {
            ReimportStatusText.Text =
                "Choisissez un projet existant et son historique DOCX avant de comparer.";
            return false;
        }

        projectDirectory = _reimportStore.ResolveWorkspaceRelativePath(
            project.RelativePath);
        return true;
    }

    /// <summary>
    /// Refreshes all three source snapshots using either the accepted or the
    /// tentative identity choices. The service validates stale and duplicate
    /// associations before updating the UI. There are no file writes.
    /// </summary>
    private async Task DisplayReimportReportAsync(
        string projectDirectory,
        ReimportHistoryChoice history,
        IReadOnlyDictionary<Guid, int?> choices,
        Guid? selectFragmentId = null)
    {
        DocxReimportReviewService service =
            new DocxReimportReviewService(_reimportWorkspaceRoot);
        DocxReimportSectionMatchOverride[] overrides = choices
            .Select(pair => new DocxReimportSectionMatchOverride
            {
                FragmentId = pair.Key,
                CurrentSectionIndex = pair.Value
            })
            .ToArray();
        DocxReimportReview review = await service.BuildWithOverridesAsync(
            projectDirectory, history.Manifest, _preview, overrides);

        List<ReimportFragmentChoice> rows = review.Fragments
            .Select(fragment => new ReimportFragmentChoice(fragment,
                fragment.Association.PreviousHeading +
                " → " + (fragment.Association.CurrentSectionIndex is int index
                    ? _preview.Sections.Single(section => section.Index == index).SourceHeading +
                      " (section " + index + ")"
                    : fragment.Association.Kind == DocxReimportAssociationKind.Ambiguous
                        ? "correspondance ambiguë"
                        : "absente du Word") +
                " · " + (fragment.Change?.Kind.ToString() ?? "appariement requis") +
                (fragment.RequiresExplicitResolution ? " · arbitrage" : "")))
            .ToList();

        ReimportFragmentsList.ItemsSource = rows;
        ReimportFragmentsList.SelectedItem = selectFragmentId is Guid selected
            ? rows.FirstOrDefault(row => row.Fragment.Association.FragmentId == selected)
            : rows.FirstOrDefault();

        ReimportStatusText.Text =
            rows.Count + " fragment(s) · " +
            review.Matching.AddedSectionIndexes.Count + " nouvelle(s) section(s) · " +
            review.Matching.UnresolvedNewSectionIndexes.Count +
            " section(s) sans correspondance sûre · " +
            (review.SourcePackageUnchanged ? "même paquet Word" : "Word modifié") +
            ". Correspondances manuelles : " + choices.Count +
            ". Lecture seule, aucun écrasement possible." +
            (review.Warnings.Count == 0 ? "" :
                "\nAttention : " + string.Join(" ; ", review.Warnings));
    }

    /// <summary>Displays the three actual snapshots of the selected historical fragment.</summary>
    private void ReimportFragment_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ReimportBaseBox is null || ReimportFragmentsList is null)
        {
            return;
        }

        if (ReimportFragmentsList.SelectedItem is not ReimportFragmentChoice selected)
        {
            ClearReimportText();
            return;
        }

        ReimportBaseBox.Text = selected.Fragment.BaseMarkdown;
        ReimportWordBox.Text = selected.Fragment.LatestWordMarkdown ??
            (selected.Fragment.Association.Kind == DocxReimportAssociationKind.Ambiguous
                ? "[Appariement non résolu : aucune section Word attribuée]"
                : "[Section absente du Word]");
        ReimportLocalBox.Text = selected.Fragment.CurrentNodalisMarkdown ??
            "[Fichier Markdown absent]";
        List<ReimportSectionOption> options =
        [
            new ReimportSectionOption(null, "Section supprimée du Word (à confirmer)")
        ];
        options.AddRange(_preview.Sections
            .OrderBy(section => section.Index)
            .Select(section => new ReimportSectionOption(
                section.Index,
                "Section " + section.Index + " · " + section.SourceHeading)));
        ReimportMatchComboBox.ItemsSource = options;
        ReimportMatchComboBox.SelectedItem = options.FirstOrDefault(option =>
            option.Index == selected.Fragment.Association.CurrentSectionIndex &&
            (option.Index is not null ||
             selected.Fragment.Association.Kind ==
                 DocxReimportAssociationKind.ConfirmedMissingFromWord));
    }

    /// <summary>Clears stale previews after switching projects or comparing another history.</summary>
    private void ClearReimportText()
    {
        ReimportBaseBox.Text = string.Empty;
        ReimportWordBox.Text = string.Empty;
        ReimportLocalBox.Text = string.Empty;
        ReimportMatchComboBox.ItemsSource = null;
    }

    /// <summary>One explicit historical import selection in the current project.</summary>
    private sealed record ReimportHistoryChoice(
        DocxImportManifest Manifest, string Label);

    /// <summary>One Word section or explicit source deletion offered for arbitration.</summary>
    private sealed record ReimportSectionOption(int? Index, string Label);

    /// <summary>One immutable row of the read-only three-way comparison.</summary>
    private sealed record ReimportFragmentChoice(
        DocxReimportReviewedFragment Fragment, string Label);
}
