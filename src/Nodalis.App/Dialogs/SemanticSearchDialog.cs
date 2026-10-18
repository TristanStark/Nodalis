using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Nodalis.Core.Links;
using Nodalis.Core.Semantic;
using Nodalis.Core.Settings;
using Nodalis.Infrastructure.AI;
using Nodalis.Infrastructure.Semantic;

namespace Nodalis.App.Dialogs;

/// <summary>Opt-in local semantic indexing and source-backed vector search.</summary>
public sealed class SemanticSearchDialog : Window
{
    private readonly SemanticWorkspaceSearchService _service;
    private readonly IReadOnlyList<SemanticSource> _documents;
    private readonly bool _enabled;
    private readonly TextBox _query = new() { MinHeight = 32, Margin = new Thickness(0, 5, 0, 9) };
    private readonly TextBox _scope = new() { MinHeight = 32, Margin = new Thickness(0, 5, 0, 9) };
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 8) };
    private readonly ListBox _results = new() { HorizontalContentAlignment = HorizontalAlignment.Stretch };
    private readonly Button _index = new() { Content = "Indexer ce périmètre", Margin = new Thickness(0, 0, 8, 0), MinHeight = 32 };
    private readonly Button _search = new() { Content = "Rechercher", Margin = new Thickness(0, 0, 8, 0), MinHeight = 32 };
    private readonly Button _cancel = new() { Content = "Annuler", Margin = new Thickness(0, 0, 8, 0), MinHeight = 32 };
    private CancellationTokenSource? _request;

    /// <summary>Opened result, or null if the user closed the dialog.</summary>
    public SemanticSearchResult? SelectedResult { get; private set; }

    /// <summary>Whether the user explicitly requested the existing exact/fuzzy search.</summary>
    public bool SwitchToClassicSearch { get; private set; }

    /// <summary>Creates a modal local search without running the provider on opening.</summary>
    /// <param name="workspaceRoot">Current workspace root.</param>
    /// <param name="preferences">Saved explicit opt-in settings.</param>
    /// <param name="catalog">Latest canonical document index.</param>
    public SemanticSearchDialog(string workspaceRoot, SemanticSearchPreferences preferences,
        LinkIndexCatalog catalog)
    {
        Title = "Recherche par le sens — locale (1.7)";
        Width = 800;
        Height = 640;
        MinWidth = 500;
        MinHeight = 400;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        _enabled = preferences.IsEnabled;
        SemanticChunkingOptions segmentation = new()
        {
            ExcludedPathPatterns = preferences.ExcludedPathPatterns
        };
        _service = new SemanticWorkspaceSearchService(workspaceRoot, preferences,
            new ProcessEmbeddingProvider(preferences), chunking: segmentation);
        _documents = catalog.Targets.Where(item =>
                item.Kind == LinkTargetKind.Document &&
                SemanticMarkdownChunker.IsEligiblePath(item.RelativePath, segmentation))
            .Select(item => new SemanticSource(item.Id, item.RelativePath))
            .DistinctBy(item => item.DocumentId).ToArray();

        DockPanel layout = new() { Margin = new Thickness(16) };
        StackPanel top = new();
        DockPanel.SetDock(top, Dock.Top);
        layout.Children.Add(top);
        top.Children.Add(new TextBlock
        {
            Text = _enabled
                ? "Modèle local : " + preferences.ModelId + " (" + preferences.Dimensions + " dimensions)"
                : "Désactivé : utilisez Ctrl+P → Configurer la recherche sémantique locale.",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 12)
        });
        top.Children.Add(new TextBlock { Text = "Requête" });
        top.Children.Add(_query);
        top.Children.Add(new TextBlock
        {
            Text = "Périmètre facultatif : dossier relatif (vide = workspace entier)",
            TextWrapping = TextWrapping.Wrap
        });
        top.Children.Add(_scope);

        WrapPanel actions = new() { Margin = new Thickness(0, 2, 0, 12) };
        _search.Click += SearchClicked;
        _index.Click += IndexClicked;
        _cancel.Click += (_, _) => _request?.Cancel();
        Button close = new() { Content = "Fermer", MinHeight = 32, IsCancel = true };
        close.Click += (_, _) => Close();
        actions.Children.Add(_search);
        actions.Children.Add(_index);
        Button classical = new()
        {
            Content = "Recherche exacte / fuzzy",
            MinHeight = 32,
            Margin = new Thickness(0, 0, 8, 0)
        };
        classical.Click += (_, _) =>
        {
            _request?.Cancel();
            SwitchToClassicSearch = true;
            DialogResult = true;
        };
        actions.Children.Add(classical);
        actions.Children.Add(_cancel);
        actions.Children.Add(close);
        top.Children.Add(actions);
        top.Children.Add(_status);
        layout.Children.Add(_results);
        Content = layout;
        _search.IsEnabled = _enabled;
        _index.IsEnabled = _enabled;
        _cancel.IsEnabled = false;
        _status.Text = "Cache local effaçable : " + _service.CachePath;
        _results.MouseDoubleClick += OpenSelected;
        _results.KeyDown += (sender, args) =>
        {
            if (args.Key == Key.Enter)
            {
                OpenSelected(sender, args);
                args.Handled = true;
            }
        };
        Closing += (_, _) => _request?.Cancel();
        Loaded += (_, _) => _query.Focus();
    }

    /// <summary>Indices only user-approved canonical documents in the selected scope.</summary>
    /// <param name="sender">Index button.</param>
    /// <param name="args">Click.</param>
    private async void IndexClicked(object sender, RoutedEventArgs args)
    {
        string? scope = GetScope();
        if (scope is null) return;
        SemanticSource[] selection = _documents.Where(source =>
            scope.Length == 0 ||
            source.RelativePath.Replace('\\', '/').StartsWith(scope + "/",
                StringComparison.OrdinalIgnoreCase)).ToArray();
        if (selection.Length == 0)
        {
            _status.Text = "Aucun document dans ce périmètre.";
            return;
        }

        if (MessageBox.Show(this,
            "Autoriser l'envoi de passages provenant de " + selection.Length +
            " fichiers Markdown au processus d'embeddings local de confiance ? " +
            "Le cache vectoriel sera conservé sur cet ordinateur ; aucune source ne sera modifiée.",
            "Confirmer l'indexation locale", MessageBoxButton.YesNo, MessageBoxImage.Question)
            != MessageBoxResult.Yes) return;

        CancellationToken token = Begin();
        try
        {
            Progress<SemanticIndexProgress> progress = new(info =>
                _status.Text = "Indexation : " + info.Completed + "/" + info.Total +
                    " fichiers · " + info.EmbeddedChunks + " nouveaux vecteurs");
            SemanticIndexSummary summary = await _service.IndexAsync(
                selection, true, progress, token);
            _status.Text = "Indexation terminée : " + summary.DocumentsChanged +
                " fichier(s) actualisé(s), " + summary.EmbeddedChunks + " vecteurs créés.";
        }
        catch (OperationCanceledException)
        {
            _status.Text = "Indexation interrompue (résultats précédents conservés).";
        }
        catch (Exception error) when (error is EmbeddingProviderException or
            InvalidDataException or IOException or UnauthorizedAccessException)
        {
            _status.Text = "Indexation indisponible : " + error.Message;
        }
        finally
        {
            End();
        }
    }

    /// <summary>Ranks real embeddings, then loads each hit's current source excerpt.</summary>
    /// <param name="sender">Search button.</param>
    /// <param name="args">Click.</param>
    private async void SearchClicked(object sender, RoutedEventArgs args)
    {
        string? scope = GetScope();
        if (scope is null) return;
        string query = _query.Text.Trim();
        if (query.Length == 0)
        {
            _status.Text = "Saisissez une requête.";
            return;
        }

        CancellationToken token = Begin();
        try
        {
            IReadOnlyList<SemanticSearchResult> hits = await _service.SearchAsync(
                query, true, scope.Length == 0 ? null : scope, token);
            _results.Items.Clear();
            foreach (SemanticSearchResult hit in hits)
            {
                StackPanel display = new();
                display.Children.Add(new TextBlock
                {
                    Text = hit.RelativePath + " · ligne " + hit.SourceLine +
                        (hit.Heading is null ? "" : " · " + hit.Heading) +
                        " · similarité " + hit.Similarity.ToString("0.000",
                            System.Globalization.CultureInfo.InvariantCulture),
                    FontWeight = FontWeights.SemiBold,
                    TextWrapping = TextWrapping.Wrap
                });
                display.Children.Add(new TextBlock
                {
                    Text = hit.Excerpt,
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 4, 0, 8)
                });
                _results.Items.Add(new ListBoxItem
                {
                    Content = display,
                    Tag = hit,
                    HorizontalContentAlignment = HorizontalAlignment.Stretch
                });
            }

            _status.Text = hits.Count == 0
                ? "Aucun passage valide trouvé : réindexez ou utilisez la recherche fuzzy/exacte."
                : hits.Count + " document(s) retrouvés par similarité (ce n'est pas une probabilité).";
        }
        catch (OperationCanceledException)
        {
            _status.Text = "Recherche annulée.";
        }
        catch (Exception error) when (error is EmbeddingProviderException or
            InvalidDataException or IOException or UnauthorizedAccessException)
        {
            _status.Text = "Modèle local indisponible : " + error.Message +
                " · La recherche exacte/fuzzy reste accessible.";
        }
        finally
        {
            End();
        }
    }

    /// <summary>Returns a validated relative folder prefix.</summary>
    /// <returns>Safe relative scope, or null on an unsafe value.</returns>
    private string? GetScope()
    {
        string value = _scope.Text.Trim().Replace('\\', '/').Trim('/');
        if (value.Contains(':') || value.Split('/').Any(part => part is "." or ".."))
        {
            _status.Text = "Le dossier doit être relatif au workspace.";
            return null;
        }

        return value;
    }

    /// <summary>Begins a cancellable user-initiated query or indexing action.</summary>
    /// <returns>Cancellation token for this operation.</returns>
    private CancellationToken Begin()
    {
        _request?.Dispose();
        _request = new CancellationTokenSource();
        _index.IsEnabled = false;
        _search.IsEnabled = false;
        _cancel.IsEnabled = true;
        return _request.Token;
    }

    /// <summary>Clears transient operation state after an async action.</summary>
    private void End()
    {
        _request?.Dispose();
        _request = null;
        _index.IsEnabled = _enabled;
        _search.IsEnabled = _enabled;
        _cancel.IsEnabled = false;
    }

    /// <summary>Closes the dialog only when an actual source result is selected.</summary>
    /// <param name="sender">ListBox or keyboard source.</param>
    /// <param name="args">Open gesture.</param>
    private void OpenSelected(object sender, RoutedEventArgs args)
    {
        if (_results.SelectedItem is ListBoxItem item &&
            item.Tag is SemanticSearchResult hit)
        {
            SelectedResult = hit;
            DialogResult = true;
        }
    }
}
