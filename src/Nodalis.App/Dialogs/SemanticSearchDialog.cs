using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Nodalis.Core.Links;
using Nodalis.Core.Search;
using Nodalis.Core.Semantic;
using Nodalis.Core.Settings;
using Nodalis.Infrastructure.AI;
using Nodalis.Infrastructure.Semantic;
using Nodalis.Infrastructure.Search;

namespace Nodalis.App.Dialogs;

/// <summary>Opt-in local semantic indexing and source-backed vector search.</summary>
public sealed class SemanticSearchDialog : Window
{
    private readonly SemanticWorkspaceSearchService _service;
    private readonly WorkspaceSearchService _lexicalService = new();
    private readonly IReadOnlyList<SemanticSource> _documents;
    private readonly IReadOnlyList<SemanticSource> _lexicalDocuments;
    private readonly string _workspaceRoot;
    private readonly bool _enabled;
    private readonly ComboBox _mode = new() { MinHeight = 32, Margin = new Thickness(0, 5, 0, 9) };
    private readonly TextBox _query = new() { MinHeight = 32, Margin = new Thickness(0, 5, 0, 9) };
    private readonly TextBox _scope = new() { MinHeight = 32, Margin = new Thickness(0, 5, 0, 9) };
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 8) };
    private readonly ListBox _results = new() { HorizontalContentAlignment = HorizontalAlignment.Stretch };
    private readonly Button _index = new() { Content = "Indexer / reprendre ce périmètre", Margin = new Thickness(0, 0, 8, 0), MinHeight = 32 };
    private readonly Button _search = new() { Content = "Rechercher", Margin = new Thickness(0, 0, 8, 0), MinHeight = 32 };
    private readonly Button _cancel = new() { Content = "Pause / Annuler", Margin = new Thickness(0, 0, 8, 0), MinHeight = 32 };
    private readonly Button _inspect = new() { Content = "Afficher l'état", Margin = new Thickness(0, 0, 8, 0), MinHeight = 32 };
    private readonly Button _purge = new() { Content = "Purger les vecteurs…", MinHeight = 32 };
    private readonly TextBlock _indexInfo = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 8) };
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
        Title = "Recherche hybride — locale (1.8)";
        Width = 800;
        Height = 640;
        MinWidth = 500;
        MinHeight = 400;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        _enabled = preferences.IsEnabled;
        _workspaceRoot = Path.GetFullPath(workspaceRoot);
        _mode.Items.Add("Mixte : exacte/fuzzy + sémantique");
        _mode.Items.Add("Texte seul : exacte/fuzzy (sans modèle)");
        _mode.Items.Add("Sens seul : embeddings locaux");
        _mode.SelectedIndex = _enabled ? 0 : 1;
        SemanticChunkingOptions segmentation = new()
        {
            ExcludedPathPatterns = preferences.ExcludedPathPatterns
        };
        _service = new SemanticWorkspaceSearchService(workspaceRoot, preferences,
            new ProcessEmbeddingProvider(preferences), chunking: segmentation);
        _lexicalDocuments = catalog.Targets.Where(item =>
                item.Kind == LinkTargetKind.Document)
            .Select(item => new SemanticSource(item.Id, item.RelativePath))
            .DistinctBy(item => item.DocumentId).ToArray();
        _documents = _lexicalDocuments.Where(item =>
                SemanticMarkdownChunker.IsEligiblePath(item.RelativePath, segmentation))
            .ToArray();

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
        top.Children.Add(new TextBlock { Text = "Mode de classement" });
        top.Children.Add(_mode);
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
        Expander indexPanel = new()
        {
            Header = "État et gestion de l'index local",
            IsExpanded = false,
            Margin = new Thickness(0, 0, 0, 8)
        };
        StackPanel manager = new();
        WrapPanel managerActions = new() { Margin = new Thickness(0, 6, 0, 6) };
        _inspect.Click += InspectClicked;
        _purge.Click += PurgeClicked;
        managerActions.Children.Add(_inspect);
        managerActions.Children.Add(_purge);
        manager.Children.Add(managerActions);
        manager.Children.Add(_indexInfo);
        indexPanel.Content = manager;
        top.Children.Add(indexPanel);
        top.Children.Add(_status);
        layout.Children.Add(_results);
        Content = layout;
        _search.IsEnabled = true;
        _index.IsEnabled = _enabled;
        _cancel.IsEnabled = false;
        _status.Text = "Mode texte sans modèle disponible · cache sémantique dérivé et effaçable.";
        _indexInfo.Text = "Fichiers éligibles pour l'indexation locale : " + _documents.Count +
            ". L'état est lu uniquement sur demande, sans démarrer le modèle.";
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
            // Catalog reconciliation uses ALL eligible workspace identities.
            // The user-selected folder remains the only embedding scope.
            int removed = await _service.PruneRemovedDocumentsAsync(
                _documents.Select(source => source.DocumentId).ToHashSet(), true, token);
            SemanticIndexSummary summary = await _service.IndexAsync(
                selection, true, progress, token);
            _status.Text = "Indexation terminée : " + summary.DocumentsChanged +
                " fichier(s) actualisé(s), " + summary.EmbeddedChunks +
                " vecteurs créés, " + removed +
                " fragments de documents supprimés nettoyés. Relancer reprend les fragments inchangés.";
            await RefreshIndexStatusAsync(token);
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

    /// <summary>Shows derived cache size, coverage and compatibility without a model call.</summary>
    /// <param name="sender">Inspection button.</param>
    /// <param name="args">Click gesture.</param>
    private async void InspectClicked(object sender, RoutedEventArgs args)
    {
        _inspect.IsEnabled = false;
        try
        {
            await RefreshIndexStatusAsync(CancellationToken.None);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or
            InvalidDataException)
        {
            _indexInfo.Text = "État de l'index indisponible : " + error.Message;
        }
        finally
        {
            _inspect.IsEnabled = true;
        }
    }

    /// <summary>Permanently purges only derived vectors after user confirmation.</summary>
    /// <param name="sender">Purge button.</param>
    /// <param name="args">Click gesture.</param>
    private async void PurgeClicked(object sender, RoutedEventArgs args)
    {
        if (MessageBox.Show(this,
            "Purger définitivement les embeddings du workspace courant ? " +
            "Les notes, projets et sources Markdown ne seront pas modifiés. " +
            "Une indexation future exigera un nouveau consentement.",
            "Confirmer la purge locale", MessageBoxButton.YesNo, MessageBoxImage.Warning)
            != MessageBoxResult.Yes) return;

        _purge.IsEnabled = false;
        _inspect.IsEnabled = false;
        try
        {
            await _service.PurgeAsync();
            _results.Items.Clear();
            _status.Text = "Vecteurs supprimés. Le mode texte reste disponible sans modèle.";
            await RefreshIndexStatusAsync(CancellationToken.None);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            _indexInfo.Text = "Échec de la purge locale : " + error.Message;
        }
        finally
        {
            _inspect.IsEnabled = true;
            _purge.IsEnabled = true;
        }
    }

    /// <summary>Reads validated cache metadata and scope coverage without indexing.</summary>
    /// <param name="token">Cancellation of the local read.</param>
    /// <returns>Completed status display.</returns>
    private async Task RefreshIndexStatusAsync(CancellationToken token)
    {
        SemanticIndexStatus status = await _service.InspectAsync(token);
        string state = !status.FileExists ? "Absent (aucun vecteur)" :
            !status.IsCompatible ? "Incompatible ou corrompu : réindexation nécessaire" :
            "Valide";
        string date = status.UpdatedAtUtc?.ToLocalTime().ToString("g") ?? "aucune";
        string[] visible = status.IndexedPaths.Take(5).ToArray();
        string sample = visible.Length == 0 ? "" :
            " · Exemples : " + string.Join(", ", visible) +
            (status.IndexedPaths.Count > visible.Length ? "…" : "");
        _indexInfo.Text = "État : " + state +
            " · Modèle : " + (string.IsNullOrWhiteSpace(status.ModelId) ? "non configuré" : status.ModelId) +
            " (" + status.Dimensions + " dim.)" +
            " · Documents : " + status.DocumentCount +
            " · Passages : " + status.ChunkCount + "/10 000" +
            " · Cache disque : " + (status.OnDiskBytes / 1024.0).ToString("0.0") + " Ko" +
            " · RAM vecteurs estimée : " + (status.VectorBytes / 1024.0).ToString("0.0") + " Ko" +
            " · Dernière modification : " + date +
            " · Documents éligibles : " + _documents.Count +
            " · Chemin : " + status.CachePath + sample;
    }

    /// <summary>
    /// Searches locally in lexical, semantic or combined mode; an unavailable
    /// embeddings provider never blocks the useful lexical fallback.
    /// </summary>
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

        int mode = _mode.SelectedIndex;
        if (!_enabled && mode != 1)
        {
            _status.Text = "Embeddings désactivés : choisissez Texte seul, ou activez le fournisseur dans Ctrl+P.";
            return;
        }

        CancellationToken token = Begin();
        try
        {
            IReadOnlyList<HybridSearchCandidate> lexical = mode == 2
                ? [] : await GetLexicalCandidatesAsync(query, scope, token);
            IReadOnlyList<SemanticSearchResult> vector = [];
            bool providerUnavailable = false;
            string semanticQuery = WorkspaceSearchService.GetFreeTextQuery(query);
            if (mode != 1 && semanticQuery.Length > 0)
            {
                try
                {
                    vector = await _service.SearchAsync(semanticQuery, true,
                        scope.Length == 0 ? null : scope, token);
                    if (WorkspaceSearchService.HasStructuredFilters(query))
                    {
                        // Reuse the original engine's exact front matter, project
                        // and modification-date predicates before displaying vectors.
                        IReadOnlySet<string> permitted =
                            await _lexicalService.GetFilteredRelativePathsAsync(
                                _workspaceRoot, query, token);
                        vector = vector.Where(hit => permitted.Contains(
                            hit.RelativePath.Replace('\\', '/'))).ToArray();
                    }
                }
                catch (Exception error) when (mode == 0 && error is
                    EmbeddingProviderException or InvalidDataException or
                    IOException or UnauthorizedAccessException)
                {
                    // Hybrid search still has genuine legacy lexical results.
                    // Never fabricate semantic hits or persist an error payload.
                    providerUnavailable = true;
                }
            }

            HybridSearchCandidate[] semantic = vector.Select(hit =>
                new HybridSearchCandidate(hit.DocumentId, hit.RelativePath,
                    hit.SourceLine, hit.Heading, hit.Excerpt,
                    Similarity: hit.Similarity)).ToArray();
            IReadOnlyList<HybridRankedHit> hits = HybridSearchRanker.Fuse(lexical, semantic);
            _results.Items.Clear();
            foreach (HybridRankedHit hit in hits)
            {
                HybridSearchCandidate candidate = hit.Candidate;
                string explanation = hit.MatchSource switch
                {
                    HybridMatchSource.Combined => "mixte (lexical + sémantique)",
                    HybridMatchSource.Semantic => "sémantique",
                    _ => "lexical"
                };
                string score = hit.Similarity is double similarity
                    ? " · similarité cosinus " + similarity.ToString("0.000",
                        System.Globalization.CultureInfo.InvariantCulture)
                    : "";
                StackPanel display = new();
                display.Children.Add(new TextBlock
                {
                    Text = candidate.RelativePath + " · ligne " + candidate.SourceLine +
                        (candidate.Heading is null ? "" : " · " + candidate.Heading) +
                        " · " + explanation + score,
                    FontWeight = FontWeights.SemiBold,
                    TextWrapping = TextWrapping.Wrap
                });
                display.Children.Add(new TextBlock
                {
                    Text = candidate.Excerpt,
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 4, 0, 8)
                });
                _results.Items.Add(new ListBoxItem
                {
                    Content = display,
                    Tag = new SemanticSearchResult(candidate.DocumentId,
                        candidate.RelativePath, candidate.Heading, candidate.SourceLine,
                        candidate.Excerpt, hit.Similarity ?? 0),
                    HorizontalContentAlignment = HorizontalAlignment.Stretch
                });
            }

            _status.Text = providerUnavailable
                ? "Fournisseur local indisponible : résultats exacts/fuzzy affichés sans IA."
                : hits.Count == 0
                    ? "Aucune correspondance. Essayez un autre terme ou réindexez les documents."
                    : hits.Count + " document(s) · RRF = fusion des rangs, pas une probabilité.";
        }
        catch (OperationCanceledException)
        {
            _status.Text = "Recherche annulée.";
        }
        catch (Exception error) when (error is EmbeddingProviderException or
            InvalidDataException or IOException or UnauthorizedAccessException)
        {
            _status.Text = "Recherche indisponible : " + error.Message +
                " · Essayez le mode Texte seul.";
        }
        finally
        {
            End();
        }
    }

    /// <summary>
    /// Resolves real legacy search occurrences to canonical GUID identities;
    /// path scope and exclusions are enforced before merging the two rankings.
    /// </summary>
    /// <param name="query">User query submitted to the original fuzzy engine.</param>
    /// <param name="scope">Validated workspace-relative directory prefix.</param>
    /// <param name="token">Cancellation token.</param>
    /// <returns>Best-first GUID-backed lexical candidates.</returns>
    private async Task<IReadOnlyList<HybridSearchCandidate>> GetLexicalCandidatesAsync(
        string query, string scope, CancellationToken token)
    {
        SearchResultSet results = await _lexicalService.SearchAsync(
            _workspaceRoot, null, query, SearchMatchMode.Fuzzy, token);
        List<HybridSearchCandidate> candidates = [];
        HashSet<Guid> seen = [];
        IEnumerable<SearchResult> ranked = results.Project
            .Concat(results.Application).Concat(results.Global)
            .OrderByDescending(item => item.Score)
            .ThenBy(item => item.FilePath, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.LineNumber);
        foreach (SearchResult hit in ranked)
        {
            token.ThrowIfCancellationRequested();
            string relative = Path.GetRelativePath(_workspaceRoot, hit.FilePath)
                .Replace('\\', '/');
            if (relative == ".." || relative.StartsWith("../", StringComparison.Ordinal) ||
                Path.IsPathRooted(relative) ||
                (scope.Length > 0 &&
                 !relative.StartsWith(scope + "/", StringComparison.OrdinalIgnoreCase)))
                continue;

            SemanticSource? source = _lexicalDocuments.FirstOrDefault(document =>
                string.Equals(document.RelativePath.Replace('\\', '/'), relative,
                    StringComparison.OrdinalIgnoreCase));
            if (source is null || !seen.Add(source.DocumentId)) continue;

            bool exactReference = hit.MatchKind == SearchMatchKind.FileName &&
                SearchTextScorer.TryScore(query, hit.DisplayName,
                    SearchMatchMode.Exact, out double _);
            candidates.Add(new HybridSearchCandidate(source.DocumentId,
                source.RelativePath, hit.LineNumber, null, hit.Excerpt,
                IsExactIdentifier: exactReference));
            if (candidates.Count >= 200) break;
        }

        return candidates;
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
        _mode.IsEnabled = false;
        _purge.IsEnabled = false;
        _inspect.IsEnabled = false;
        return _request.Token;
    }

    /// <summary>Clears transient operation state after an async action.</summary>
    private void End()
    {
        _request?.Dispose();
        _request = null;
        _index.IsEnabled = _enabled;
        _search.IsEnabled = true;
        _mode.IsEnabled = true;
        _cancel.IsEnabled = false;
        _purge.IsEnabled = true;
        _inspect.IsEnabled = true;
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
