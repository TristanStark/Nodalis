using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Nodalis.Core.Impact;
using Nodalis.Core.Links;

namespace Nodalis.App.Dialogs;

/// <summary>
/// Keyboard-accessible, read-only impact explorer backed by an immutable graph snapshot.
/// </summary>
public partial class ImpactAnalysisDialog : Window
{
    private const string AllEvidence = "Toutes les preuves";
    private const string ExplicitEvidence = "Relations explicites";
    private const string SuggestedEvidence = "Suggestions";
    private const string AllScopes = "Tous les niveaux";
    private readonly LinkTargetEntry _origin;
    private readonly ImpactGraphIndex _graph;
    private readonly IReadOnlyList<ImpactMaintenanceContext> _maintenance;
    private ImpactAnalysisResult? _analysis;
    private CancellationTokenSource? _analysisCancellation;

    /// <summary>
    /// Initializes the dialog with the selected document and an immutable in-memory graph.
    /// </summary>
    /// <param name="origin">The current indexed document.</param>
    /// <param name="graph">A reconstructed graph of the workspace.</param>
    public ImpactAnalysisDialog(LinkTargetEntry origin, ImpactGraphIndex graph,
        IReadOnlyList<ImpactMaintenanceContext>? maintenance = null)
    {
        ArgumentNullException.ThrowIfNull(origin);
        ArgumentNullException.ThrowIfNull(graph);
        _origin = origin;
        _graph = graph;
        _maintenance = maintenance ?? [];

        InitializeComponent();
        OriginText.Text = origin.QualifiedName;
        EvidenceComboBox.ItemsSource = new[] { AllEvidence, ExplicitEvidence, SuggestedEvidence };
        EvidenceComboBox.SelectedIndex = 0;
        ScopeComboBox.ItemsSource = new[] { AllScopes, "Projet", "Application", "Module", "Global" };
        ScopeComboBox.SelectedIndex = 0;
        DepthComboBox.ItemsSource = new[] { 1, 2, 3, 4, 6, 8 };
        DepthComboBox.SelectedItem = 6;
        GraphGroupComboBox.ItemsSource = new[] { "Par profondeur", "Par projet", "Par type" };
        GraphGroupComboBox.SelectedIndex = 0;
    }

    /// <summary>Gets the accepted navigation destination, if any.</summary>
    public LinkTargetEntry? SelectedTarget { get; private set; }

    /// <summary>Gets the Markdown line associated with the accepted destination, if available.</summary>
    public int? SelectedLineNumber { get; private set; }

    /// <summary>Gets the selected explicit maintenance reference to navigate to its source block.</summary>
    public ImpactMaintenanceContext? SelectedMaintenance { get; private set; }

    /// <summary>
    /// Starts the initial cancellable analysis after the dialog has been loaded.
    /// </summary>
    /// <param name="sender">Loaded window.</param>
    /// <param name="e">Routed event.</param>
    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        await RecalculateAsync();
        SearchTextBox.Focus();
    }

    /// <summary>
    /// Stops outstanding work before the window is destroyed.
    /// </summary>
    /// <param name="sender">Closing window.</param>
    /// <param name="e">Cancellation arguments.</param>
    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        _analysisCancellation?.Cancel();
    }

    /// <summary>
    /// Starts a new computation after a change of propagation limits or workspace boundaries.
    /// </summary>
    /// <param name="sender">Modified control.</param>
    /// <param name="e">Routed event.</param>
    private async void AnalysisSettings_Changed(object sender, RoutedEventArgs e)
    {
        if (IsLoaded)
        {
            await RecalculateAsync();
        }
    }

    /// <summary>
    /// Calculates without blocking WPF and ignores completed runs superseded by newer filters.
    /// </summary>
    /// <returns>Completion of this cancellable analysis iteration.</returns>
    private async Task RecalculateAsync()
    {
        _analysisCancellation?.Cancel();
        CancellationTokenSource cancellation = new();
        _analysisCancellation = cancellation;
        CancelAnalysisButton.IsEnabled = true;
        StatusText.Text = "Analyse locale en cours…";
        ResultsList.ItemsSource = null;
        PathList.ItemsSource = null;
        OpenFindingButton.IsEnabled = false;
        OpenStepButton.IsEnabled = false;
        ImpactAnalysisOptions options = new()
        {
            MaxDepth = DepthComboBox.SelectedItem is int selected ? selected : 6,
            MaxResults = 250,
            MaxVisitedNodes = 1500,
            IncludeSuggested = SuggestionsCheckBox.IsChecked == true,
            IncludeCrossScope = CrossScopeCheckBox.IsChecked == true
        };

        try
        {
            ImpactAnalysisResult result = await Task.Run(
                () => ImpactAnalyzer.Analyze(_graph, _origin.Id, options, cancellation.Token),
                cancellation.Token);
            if (!ReferenceEquals(_analysisCancellation, cancellation))
            {
                return;
            }

            _analysis = result;
            RefreshResults();
        }
        catch (OperationCanceledException)
        {
            if (ReferenceEquals(_analysisCancellation, cancellation))
            {
                StatusText.Text = "Analyse interrompue, aucune donnée modifiée.";
            }
        }
        catch (Exception exception)
        {
            if (ReferenceEquals(_analysisCancellation, cancellation))
            {
                StatusText.Text = $"Erreur de calcul : {exception.Message}";
            }
        }
        finally
        {
            if (ReferenceEquals(_analysisCancellation, cancellation))
            {
                CancelAnalysisButton.IsEnabled = false;
                _analysisCancellation = null;
            }

            cancellation.Dispose();
        }
    }

    /// <summary>
    /// Applies text, evidence and target-scope filters without changing the graph.
    /// </summary>
    /// <param name="sender">Filter control.</param>
    /// <param name="e">Routed event.</param>
    private void Filters_Changed(object sender, RoutedEventArgs e)
    {
        if (IsLoaded)
        {
            RefreshResults();
        }
    }

    /// <summary>
    /// Refreshes the selected result list from the last completed analysis.
    /// </summary>
    private void RefreshResults()
    {
        if (_analysis is null)
        {
            return;
        }

        string search = SearchTextBox.Text.Trim();
        string evidence = EvidenceComboBox.SelectedItem as string ?? AllEvidence;
        string scope = ScopeComboBox.SelectedItem as string ?? AllScopes;

        ImpactFindingRow[] rows = _analysis.Findings
            .Where(item =>
                string.IsNullOrWhiteSpace(search) ||
                item.Target.QualifiedName.Contains(search, StringComparison.CurrentCultureIgnoreCase) ||
                item.Path.Any(edge => edge.RelationType.Contains(search, StringComparison.CurrentCultureIgnoreCase)))
            .Where(item =>
                evidence == AllEvidence ||
                (evidence == ExplicitEvidence && item.Evidence == ImpactEvidence.Explicit) ||
                (evidence == SuggestedEvidence && item.Evidence == ImpactEvidence.Suggested))
            .Where(item =>
                scope == AllScopes ||
                string.Equals(ScopeLabel(item.Target), scope, StringComparison.OrdinalIgnoreCase))
            .Select(item => new ImpactFindingRow(
                item,
                item.Target.QualifiedName,
                $"{(item.Evidence == ImpactEvidence.Explicit ? "Relation explicite" : "Suggestion de contexte")} · " +
                $"niveau {item.Depth} · {ScopeLabel(item.Target)} · " +
                string.Join(", ", item.Path.Select(edge => edge.RelationType))))
            .ToArray();

        ResultsList.ItemsSource = rows;
        RenderGraph();
        RefreshMaintenanceReferences();
        if (rows.Length > 0)
        {
            ResultsList.SelectedIndex = 0;
        }
        else
        {
            ExplanationText.Text = "Aucun résultat pour ces filtres.";
            PathList.ItemsSource = null;
            OpenStepButton.IsEnabled = false;
            OpenFindingButton.IsEnabled = false;
        }

        StatusText.Text =
            $"{rows.Length} résultat(s) affiché(s), {_analysis.Findings.Count} résultat(s) calculé(s)" +
            (_analysis.IsTruncated ? " · ATTENTION : analyse tronquée par les limites." : ".") +
            (_analysis.Diagnostics.Count > 0
                ? $" · {_analysis.Diagnostics.Count} référence(s) cassée(s) ou non résolue(s) ignorée(s)."
                : string.Empty);
    }

    /// <summary>
    /// Rebuilds the readable proof and the keyboard-navigable steps for the selected finding.
    /// </summary>
    /// <param name="sender">Results list.</param>
    /// <param name="e">Selection change.</param>
    private void ResultsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        ImpactFindingRow? row = ResultsList.SelectedItem as ImpactFindingRow;
        OpenFindingButton.IsEnabled = row is not null;
        FocusGraphSelection();
        if (row is null)
        {
            PathList.ItemsSource = null;
            OpenStepButton.IsEnabled = false;
            return;
        }

        ImpactFinding finding = row.Finding;
        ExplanationText.Text = finding.Evidence == ImpactEvidence.Explicit
            ? "Chemin de relations typées : chaque étape dépend explicitement de la précédente, mais l'impact reste potentiel."
            : "Lien de contexte uniquement. Aucune dépendance métier ou propagation indirecte n'est affirmée.";

        List<ImpactPathStep> steps =
        [
            new ImpactPathStep(_origin, null, _origin.DisplayName, "Élément dont le changement est analysé")
        ];

        foreach (ImpactGraphEdge edge in finding.Path)
        {
            Guid stepId = finding.Evidence == ImpactEvidence.Explicit || edge.TargetId == _origin.Id
                ? edge.SourceId
                : edge.TargetId;
            if (!_graph.Targets.TryGetValue(stepId, out LinkTargetEntry? target))
            {
                continue;
            }

            string direction = finding.Evidence == ImpactEvidence.Explicit
                ? "Changement possible → dépendance"
                : "Association à examiner";
            steps.Add(new ImpactPathStep(
                target,
                stepId == edge.SourceId ? edge.LineNumber : null,
                target.DisplayName,
                $"{direction} · {edge.RelationType} · déclaration : " +
                $"{_graph.Targets[edge.SourceId].DisplayName}, ligne {edge.LineNumber}" +
                (string.IsNullOrWhiteSpace(edge.Excerpt) ? string.Empty : $" · {edge.Excerpt}")));
        }

        PathList.ItemsSource = steps;
        PathList.SelectedIndex = steps.Count - 1;
        OpenStepButton.IsEnabled = true;
    }

    /// <summary>Returns the stable workspace level used for the result filter.</summary>
    /// <param name="target">Indexed target.</param>
    /// <returns>Human-readable scope category.</returns>
    private static string ScopeLabel(LinkTargetEntry target)
    {
        string scope = target.ScopeIdentity ?? string.Empty;
        if (scope.StartsWith("project:", StringComparison.OrdinalIgnoreCase))
        {
            return "Projet";
        }

        if (scope.StartsWith("module:", StringComparison.OrdinalIgnoreCase))
        {
            return "Module";
        }

        if (scope.StartsWith("application:", StringComparison.OrdinalIgnoreCase))
        {
            return "Application";
        }

        return "Global";
    }

    /// <summary>
    /// Accepts the currently selected impact result for direct navigation.
    /// </summary>
    /// <param name="sender">Open button.</param>
    /// <param name="e">Routed event.</param>
    private void OpenFindingButton_Click(object sender, RoutedEventArgs e)
    {
        if (ResultsList.SelectedItem is not ImpactFindingRow row)
        {
            return;
        }

        SelectedTarget = row.Finding.Target;
        ImpactGraphEdge? last = row.Finding.Path.LastOrDefault();
        SelectedLineNumber = last is not null && last.SourceId == SelectedTarget.Id
            ? last.LineNumber
            : null;
        DialogResult = true;
    }

    /// <summary>
    /// Accepts a selected path step so any supporting document can be opened.
    /// </summary>
    /// <param name="sender">Path open button.</param>
    /// <param name="e">Routed event.</param>
    private void OpenStepButton_Click(object sender, RoutedEventArgs e)
    {
        if (PathList.SelectedItem is not ImpactPathStep step)
        {
            return;
        }

        SelectedTarget = step.Target;
        SelectedLineNumber = step.LineNumber;
        DialogResult = true;
    }

    /// <summary>
    /// Supports keyboard-only dialog navigation.
    /// </summary>
    /// <param name="sender">Window.</param>
    /// <param name="e">Key event.</param>
    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
        {
            return;
        }

        if (PathList.IsKeyboardFocusWithin)
        {
            OpenStepButton_Click(sender, e);
            e.Handled = true;
        }
        else if (ResultsList.IsKeyboardFocusWithin)
        {
            OpenFindingButton_Click(sender, e);
            e.Handled = true;
        }
    }

    /// <summary>Opens the double-clicked result.</summary>
    /// <param name="sender">List.</param>
    /// <param name="e">Mouse event.</param>
    private void ResultsList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        OpenFindingButton_Click(sender, e);
    }

    /// <summary>Opens the double-clicked proof step.</summary>
    /// <param name="sender">List.</param>
    /// <param name="e">Mouse event.</param>
    private void PathList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        OpenStepButton_Click(sender, e);
    }

    /// <summary>Signals cooperative cancellation without closing the dialog.</summary>
    /// <param name="sender">Cancel button.</param>
    /// <param name="e">Routed event.</param>
    private void CancelAnalysisButton_Click(object sender, RoutedEventArgs e)
    {
        _analysisCancellation?.Cancel();
    }

    /// <summary>Closes without navigation or workspace changes.</summary>
    /// <param name="sender">Close button.</param>
    /// <param name="e">Routed event.</param>
    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }

    private sealed record ImpactFindingRow(ImpactFinding Finding, string Title, string Subtitle);

    private sealed record ImpactPathStep(
        LinkTargetEntry Target, int? LineNumber, string Title, string Detail);
}
