using System.Windows;
using Nodalis.Infrastructure.Projects;

namespace Nodalis.App.Dialogs;

/// <summary>
/// Cancellable, read-only migration preflight. Closing this window signals
/// cancellation; only a successfully completed plan is returned to its owner.
/// The UI dispatcher is never used to scan or fingerprint the workspace.
/// </summary>
public partial class ComplexMigrationPreflightDialog : Window
{
    private readonly Func<CancellationToken,
        IProgress<ComplexMigrationPreflightProgress>,
        Task<ComplexProjectMigrationPlan>> _prepare;
    private readonly CancellationTokenSource _cancellation = new();
    private bool _started;

    /// <summary>Creates an analysis dialog without starting any file access.</summary>
    /// <param name="prepare">Cancelable, side-effect-free preflight operation.</param>
    public ComplexMigrationPreflightDialog(
        Func<CancellationToken, IProgress<ComplexMigrationPreflightProgress>,
            Task<ComplexProjectMigrationPlan>> prepare)
    {
        _prepare = prepare ?? throw new ArgumentNullException(nameof(prepare));
        InitializeComponent();
        Loaded += async (_, _) => await StartAsync();
        Closed += (_, _) => _cancellation.Cancel();
    }

    /// <summary>Present only after a completed and noncancelled preflight.</summary>
    public ComplexProjectMigrationPlan? Plan { get; private set; }

    /// <summary>Propagates a genuine preflight failure to the existing UI guard.</summary>
    public Exception? Failure { get; private set; }

    /// <summary>Starts the heavy read-only preflight on a worker once.</summary>
    /// <returns>Completion or cancellation; never writes a project file.</returns>
    private async Task StartAsync()
    {
        if (_started)
        {
            return;
        }

        _started = true;
        try
        {
            // Constructed on the UI dispatcher: Progress<T> safely posts
            // bounded, source-name-free snapshots to this window.
            IProgress<ComplexMigrationPreflightProgress> progress =
                new Progress<ComplexMigrationPreflightProgress>(snapshot =>
                {
                    if (_cancellation.IsCancellationRequested || !IsVisible)
                    {
                        return;
                    }

                    string stage = snapshot.Phase switch
                    {
                        ComplexMigrationPreflightPhase.Inventory =>
                            "Inventaire du projet…",
                        ComplexMigrationPreflightPhase.AnalyzeLinks =>
                            "Vérification des liens Markdown…",
                        ComplexMigrationPreflightPhase.Fingerprint =>
                            "Calcul de l'empreinte du projet…",
                        _ => "Vérification terminée."
                    };
                    ProgressText.Text = stage + Environment.NewLine +
                        snapshot.ProjectDocumentsInspected +
                        " document(s) du projet, " +
                        snapshot.ExternalDocumentsInspected +
                        " source(s) externe(s) candidate(s), " +
                        snapshot.ExternalDocumentsSkipped +
                        " document(s) externe(s) ignoré(s).";
                });
            ComplexProjectMigrationPlan plan = await Task.Run(
                () => _prepare(_cancellation.Token, progress),
                _cancellation.Token);
            if (!_cancellation.IsCancellationRequested && IsVisible)
            {
                Plan = plan;
                DialogResult = true;
            }
        }
        catch (OperationCanceledException) when (
            _cancellation.IsCancellationRequested)
        {
            // Closing the dialog is a deliberate user cancellation, not a
            // migration error and never an approval.
        }
        catch (Exception exception)
        {
            Failure = exception;
            if (IsVisible)
            {
                DialogResult = false;
            }
        }
    }

    /// <summary>Cancels preflight, not the durable migration transaction.</summary>
    /// <param name="sender">Cancel button.</param>
    /// <param name="e">WPF event.</param>
    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        _cancellation.Cancel();
        DialogResult = false;
    }
}
