using System.Windows;
using System.Windows.Controls;
using Nodalis.App.Reliability;
using Nodalis.Core.Projects;
using Nodalis.Infrastructure.Projects;

namespace Nodalis.App.Dialogs;

/// <summary>
/// Read-only MEP history viewer and deliberate one-file creation dialog.
/// Never starts a deployment or silently converts a gate into an approval.
/// </summary>
public partial class ProjectDeploymentRegistryDialog : Window
{
    private readonly ProjectDeploymentService _service;
    private readonly string _projectDirectory;
    private readonly bool _readOnly;
    private ProjectDeploymentCatalog? _catalog;
    private bool _busy;

    /// <summary>Creates a project-scoped deployment viewer.</summary>
    /// <param name="workspaceRoot">Current workspace.</param>
    /// <param name="projectDirectory">Explicit owner project.</param>
    /// <param name="readOnly">Archive or read-only workspace.</param>
    public ProjectDeploymentRegistryDialog(string workspaceRoot,
        string projectDirectory, bool readOnly)
    {
        _service = new ProjectDeploymentService(workspaceRoot);
        _projectDirectory = projectDirectory;
        _readOnly = readOnly;
        InitializeComponent();
        Loaded += async (_, _) => await GuardAsync(
            () => ReloadAsync(null), "Charger les opérations MEP");
    }

    /// <summary>True only if a user explicitly created a new source file.</summary>
    public bool Changed { get; private set; }

    /// <summary>Exact verified source selected to open in Nodalis.</summary>
    public string? SelectedSourcePath { get; private set; }

    /// <summary>Current stable MEP selection from the in-memory listing.</summary>
    /// <returns>Selected row or null.</returns>
    private MepRow? CurrentRow() => OperationsList.SelectedItem as MepRow;

    /// <summary>
    /// Reloads the bounded folder outside the WPF dispatcher and retains the
    /// same MEP GUID when possible; selecting/scrolling performs no disk I/O.
    /// </summary>
    /// <param name="selectedId">Optional ID to restore.</param>
    /// <returns>Completed refresh.</returns>
    private async Task ReloadAsync(Guid? selectedId)
    {
        ProjectDeploymentCatalog catalog = await Task.Run(() =>
            _service.ListAsync(_projectDirectory));
        _catalog = catalog;
        MepRow[] rows = catalog.Operations.Select(item =>
            new MepRow(item,
                item.Operation.Reference + " — " + item.Operation.Title,
                item.Operation.Attempts.Count + " tentative(s) · " +
                item.Operation.Events.Count + " événement(s)"))
            .ToArray();
        OperationsList.ItemsSource = rows;
        OperationsList.SelectedItem = rows.FirstOrDefault(item =>
            item.Entry.Operation.Id == selectedId) ?? rows.FirstOrDefault();
        DiagnosticsText.Text = catalog.Diagnostics.Count == 0
            ? "Aucun document ignoré."
            : catalog.Diagnostics.Count + " source(s) ignorée(s) : " +
              string.Join(" · ", catalog.Diagnostics.Take(3));
        StatusText.Text = _readOnly
            ? "Projet archivé / lecture seule · aucune création ni édition."
            : rows.Length + " opération(s) · référence suivante : " +
              (catalog.NextReference ?? "plus de références libres");
        PaintDetails();
        UpdateButtons();
    }

    /// <summary>Paints one already loaded event history without rescanning.</summary>
    private void PaintDetails()
    {
        if (CurrentRow() is not MepRow row)
        {
            DetailsBox.Text = "Aucune opération MEP sélectionnée.";
            return;
        }

        ProjectDeploymentOperation operation = row.Entry.Operation;
        List<string> lines =
        [
            operation.Reference + " — " + operation.Title,
            "Identité : " + operation.Id.ToString("D"),
            "Création : " + operation.CreatedUtc.ToLocalTime().ToString(
                "dd/MM/yyyy HH:mm"),
            "Source : " + row.Entry.RelativePath,
            "Révision SHA-256 : " + row.Entry.SourceSha256,
            string.Empty,
            "Les tentatives sont des déclarations humaines, pas des GO automatiques."
        ];
        foreach (ProjectDeploymentAttempt attempt in operation.Attempts)
        {
            lines.Add(string.Empty);
            lines.Add("TENTATIVE " + attempt.Number + " · " +
                attempt.StartedUtc.ToLocalTime().ToString("dd/MM/yyyy HH:mm"));
            lines.Add("Avant : " + attempt.ObservedVersionBefore +
                " · cible : " + attempt.TargetVersion);
            lines.Add("Environnement : " + attempt.Environment);
            lines.Add("Périmètre : " + attempt.Scope);
            lines.Add("Auteur : " + attempt.StartedBy);
            lines.Add("GO relu : " + attempt.GateDecisionId.ToString("D") +
                " · " + attempt.AuthorizationReviewedBy);
            lines.Add("Procédure : " + attempt.Procedure.RelativePath);
            lines.Add("Rollback : " + attempt.RollbackProcedure.RelativePath);
            ProjectDeploymentAttemptEvent[] history = operation.Events
                .Where(item => item.AttemptId == attempt.Id).ToArray();
            if (history.Length == 0)
            {
                lines.Add("  Aucun événement encore historisé.");
            }

            foreach (ProjectDeploymentAttemptEvent action in history)
            {
                string observation = action.ObservedVersionAfter is null
                    ? string.Empty
                    : " · version constatée : " + action.ObservedVersionAfter;
                lines.Add("  " + action.RecordedUtc.ToLocalTime().ToString(
                    "dd/MM/yyyy HH:mm") + " · " + action.Kind +
                    (action.RollbackOutcome is null
                        ? string.Empty : " · rollback " + action.RollbackOutcome) +
                    observation);
                lines.Add("    " + action.Actor + " : " + action.Rationale);
            }
        }

        DetailsBox.Text = string.Join(Environment.NewLine, lines);
    }

    /// <summary>Enables only meaningful controls on the current snapshot.</summary>
    private void UpdateButtons()
    {
        CreateButton.IsEnabled = !_busy && !_readOnly &&
            _catalog?.NextReference is not null;
        OpenButton.IsEnabled = !_busy && CurrentRow() is not null;
        RefreshButton.IsEnabled = !_busy;
    }

    /// <summary>Runs one WPF action inside the normal error guard.</summary>
    /// <param name="action">Async operation.</param>
    /// <param name="context">Human-readable operation label.</param>
    /// <returns>Completed UI action.</returns>
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
            await UiActionGuard.RunAsync(this, "MEP · " + context, action);
        }
        finally
        {
            _busy = false;
            UpdateButtons();
        }
    }

    /// <summary>Shows selected operation's existing Markdown in the editor.</summary>
    /// <param name="sender">Open source button.</param>
    /// <param name="e">WPF click event.</param>
    private void Open_Click(object sender, RoutedEventArgs e)
    {
        if (_busy || CurrentRow() is not MepRow row)
        {
            return;
        }

        SelectedSourcePath = row.Entry.RelativePath;
        DialogResult = true;
    }

    /// <summary>Opens a human-approved creation with no implicit attempts.</summary>
    /// <param name="sender">Create button.</param>
    /// <param name="e">WPF click event.</param>
    private async void Create_Click(object sender, RoutedEventArgs e) =>
        await GuardAsync(async () =>
        {
            if (_readOnly || _catalog?.NextReference is not string reference)
            {
                return;
            }

            TextPromptDialog prompt = new(
                "Créer une opération MEP", "Intitulé de la mise en production :")
            {
                Owner = this
            };
            if (prompt.ShowDialog() != true)
            {
                return;
            }

            string title = prompt.Value;
            if (MessageBox.Show(this,
                "Créer " + reference + " — " + title + " ?\n\n" +
                "Une seule fiche Markdown sera créée dans " +
                "Exploitation/Déploiement/Opérations. " +
                "Aucune tentative ni autorisation ne sera créée et " +
                "aucun déploiement ne sera exécuté.",
                "Nouvelle MEP · confirmer",
                MessageBoxButton.YesNo, MessageBoxImage.Question) !=
                MessageBoxResult.Yes)
            {
                return;
            }

            ProjectDeploymentEntry saved =
                await _service.CreateAsync(_projectDirectory, title, reference);
            Changed = true;
            await ReloadAsync(saved.Operation.Id);
        }, "Créer une opération MEP");

    /// <summary>Reloads the canonical folder only on explicit user request.</summary>
    /// <param name="sender">Refresh button.</param>
    /// <param name="e">WPF click event.</param>
    private async void Refresh_Click(object sender, RoutedEventArgs e) =>
        await GuardAsync(() => ReloadAsync(CurrentRow()?.Entry.Operation.Id),
            "Actualiser les fiches MEP");

    /// <summary>Closes the read-only viewer without creating anything.</summary>
    /// <param name="sender">Close button.</param>
    /// <param name="e">WPF click event.</param>
    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    /// <summary>Updates details from the already materialized local catalog.</summary>
    /// <param name="sender">Operations list.</param>
    /// <param name="e">WPF selection event.</param>
    private void OperationsList_SelectionChanged(
        object sender, SelectionChangedEventArgs e)
    {
        PaintDetails();
        UpdateButtons();
    }

    /// <summary>Display-only row grounded in one source-verified MEP.</summary>
    /// <param name="Entry">Canonical operation.</param>
    /// <param name="Name">Readable reference and title.</param>
    /// <param name="Summary">History counts.</param>
    private sealed record MepRow(
        ProjectDeploymentEntry Entry, string Name, string Summary);
}
