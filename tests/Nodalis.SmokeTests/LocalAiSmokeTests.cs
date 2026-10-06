using Nodalis.Core.AI;
using Nodalis.Core.Settings;
using Nodalis.Infrastructure.AI;

internal static class LocalAiSmokeTests
{
    /// <summary>
    /// Verifies disabled behavior, readable prompts, exact approval payloads, and local-process execution.
    /// </summary>
    /// <param name="root">The smoke-test root directory.</param>
    /// <returns>A task representing the verification.</returns>
    public static async Task RunAsync(
            string root)
    {
        string promptRoot =
            Path.Combine(
                root,
                "AiPrompts");

        Directory.CreateDirectory(
            promptRoot);

        string promptPath =
            Path.Combine(
                promptRoot,
                "smoke-v1.md");

        await File.WriteAllTextAsync(
            promptPath,
            "# Prompt lisible\n\nRéponds uniquement avec le contexte fourni.");

        FileSystemAiPromptStore store =
            new FileSystemAiPromptStore(
                promptRoot);

        string systemPrompt =
            await store.LoadAsync(
                "smoke-v1");

        Assert(
            systemPrompt.Contains(
                "Prompt lisible",
                StringComparison.Ordinal),
            "Versioned AI prompts must remain readable files.");

        LocalAiRequest request =
            new LocalAiRequest
            {
                PromptId =
                    "smoke-v1",
                SystemPrompt =
                    systemPrompt,
                UserPrompt =
                    "Résume ce texte.",
                Context =
                [
                    new LocalAiContextItem
                    {
                        Label =
                            "Document test",
                        Content =
                            "Contexte exact à transmettre."
                    }
                ]
            };

        LocalAiPayloadPreview preview =
            LocalAiPayloadBuilder.CreatePreview(
                request);

        Assert(
            preview.Payload.Contains(
                "### Document test",
                StringComparison.Ordinal) &&
            preview.Payload.Contains(
                "Contexte exact à transmettre.",
                StringComparison.Ordinal) &&
            preview.ContextLabels.SequenceEqual(
                new[]
                {
                    "Document test"
                }),
            "The approval preview must expose the exact context and payload.");

        RecordingProvider recordingProvider =
            new RecordingProvider();

        LocalAiExecutionService disabled =
            new LocalAiExecutionService(
                new AiPreferences
                {
                    IsEnabled =
                        false
                },
                recordingProvider);

        bool disabledRejected =
            false;

        try
        {
            await disabled.ExecuteWithApprovalAsync(
                request,
                static (
                    _,
                    _) =>
                    Task.FromResult(
                        true));
        }
        catch (InvalidOperationException)
        {
            disabledRejected =
                true;
        }

        Assert(
            disabledRejected &&
            recordingProvider.CallCount ==
                0,
            "A disabled local AI integration must never invoke its provider.");

        LocalAiExecutionService approvalService =
            new LocalAiExecutionService(
                new AiPreferences
                {
                    IsEnabled =
                        true
                },
                recordingProvider);

        LocalAiResponse? rejected =
            await approvalService.ExecuteWithApprovalAsync(
                request,
                (
                    exactPreview,
                    _) =>
                {
                    Assert(
                        exactPreview.Payload ==
                            LocalAiPayloadBuilder.Build(
                                request),
                        "The approval callback must receive the exact provider stdin payload.");

                    return Task.FromResult(
                        false);
                });

        Assert(
            rejected is null &&
            recordingProvider.CallCount ==
                0,
            "Rejecting the visible payload must prevent local provider execution.");

        LocalAiResponse? approved =
            await approvalService.ExecuteWithApprovalAsync(
                request,
                static (
                    _,
                    _) =>
                    Task.FromResult(
                        true));

        Assert(
            approved?.Content ==
                "recorded" &&
            recordingProvider.CallCount ==
                1,
            "Explicit approval must allow exactly one provider execution.");

        string? commandProcessor =
            Environment.GetEnvironmentVariable(
                "ComSpec");

        if (!string.IsNullOrWhiteSpace(
                commandProcessor) &&
            File.Exists(
                commandProcessor))
        {
            AiPreferences processPreferences =
                new AiPreferences
                {
                    IsEnabled =
                        true,
                    ExecutablePath =
                        commandProcessor,
                    Arguments =
                        "/D /Q /C more",
                    ModelName =
                        "smoke-model",
                    TimeoutSeconds =
                        15
                };

            ProcessLocalAiProvider processProvider =
                new ProcessLocalAiProvider(
                    processPreferences);

            LocalAiResponse processResponse =
                await processProvider.ExecuteAsync(
                    request);

            Assert(
                processResponse.Content.Contains(
                    "Contexte exact à transmettre.",
                    StringComparison.Ordinal),
                "The process provider must write the exact request payload to local process stdin and capture stdout.");
        }

        bool traversalRejected =
            false;

        try
        {
            await store.LoadAsync(
                "../secret");
        }
        catch (ArgumentException)
        {
            traversalRejected =
                true;
        }

        Assert(
            traversalRejected,
            "Prompt identifiers must not escape the configured local prompt directory.");
    }

    /// <summary>
    /// Throws when a local AI smoke-test condition is not satisfied.
    /// </summary>
    /// <param name="condition">Condition to verify.</param>
    /// <param name="message">Failure message.</param>
    private static void Assert(
            bool condition,
            string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(
                message);
        }
    }

    private sealed class RecordingProvider : ILocalAiProvider
    {
        /// <summary>Gets the number of provider invocations.</summary>
        public int CallCount { get; private set; }

        /// <summary>
        /// Records one invocation without contacting any external service.
        /// </summary>
        /// <param name="request">The approved request.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>A deterministic fake response.</returns>
        public Task<LocalAiResponse> ExecuteAsync(
                LocalAiRequest request,
                CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(
                request);
            cancellationToken.ThrowIfCancellationRequested();

            CallCount++;

            return Task.FromResult(
                new LocalAiResponse
                {
                    Content =
                        "recorded"
                });
        }
    }
}
