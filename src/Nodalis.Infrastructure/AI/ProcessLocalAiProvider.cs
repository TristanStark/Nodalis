using System.Diagnostics;
using Nodalis.Core.AI;
using Nodalis.Core.Settings;

namespace Nodalis.Infrastructure.AI;

/// <summary>
/// Executes AI requests through one explicitly configured local process using standard input and output.
/// </summary>
public sealed class ProcessLocalAiProvider : ILocalAiProvider
{
    private readonly AiPreferences _preferences;

    /// <summary>
    /// Initializes a new local process provider.
    /// </summary>
    /// <param name="preferences">The explicit local AI preferences.</param>
    public ProcessLocalAiProvider(
            AiPreferences preferences)
    {
        ArgumentNullException.ThrowIfNull(
            preferences);

        _preferences =
            preferences;
    }

    /// <summary>
    /// Executes an approved request through the configured local process.
    /// </summary>
    /// <param name="request">The approved local AI request.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The local process response.</returns>
    public async Task<LocalAiResponse> ExecuteAsync(
            LocalAiRequest request,
            CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(
            request);

        ValidatePreferences(
            _preferences);

        using CancellationTokenSource timeoutSource =
            new CancellationTokenSource(
                TimeSpan.FromSeconds(
                    _preferences.TimeoutSeconds));

        using CancellationTokenSource linkedSource =
            CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                timeoutSource.Token);

        ProcessStartInfo startInfo =
            new ProcessStartInfo
            {
                FileName =
                    _preferences.ExecutablePath,
                Arguments =
                    _preferences.Arguments,
                UseShellExecute =
                    false,
                RedirectStandardInput =
                    true,
                RedirectStandardOutput =
                    true,
                RedirectStandardError =
                    true,
                CreateNoWindow =
                    true
            };

        if (!string.IsNullOrWhiteSpace(
                _preferences.ModelName))
        {
            startInfo.Environment[
                "NODALIS_AI_MODEL"] =
                _preferences.ModelName.Trim();
        }

        using Process process =
            new Process
            {
                StartInfo =
                    startInfo,
                EnableRaisingEvents =
                    true
            };

        if (!process.Start())
        {
            throw new InvalidOperationException(
                "Le processus IA local n'a pas pu être démarré.");
        }

        string payload =
            LocalAiPayloadBuilder.Build(
                request);

        try
        {
            Task<string> outputTask =
                process.StandardOutput.ReadToEndAsync(
                    linkedSource.Token);
            Task<string> errorTask =
                process.StandardError.ReadToEndAsync(
                    linkedSource.Token);

            await process.StandardInput.WriteAsync(
                payload.AsMemory(),
                linkedSource.Token);
            await process.StandardInput.FlushAsync(
                linkedSource.Token);
            process.StandardInput.Close();

            await process.WaitForExitAsync(
                linkedSource.Token);

            string output =
                await outputTask;
            string diagnostics =
                await errorTask;

            if (process.ExitCode !=
                0)
            {
                throw new InvalidOperationException(
                    "Le moteur IA local s'est terminé avec le code " +
                    process.ExitCode +
                    ". " +
                    diagnostics.Trim());
            }

            return new LocalAiResponse
            {
                Content =
                    output,
                Diagnostics =
                    diagnostics
            };
        }
        catch (OperationCanceledException) when (
            timeoutSource.IsCancellationRequested &&
            !cancellationToken.IsCancellationRequested)
        {
            TryKillProcess(
                process);

            throw new TimeoutException(
                "Le moteur IA local a dépassé le délai configuré de " +
                _preferences.TimeoutSeconds +
                " seconde(s).");
        }
        catch (OperationCanceledException)
        {
            TryKillProcess(
                process);

            throw;
        }
    }

    /// <summary>
    /// Validates the explicit local process configuration.
    /// </summary>
    /// <param name="preferences">The local AI preferences.</param>
    private static void ValidatePreferences(
            AiPreferences preferences)
    {
        if (!preferences.IsEnabled)
        {
            throw new InvalidOperationException(
                "L'assistant IA local est désactivé dans les préférences.");
        }

        if (string.IsNullOrWhiteSpace(
                preferences.ExecutablePath))
        {
            throw new InvalidOperationException(
                "Aucun exécutable IA local n'est configuré.");
        }

        string fullPath =
            Path.GetFullPath(
                preferences.ExecutablePath);

        if (!Path.IsPathFullyQualified(
                fullPath) ||
            !File.Exists(
                fullPath))
        {
            throw new FileNotFoundException(
                "L'exécutable IA local configuré est introuvable.",
                fullPath);
        }

        if (preferences.TimeoutSeconds is <
                1 or >
                3600)
        {
            throw new ArgumentOutOfRangeException(
                nameof(preferences),
                "Le délai IA local doit être compris entre 1 et 3600 secondes.");
        }
    }

    /// <summary>
    /// Stops a cancelled or timed-out local process without allowing an exception to mask the original cancellation.
    /// </summary>
    /// <param name="process">The process to stop.</param>
    private static void TryKillProcess(
            Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(
                    entireProcessTree:
                        true);
            }
        }
        catch (InvalidOperationException)
        {
        }
    }
}
