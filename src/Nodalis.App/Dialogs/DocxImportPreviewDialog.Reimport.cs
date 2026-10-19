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

    /// <summary>Builds a three-way inspection, showing no results if the project has changed.</summary>
    private async void ReimportCompare_Click(object sender, RoutedEventArgs e)
    {
        await UiActionGuard.RunAsync(this, "Réimport DOCX · comparer", async () =>
        {
            ReimportFragmentsList.ItemsSource = null;
            ClearReimportText();

            if (CreateNewProjectCheckBox.IsChecked == true ||
                ProjectComboBox.SelectedItem is not DocxImportTargetOption selectedProject ||
                ReimportHistoryComboBox.SelectedItem is not ReimportHistoryChoice history)
            {
                ReimportStatusText.Text =
                    "Choisissez un projet existant et un historique DOCX avant de comparer.";
                return;
            }

            string projectDirectory = _reimportStore.ResolveWorkspaceRelativePath(
                selectedProject.RelativePath);
            DocxReimportReviewService reviewService =
                new DocxReimportReviewService(_reimportWorkspaceRoot);
            DocxReimportReview review = await reviewService.BuildAsync(
                projectDirectory, history.Manifest, _preview);

            List<ReimportFragmentChoice> rows = review.Fragments
                .Select(fragment =>
                    new ReimportFragmentChoice(fragment,
                        fragment.Association.PreviousHeading +
                        " → " + (fragment.Association.CurrentSectionIndex is int sectionIndex
                            ? _preview.Sections.Single(section =>
                                section.Index == sectionIndex).SourceHeading
                            : fragment.Association.Kind == DocxReimportAssociationKind.Ambiguous
                                ? "correspondance ambiguë"
                                : "absente du Word") +
                        " · " + (fragment.Change?.Kind.ToString() ?? "appariement requis") +
                        (fragment.RequiresExplicitResolution ? " · arbitrage" : "")))
                .ToList();

            ReimportFragmentsList.ItemsSource = rows;
            if (rows.Count > 0)
            {
                ReimportFragmentsList.SelectedIndex = 0;
            }

            ReimportStatusText.Text =
                rows.Count + " fragment(s) · " +
                review.Matching.AddedSectionIndexes.Count + " nouvelle(s) section(s) · " +
                review.Matching.UnresolvedNewSectionIndexes.Count +
                " section(s) sans correspondance sûre · " +
                (review.SourcePackageUnchanged
                    ? "même paquet Word" : "Word modifié") +
                ". Lecture seule, aucun écrasement possible." +
                (review.Warnings.Count == 0 ? "" :
                    "\nAttention : " + string.Join(" ; ", review.Warnings));
        });
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
    }

    /// <summary>Clears stale previews after switching projects or comparing another history.</summary>
    private void ClearReimportText()
    {
        ReimportBaseBox.Text = string.Empty;
        ReimportWordBox.Text = string.Empty;
        ReimportLocalBox.Text = string.Empty;
    }

    /// <summary>One explicit historical import selection in the current project.</summary>
    private sealed record ReimportHistoryChoice(
        DocxImportManifest Manifest, string Label);

    /// <summary>One immutable row of the read-only three-way comparison.</summary>
    private sealed record ReimportFragmentChoice(
        DocxReimportReviewedFragment Fragment, string Label);
}
