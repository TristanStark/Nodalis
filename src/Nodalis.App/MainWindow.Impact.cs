using System.IO;
using System.Windows;
using Nodalis.App.Dialogs;
using Nodalis.Core.Impact;
using Nodalis.Core.Links;
using Nodalis.Core.Maintenance;
using Nodalis.Core.Navigation;

namespace Nodalis.App;

public partial class MainWindow
{
    private readonly ImpactGraphSnapshotCache _impactGraphCache = new();
    /// <summary>
    /// Opens the impact analysis command for the current Markdown document.
    /// </summary>
    /// <param name="sender">Analysis button.</param>
    /// <param name="e">Routed event.</param>
    private async void ImpactAnalysisButton_Click(object sender, RoutedEventArgs e)
    {
        await ShowImpactAnalysisAsync();
    }

    /// <summary>
    /// Opens impact analysis for the active document without changing its contents.
    /// </summary>
    /// <returns>Completion of local analysis and optional navigation.</returns>
    private async Task ShowImpactAnalysisAsync()
    {
        await RunUiActionAsync("Impacts · analyse", async () =>
        {
            if (_selectedNode?.Kind != WorkspaceNodeKind.Document)
            {
                StatusText.Text = "Sélectionnez un document Markdown pour analyser ses impacts.";
                return;
            }

            LinkTargetEntry? origin = FindIndexedTarget(_selectedNode);
            if (origin is null)
            {
                StatusText.Text = "Le document actif n'a pas d'identifiant d'index utilisable.";
                return;
            }

            await OpenImpactAnalysisForTargetAsync(origin);
        });
    }

    /// <summary>
    /// Launches a read-only analysis for a stable target selected from an incident or document.
    /// </summary>
    /// <param name="origin">The exact GUID-identified document to analyze.</param>
    /// <returns>Completion of dialog and navigation.</returns>
    private async Task OpenImpactAnalysisForTargetAsync(LinkTargetEntry origin)
    {
        LinkIndexCatalog snapshot = _linkIndex;
        ImpactGraphIndex graph = await Task.Run(() => _impactGraphCache.GetOrBuild(snapshot));
        IReadOnlyList<ImpactMaintenanceContext> maintenance =
            await LoadImpactMaintenanceContextsAsync(snapshot);
        ImpactAnalysisDialog dialog = new(origin, graph, maintenance)
        {
            Owner = this
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        if (dialog.SelectedMaintenance is ImpactMaintenanceContext selectedMaintenance)
        {
            LinkTargetEntry? source = _linkIndex.Targets.FirstOrDefault(target =>
                target.Id == selectedMaintenance.SourceDocument.Id);
            if (source is not null)
            {
                await NavigateToLinkTargetAsync(source,
                    selectedMaintenance.Record.LineNumber);
            }

            return;
        }

        if (dialog.SelectedTarget is not null)
        {
            await NavigateToLinkTargetAsync(dialog.SelectedTarget,
                dialog.SelectedLineNumber);
        }
    }

    /// <summary>
    /// Reads only maintenance sheets that have typed relations in the current index.
    /// It uses already existing IDs, never rewriting Markdown or its identity registry.
    /// </summary>
    /// <param name="catalog">Current, read-only link index snapshot.</param>
    /// <returns>Explicit maintenance references and source locations.</returns>
    private async Task<IReadOnlyList<ImpactMaintenanceContext>>
        LoadImpactMaintenanceContextsAsync(LinkIndexCatalog catalog)
    {
        HashSet<Guid> sourceIds = catalog.Relations.Select(item => item.SourceId).ToHashSet();
        LinkTargetEntry[] candidates = catalog.Targets
            .Where(target => target.Kind == LinkTargetKind.Document &&
                sourceIds.Contains(target.Id) &&
                target.DisplayName.Equals("Fiche de Maintenance.md",
                    StringComparison.OrdinalIgnoreCase))
            .ToArray();
        List<ImpactMaintenanceContext> contexts = [];
        string workspace = Path.GetFullPath(_root.FullPath);
        string prefix = Path.TrimEndingDirectorySeparator(workspace) +
            Path.DirectorySeparatorChar;
        foreach (LinkTargetEntry source in candidates)
        {
            string path = Path.GetFullPath(Path.Combine(workspace,
                source.RelativePath.Replace('/', Path.DirectorySeparatorChar)));
            if (!path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ||
                !File.Exists(path))
            {
                continue;
            }

            try
            {
                string markdown = await File.ReadAllTextAsync(path);
                contexts.AddRange(ImpactMaintenanceQuery.Parse(source, markdown, catalog));
            }
            catch (Exception exception) when (exception is IOException or
                UnauthorizedAccessException)
            {
                StatusText.Text = "Fiche de maintenance non lisible : " + source.DisplayName +
                    " · " + exception.Message;
            }
        }

        return contexts;
    }

    /// <summary>
    /// Opens the stable document referenced by a chosen maintenance record,
    /// then analyzes that document. A record without an explicit usable link
    /// never becomes an invented graph origin.
    /// </summary>
    /// <param name="record">Selected maintenance incident or intervention.</param>
    /// <returns>A read-only impact analysis after explicit user target selection.</returns>
    private async Task AnalyzeMaintenanceRecordAsync(MaintenanceHistoryItem record)
    {
        if (!File.Exists(record.SourcePath))
        {
            MessageBox.Show(this, "Fiche de maintenance introuvable.",
                "Analyse d'impact", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        string markdown = await File.ReadAllTextAsync(record.SourcePath);
        IReadOnlyList<MaintenanceDocumentReference> parsed =
            MaintenanceRelationIndex.Parse(record.SourcePath, markdown, _linkIndex);
        IReadOnlyList<MaintenanceDocumentReference> links = record.Kind == MaintenanceRecordKind.Incident
            ? MaintenanceRelationIndex.ForIncident(record, parsed)
            : MaintenanceRelationIndex.ForRecord(record, parsed);
        if (links.Count == 0)
        {
            MessageBox.Show(this,
                "Aucun document relié explicitement à cet incident ou cette intervention. " +
                "Ajoutez une relation GUID avant de lancer une analyse.",
                "Analyse d'impact", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        MaintenanceReferencesDialog picker = new(links,
            "Choisissez le document à analyser · " + record.Summary)
        {
            Owner = this
        };
        if (picker.ShowDialog() != true ||
            picker.SelectedReference?.TargetId is not Guid targetId)
        {
            return;
        }

        LinkTargetEntry? target = _linkIndex.Targets.FirstOrDefault(item =>
            item.Id == targetId && item.Kind == LinkTargetKind.Document);
        if (target is null)
        {
            MessageBox.Show(this, "La cible GUID est absente du workspace actif.",
                "Analyse d'impact", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        await OpenImpactAnalysisForTargetAsync(target);
    }
}
