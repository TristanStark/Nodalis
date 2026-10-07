using System.IO;
using System.Windows;
using Nodalis.App.Dialogs;
using Nodalis.Core.AI;
using Nodalis.Core.Navigation;
using Nodalis.Infrastructure.AI;

namespace Nodalis.App;

public partial class MainWindow
{
    /// <summary>
    /// Generates local test scenarios from explicitly selected documentation and inserts only user-approved Markdown.
    /// </summary>
    /// <returns>A task representing the local test-generation workflow.</returns>
    private async Task GenerateTestsWithAiAsync()
    {
        if (_selectedNode is null)
        {
            MessageBox.Show(
                this,
                "Sélectionnez d'abord un document, une section ou un projet servant de contexte.",
                "Génération de tests IA",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        if (!_preferences.Ai.IsEnabled)
        {
            MessageBox.Show(
                this,
                "L'assistant IA local est désactivé. Activez-le et configurez un exécutable local dans Préférences.",
                "Génération de tests IA",
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
                        "Le document sélectionné contient encore des modifications non enregistrées. Enregistrez-les avant la génération.",
                        "Génération de tests IA",
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
                    "Génération de tests IA",
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
                    "Génération de tests annulée avant sélection du contexte";
                return;
            }

            AiTestOptionsDialog optionsDialog =
                new AiTestOptionsDialog
                {
                    Owner =
                        this
                };

            if (optionsDialog.ShowDialog() !=
                    true ||
                optionsDialog.Options is null)
            {
                StatusText.Text =
                    "Génération de tests annulée avant configuration";
                return;
            }

            AiTestGenerationOptions options =
                optionsDialog.Options;

            FileSystemAiPromptStore promptStore =
                new FileSystemAiPromptStore(
                    Path.Combine(
                        AppContext.BaseDirectory,
                        "Prompts"));
            const string promptId =
                "test-scenarios-v1";
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
                        "Génère des scénarios de tests. Type demandé : " +
                        options.TypeLabel +
                        ". Niveau demandé : " +
                        options.LevelLabel +
                        ". Couvre obligatoirement nominal, erreurs, limites et non-régression lorsque les sources permettent de le justifier.",
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
                "Tests IA · validation du payload";

            LocalAiResponse? response =
                await executionService.ExecuteWithApprovalAsync(
                    request,
                    ShowLocalAiPayloadApprovalAsync);

            if (response is null)
            {
                StatusText.Text =
                    "Génération de tests annulée avant exécution";
                return;
            }

            if (string.IsNullOrWhiteSpace(
                    response.Content))
            {
                throw new InvalidDataException(
                    "Le moteur IA local n'a retourné aucun scénario de test.");
            }

            string[] sourceLabels =
                selectionDialog.SelectedContext
                    .Select(item =>
                        item.Label)
                    .ToArray();

            AiTestReviewDialog reviewDialog =
                new AiTestReviewDialog(
                    response.Content,
                    options,
                    sourceLabels)
                {
                    Owner =
                        this
                };

            if (reviewDialog.ShowDialog() !=
                    true ||
                string.IsNullOrWhiteSpace(
                    reviewDialog.ApprovedMarkdown))
            {
                StatusText.Text =
                    "Tests IA rejetés · aucun fichier modifié";
                return;
            }

            GeneratedTestDocumentService documentService =
                new GeneratedTestDocumentService(
                    _root.FullPath);

            string outputPath =
                await documentService.WriteAsync(
                    _selectedNode.FullPath,
                    promptId,
                    options,
                    selectionDialog.SelectedContext,
                    reviewDialog.ApprovedMarkdown);

            await RefreshNavigationAsync(
                outputPath);
            await RefreshLinkIndexAndContextAsync();

            StatusText.Text =
                "Scénarios IA insérés dans Tests · " +
                Path.GetRelativePath(
                    _root.FullPath,
                    outputPath);

            MessageBox.Show(
                this,
                "Le Markdown validé a été créé dans la section Tests avec les liens vers les sources sélectionnées.",
                "Génération de tests IA",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception exception) when (
            RecoverableExceptionPolicy.CanContinue(
                exception))
        {
            ReportRecoverableUiError(
                "IA locale · génération de tests",
                exception);
        }
    }

    /// <summary>
    /// Handles the local-AI test-generation toolbar action.
    /// </summary>
    /// <param name="sender">Event sender.</param>
    /// <param name="e">Event arguments.</param>
    private async void GenerateTestsWithAi_Click(
            object sender,
            RoutedEventArgs e)
    {
        await RunUiActionAsync(
            "IA locale · génération de tests",
            GenerateTestsWithAiAsync);
    }
}
