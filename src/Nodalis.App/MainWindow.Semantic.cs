using System.Windows;
using Nodalis.App.Dialogs;
using Nodalis.Core.Settings;
using Nodalis.Core.Links;
using Nodalis.Core.Semantic;
using Nodalis.Infrastructure.Semantic;

namespace Nodalis.App;

/// <summary>Optional semantic-search configuration independent from text-generation AI.</summary>
public partial class MainWindow
{
    /// <summary>Opens explicit local settings without running an embedding provider.</summary>
    /// <returns>Completed persistence of user-approved local-only options.</returns>
    private async Task ConfigureSemanticSearchAsync()
    {
        SemanticVectorCacheService cache = new(_root.FullPath);
        SemanticSettingsDialog dialog = new(_preferences.SemanticSearch, cache.CachePath)
        {
            Owner = this
        };
        if (dialog.ShowDialog() != true || dialog.Preferences is null) return;

        SemanticSearchPreferences previous = _preferences.SemanticSearch;
        SemanticSearchPreferences chosen = dialog.Preferences;
        bool changedScope = !previous.ExcludedPathPatterns
            .Order(StringComparer.OrdinalIgnoreCase)
            .SequenceEqual(chosen.ExcludedPathPatterns.Order(
                StringComparer.OrdinalIgnoreCase),
                StringComparer.OrdinalIgnoreCase);
        bool invalidatesIndex = !chosen.IsEnabled ||
            changedScope ||
            previous.Dimensions != chosen.Dimensions ||
            !string.Equals(previous.ModelId, chosen.ModelId, StringComparison.Ordinal);
        _preferences = _preferences with { SemanticSearch = chosen };
        await _preferencesStore.SaveAsync(_preferences);
        if (invalidatesIndex)
        {
            // A removed consent, newly excluded file or changed model must not
            // leave an old derived index silently readable from local disk.
            await cache.PurgeAsync();
        }

        StatusText.Text = chosen.IsEnabled
            ? "Recherche sémantique : paramètres locaux enregistrés" +
                (invalidatesIndex ? ", ancien cache effacé." : ", cache conservé.")
            : "Recherche sémantique désactivée et cache effacé ; fuzzy/exacte inchangée.";
    }

    /// <summary>Searches an approved local vector index and opens a canonical source passage.</summary>
    /// <returns>Selected document navigation, or a cancelled dialog.</returns>
    private async Task OpenSemanticSearchAsync()
    {
        LinkIndexCatalog catalog = await _linkIndexService.LoadAsync();
        SemanticSearchDialog dialog = new(_root.FullPath, _preferences.SemanticSearch, catalog)
        {
            Owner = this
        };
        if (dialog.ShowDialog() != true) return;
        if (dialog.SwitchToClassicSearch)
        {
            await SearchAsync();
            return;
        }

        if (dialog.SelectedResult is not SemanticSearchResult result) return;

        LinkTargetEntry? target = catalog.Targets.FirstOrDefault(item =>
            item.Kind == LinkTargetKind.Document && item.Id == result.DocumentId);
        if (target is null)
        {
            StatusText.Text = "Source sémantique introuvable : rafraîchissez les liens.";
            return;
        }

        await NavigateToLinkTargetAsync(target, result.SourceLine);
    }

    /// <summary>Removes the disposable embedding cache, never the Markdown documents.</summary>
    /// <returns>Completed user-confirmed vector deletion.</returns>
    private async Task PurgeSemanticIndexAsync()
    {
        MessageBoxResult answer = MessageBox.Show(this,
            "Supprimer les embeddings dérivés de ce workspace ? Les fichiers Markdown resteront intacts.",
            "Purger l'index sémantique", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (answer != MessageBoxResult.Yes) return;
        SemanticVectorCacheService cache = new(_root.FullPath);
        await cache.PurgeAsync();
        StatusText.Text = "Cache sémantique effacé ; documents originaux préservés.";
    }
}
