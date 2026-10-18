using System.Windows;
using Nodalis.App.Dialogs;
using Nodalis.Core.Settings;
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
        _preferences = _preferences with { SemanticSearch = dialog.Preferences };
        await _preferencesStore.SaveAsync(_preferences);
        StatusText.Text = dialog.Preferences.IsEnabled
            ? "Recherche sémantique : autorisation locale enregistrée. Aucune indexation lancée."
            : "Recherche sémantique désactivée ; recherche exacte/fuzzy inchangée.";
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
