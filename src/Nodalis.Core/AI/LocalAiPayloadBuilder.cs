using System.Text;

namespace Nodalis.Core.AI;

/// <summary>
/// Builds the exact text payload written to a local AI process.
/// </summary>
public static class LocalAiPayloadBuilder
{
    /// <summary>
    /// Serializes one request into a deterministic, human-readable stdin payload.
    /// </summary>
    /// <param name="request">The local AI request.</param>
    /// <returns>The exact payload to display and send.</returns>
    public static string Build(
            LocalAiRequest request)
    {
        ArgumentNullException.ThrowIfNull(
            request);

        StringBuilder builder =
            new StringBuilder();

        builder.AppendLine(
            "# NODALIS LOCAL AI REQUEST");
        builder.AppendLine(
            "Prompt-Id: " +
            request.PromptId);
        builder.AppendLine();
        builder.AppendLine(
            "## SYSTEM");
        builder.AppendLine(
            request.SystemPrompt.Trim());
        builder.AppendLine();
        builder.AppendLine(
            "## CONTEXT");

        if (request.Context.Count ==
            0)
        {
            builder.AppendLine(
                "(aucun contexte)");
        }
        else
        {
            foreach (LocalAiContextItem item in request.Context)
            {
                builder.AppendLine();
                builder.AppendLine(
                    "### " +
                    item.Label);
                builder.AppendLine(
                    item.Content.Trim());
            }
        }

        builder.AppendLine();
        builder.AppendLine(
            "## USER");
        builder.AppendLine(
            request.UserPrompt.Trim());

        return builder.ToString();
    }

    /// <summary>
    /// Creates the user-visible preview for one request.
    /// </summary>
    /// <param name="request">The local AI request.</param>
    /// <returns>The exact payload preview.</returns>
    public static LocalAiPayloadPreview CreatePreview(
            LocalAiRequest request)
    {
        ArgumentNullException.ThrowIfNull(
            request);

        return new LocalAiPayloadPreview
        {
            PromptId =
                request.PromptId,
            Payload =
                Build(
                    request),
            ContextLabels =
                request.Context
                    .Select(item =>
                        item.Label)
                    .ToArray()
        };
    }
}
