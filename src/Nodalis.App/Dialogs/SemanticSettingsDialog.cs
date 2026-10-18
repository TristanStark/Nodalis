using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using Nodalis.Core.Settings;
using Nodalis.Core.Semantic;
using Nodalis.Infrastructure.AI;

namespace Nodalis.App.Dialogs;

/// <summary>Explicit local embedding consent and model configuration, with no provider calls.</summary>
public sealed class SemanticSettingsDialog : Window
{
    private readonly CheckBox _enabled;
    private readonly TextBox _executable;
    private readonly TextBox _arguments;
    private readonly TextBox _model;
    private readonly TextBox _dimensions;
    private readonly TextBox _batch;
    private readonly TextBox _timeout;
    private readonly TextBox _exclusions;
    private CancellationTokenSource? _probeRequest;

    /// <summary>Validated settings chosen by the user, when DialogResult is true.</summary>
    public SemanticSearchPreferences? Preferences { get; private set; }

    /// <summary>Constructs a keyboard-accessible configuration dialog without local model probing.</summary>
    /// <param name="initial">Current persisted preferences.</param>
    /// <param name="cachePath">Location of the disposable vector cache, for transparency.</param>
    public SemanticSettingsDialog(SemanticSearchPreferences initial, string cachePath)
    {
        ArgumentNullException.ThrowIfNull(initial);
        Title = "Recherche sémantique — configuration locale";
        Width = 680;
        Height = 650;
        MinWidth = 480;
        MinHeight = 430;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.CanResize;
        StackPanel form = new() { Margin = new Thickness(20) };
        form.Children.Add(new TextBlock
        {
            Text = "Les embeddings sont optionnels. Un modèle conversationnel ne suffit pas.",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 12)
        });
        _enabled = new CheckBox
        {
            Content = "J'autorise explicitement l'indexation locale par embeddings",
            IsChecked = initial.IsEnabled,
            Margin = new Thickness(0, 0, 0, 14)
        };
        form.Children.Add(_enabled);
        string includedBridge = Path.Combine(AppContext.BaseDirectory,
            "Nodalis.EmbeddingBridge.exe");
        string defaultExecutable = string.IsNullOrWhiteSpace(initial.ExecutablePath) &&
            File.Exists(includedBridge) ? includedBridge : initial.ExecutablePath;
        _executable = AddInput(form, "Exécutable d'embeddings local (*.exe)",
            defaultExecutable);
        Button browse = new()
        {
            Content = "Parcourir…",
            Margin = new Thickness(0, 2, 0, 12),
            HorizontalAlignment = HorizontalAlignment.Left,
            MinHeight = 30
        };
        browse.Click += (_, _) =>
        {
            OpenFileDialog picker = new()
            {
                Filter = "Exécutables Windows (*.exe)|*.exe|Tous les fichiers (*.*)|*.*",
                CheckFileExists = true
            };
            if (picker.ShowDialog(this) == true) _executable.Text = picker.FileName;
        };
        form.Children.Add(browse);
        _arguments = AddInput(form, "Arguments du processus (facultatifs)",
            initial.Arguments);
        _model = AddInput(form, "Identifiant exact du modèle embeddings",
            initial.ModelId);
        _dimensions = AddInput(form, "Dimension des vecteurs (1 à 4096)",
            initial.Dimensions.ToString(System.Globalization.CultureInfo.InvariantCulture));
        _batch = AddInput(form, "Passages par lot (1 à 32)",
            initial.MaxBatchSize.ToString(System.Globalization.CultureInfo.InvariantCulture));
        _timeout = AddInput(form, "Délai maximum en secondes (1 à 300)",
            initial.TimeoutSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture));
        _exclusions = AddInput(form,
            "Exclusions supplémentaires (chemins ou motifs *.md, séparés par ;)",
            string.Join("; ", initial.ExcludedPathPatterns));

        Button preset = new()
        {
            Content = "Configurer Ollama local (embeddinggemma, 768 dimensions)",
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 10, 0, 10),
            MinHeight = 32
        };
        preset.Click += (_, _) =>
        {
            if (!File.Exists(includedBridge))
            {
                MessageBox.Show(this,
                    "L'adaptateur local n'est pas présent dans ce package. " +
                    "Sélectionnez un fournisseur CLI compatible.",
                    "Embeddings Ollama", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            _executable.Text = includedBridge;
            _arguments.Text = string.Empty;
            _model.Text = "embeddinggemma";
            _dimensions.Text = "768";
            _batch.Text = "16";
            _timeout.Text = "120";
        };
        form.Children.Add(preset);
        Button probe = new()
        {
            Content = "Tester le modèle local (phrase factice, aucun document)",
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 0, 0, 6),
            MinHeight = 32
        };
        TextBlock probeStatus = new()
        {
            Text = "Test facultatif : vérifie la disponibilité et les dimensions.",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 8)
        };
        probe.Click += async (_, _) => await ProbeProviderAsync(probe, probeStatus);
        form.Children.Add(probe);
        form.Children.Add(probeStatus);
        form.Children.Add(new TextBlock
        {
            Text = "Cache dérivé supprimable : " + cachePath,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 8, 0, 12)
        });
        form.Children.Add(new TextBlock
        {
            Text = "Un exécutable tiers peut avoir ses propres capacités réseau. " +
                "Utilisez seulement un outil local de confiance. Nodalis ne l'active " +
                "jamais au démarrage ; les opérations demandent une nouvelle confirmation.",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 14)
        });

        StackPanel actions = new()
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        Button cancel = new()
        {
            Content = "Annuler",
            MinWidth = 92,
            MinHeight = 34,
            Margin = new Thickness(0, 0, 12, 0),
            IsCancel = true
        };
        Button save = new()
        {
            Content = "Enregistrer",
            MinWidth = 115,
            MinHeight = 34,
            IsDefault = true
        };
        cancel.Click += (_, _) => DialogResult = false;
        save.Click += (_, _) => Save();
        actions.Children.Add(cancel);
        actions.Children.Add(save);
        form.Children.Add(actions);
        Content = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = form
        };
        Closing += (_, _) => _probeRequest?.Cancel();
    }

    /// <summary>Creates a labeled, text-selectable settings entry.</summary>
    /// <param name="parent">Form content host.</param>
    /// <param name="label">Human-readable accessible field label.</param>
    /// <param name="value">Initial field value.</param>
    /// <returns>Editable text field.</returns>
    private static TextBox AddInput(StackPanel parent, string label, string value)
    {
        TextBlock heading = new()
        {
            Text = label,
            Margin = new Thickness(0, 7, 0, 4)
        };
        TextBox field = new()
        {
            Text = value,
            MinHeight = 30,
            Padding = new Thickness(7, 4, 7, 4),
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        parent.Children.Add(heading);
        parent.Children.Add(field);
        return field;
    }

    /// <summary>Tests a configured model using synthetic text without indexing workspace data.</summary>
    /// <param name="button">Button disabled while the local probe is active.</param>
    /// <param name="status">Inline result or safe error diagnostic.</param>
    /// <returns>Completed explicit local model check.</returns>
    private async Task ProbeProviderAsync(Button button, TextBlock status)
    {
        if (!int.TryParse(_dimensions.Text, out int dimensions) ||
            !int.TryParse(_timeout.Text, out int seconds) ||
            dimensions is < 1 or > 4096 || seconds is < 1 or > 300)
        {
            status.Text = "Saisissez d'abord la dimension et le délai du modèle local.";
            return;
        }

        SemanticSearchPreferences probeSettings = new()
        {
            IsEnabled = true,
            ExecutablePath = _executable.Text.Trim(),
            Arguments = _arguments.Text.Trim(),
            ModelId = _model.Text.Trim(),
            Dimensions = dimensions,
            MaxBatchSize = 1,
            TimeoutSeconds = seconds
        };
        _probeRequest?.Cancel();
        CancellationTokenSource request = new();
        _probeRequest = request;
        button.IsEnabled = false;
        status.Text = "Test volontaire en cours, aucune note du workspace n'est transmise…";
        try
        {
            ProcessEmbeddingProvider provider = new(probeSettings);
            EmbeddingBatch batch = await provider.EmbedAsync(
                ["Nodalis vérifie ici un modèle d'embeddings local avec une phrase artificielle."],
                request.Token);
            status.Text = "Modèle compatible : " + batch.ModelId + ", " +
                batch.Dimensions + " dimensions. Aucun document n'a été indexé.";
        }
        catch (EmbeddingProviderException error)
        {
            status.Text = "Test local échoué : " + error.Message;
        }
        catch (OperationCanceledException)
        {
            status.Text = "Test local interrompu.";
        }
        finally
        {
            if (ReferenceEquals(_probeRequest, request))
            {
                _probeRequest = null;
            }

            request.Dispose();
            button.IsEnabled = true;
        }
    }

    /// <summary>Validates consent and ensures that no remote or missing executable is saved.</summary>
    private void Save()
    {
        bool enabled = _enabled.IsChecked == true;
        if (!int.TryParse(_dimensions.Text, out int dimensions) ||
            !int.TryParse(_batch.Text, out int batch) ||
            !int.TryParse(_timeout.Text, out int timeout) ||
            dimensions is < 0 or > 4096 ||
            batch is < 1 or > 32 || timeout is < 1 or > 300)
        {
            MessageBox.Show(this, "Dimension, lot ou délai invalide.",
                "Configuration sémantique", MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        string path = _executable.Text.Trim();
        if (enabled && (dimensions == 0 || string.IsNullOrWhiteSpace(_model.Text) ||
            string.IsNullOrWhiteSpace(path) ||
            !Path.IsPathFullyQualified(path) || !File.Exists(path) ||
            path.StartsWith(@"\\", StringComparison.Ordinal) ||
            path.StartsWith("//", StringComparison.Ordinal) ||
            _arguments.Text.Contains("://", StringComparison.Ordinal)))
        {
            MessageBox.Show(this,
                "Sélectionnez un exécutable local de confiance et un modèle " +
                "d'embeddings compatible avec sa dimension.",
                "Configuration sémantique", MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        string[] exclusions = _exclusions.Text.Split(
            [';', ',', '\r', '\n'],
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (exclusions.Length > 20 ||
            exclusions.Any(pattern => pattern.Length > 200 ||
                pattern.Contains(':') || pattern.StartsWith('/') ||
                pattern.StartsWith('\\') || pattern.Contains("..", StringComparison.Ordinal)))
        {
            MessageBox.Show(this,
                "Exclusions invalides : 20 motifs relatifs au maximum, 200 caractères chacun.",
                "Confidentialité sémantique", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        Preferences = new SemanticSearchPreferences
        {
            IsEnabled = enabled,
            ExecutablePath = path,
            Arguments = _arguments.Text.Trim(),
            ModelId = _model.Text.Trim(),
            Dimensions = dimensions,
            MaxBatchSize = batch,
            TimeoutSeconds = timeout,
            ExcludedPathPatterns = exclusions
        };
        DialogResult = true;
    }
}
