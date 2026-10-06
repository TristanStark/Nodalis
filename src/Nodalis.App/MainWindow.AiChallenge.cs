using System.IO;
using System.Windows;
using System.Windows.Input;
using Nodalis.App.Dialogs;
using Nodalis.Core.AI;
using Nodalis.Core.Navigation;
using Nodalis.Infrastructure.AI;

namespace Nodalis.App;

public partial class MainWindow
{
    /// <summary>
    /// Challenges the selected specification or project context through the explicitly configured local AI provider.
    /// </summary>
    /// <returns>A task representing the proposal-only review workflow.</returns>
    private async Task ChallengeSelectedContextWithAiAsync()
    {
        if (_selectedNode is null)
        {
            MessageBox.Show(
                this,
                "Sélectionnez d'abord un document, une section ou un projet à challenger.",
                "Challenge IA",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        if (!_preferences.Ai.IsEnabled)
        {
            MessageBox.Show(
                this,
                "L'assistant IA local est désactivé. Activez-le et configurez un exécutable local dans Préférences.",
                "Challenge IA",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        try
        {
            if (_selectedNode.Kind ==
                    WorkspaceNodeKind.Document &&
                _autosave is not null &&
                _documentSession is not null &&
                string.Equals(
                    Path.GetFullPath(
                        _documentSession.Path),
                    Path.GetFullPath(
                        _selectedNode.FullPath),
                    StringComparison.OrdinalIgnoreCase))
            {
                await _autosave.FlushAsync();

                if (_documentDirty)
                {
                    MessageBox.Show(
                        this,
                        "Le document sélectionné contient encore des modifications non enregistrées. Enregistrez-les avant de lancer la revue.",
                        "Challenge IA",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                    return;
                }
            }

            ProjectAiContextService contextService =
                new ProjectAiContextService(
                    _root.FullPath);

            IReadOnlyList<LocalAiContextItem> candidates =
                await contextService.LoadCandidatesAsync(
                    _selectedNode.FullPath);

            if (candidates.Count ==
                0)
            {
                MessageBox.Show(
                    this,
                    "Aucun document Markdown exploitable n'a été trouvé dans ce contexte.",
                    "Challenge IA",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            AiContextSelectionDialog selectionDialog =
                new AiContextSelectionDialog(
                    candidates)
                {
                    Owner =
                        this
                };

            if (selectionDialog.ShowDialog() !=
                    true ||
                selectionDialog.SelectedContext.Count ==
                    0)
            {
                StatusText.Text =
                    "Challenge IA annulé avant sélection du contexte";
                return;
            }

            FileSystemAiPromptStore promptStore =
                new FileSystemAiPromptStore(
                    Path.Combine(
                        AppContext.BaseDirectory,
                        "Prompts"));
            const string promptId =
                "spec-challenge-v1";
            string systemPrompt =
                await promptStore.LoadAsync(
                    promptId);

            LocalAiRequest request =
                new LocalAiRequest
                {
                    PromptId =
                        promptId,
                    SystemPrompt =
                        systemPrompt,
                    UserPrompt =
                        "Challenge les documents sélectionnés. Identifie les zones à clarifier ou vérifier sans inventer d'exigence et formule chaque résultat comme une proposition ou une question.",
                    Context =
                        selectionDialog.SelectedContext
                };

            ProcessLocalAiProvider provider =
                new ProcessLocalAiProvider(
                    _preferences.Ai);
            LocalAiExecutionService executionService =
                new LocalAiExecutionService(
                    _preferences.Ai,
                    provider);

            StatusText.Text =
                "Challenge IA local · validation du payload";

            LocalAiResponse? response =
                await executionService.ExecuteWithApprovalAsync(
                    request,
                    ShowLocalAiPayloadApprovalAsync);

            if (response is null)
            {
                StatusText.Text =
                    "Challenge IA annulé avant exécution";
                return;
            }

            AiProposalResultDialog resultDialog =
                new AiProposalResultDialog(
                    response.Content,
                    selectionDialog.SelectedContext
                        .Select(item =>
                            item.Label)
                        .ToArray())
                {
                    Owner =
                        this
                };

            resultDialog.ShowDialog();

            StatusText.Text =
                "Challenge IA terminé · aucune modification automatique";
        }
        catch (Exception exception) when (
            exception is IOException or
            UnauthorizedAccessException or
            InvalidDataException or
            InvalidOperationException or
            ArgumentException or
            TimeoutException)
        {
            MessageBox.Show(
                this,
                "Le challenge IA a échoué.\n\n" +
                exception.Message,
                "Challenge IA",
                MessageBoxButton.OK,
                MessageBoxImage.Error);

            StatusText.Text =
                "Challenge IA en échec";
        }
    }

    /// <summary>
    /// Handles the local-AI project/specification challenge toolbar action.
    /// </summary>
    /// <param name="sender">Event sender.</param>
    /// <param name="e">Event arguments.</param>
    private async void ChallengeWithAi_Click(
            object sender,
            RoutedEventArgs e)
    {
        await ChallengeSelectedContextWithAiAsync();
    }
}
