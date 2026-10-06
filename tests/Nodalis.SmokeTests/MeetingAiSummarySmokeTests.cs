using Nodalis.Core.AI;
using Nodalis.Infrastructure.AI;

internal static class MeetingAiSummarySmokeTests
{
    /// <summary>
    /// Verifies parsing, per-proposal acceptance, provenance, and source immutability for meeting AI summaries.
    /// </summary>
    /// <param name="root">The smoke-test workspace root.</param>
    /// <returns>A task representing the verification.</returns>
    public static async Task RunAsync(
            string root)
    {
        string directory =
            Path.Combine(
                root,
                "Applications",
                "App",
                "Projects",
                "Demo",
                "Réunions");

        Directory.CreateDirectory(
            directory);

        string sourcePath =
            Path.Combine(
                directory,
                "2026-10-06-comite.md");
        const string sourceContent =
            "# Réunion\n\nDécision : conserver le format local.\nAction : documenter le flux.\n";

        await File.WriteAllTextAsync(
            sourcePath,
            sourceContent);

        const string response =
            "## Résumé\n\nLe comité confirme le fonctionnement local.\n\n" +
            "## Décisions candidates\n\n- Conserver le format local.\n\n" +
            "## Actions candidates\n\n- Documenter le flux.\n\n" +
            "## Questions ouvertes\n\n- Faut-il ajouter un exemple ?\n\n" +
            "## Risques / points d'attention\n\n- Vérifier la compatibilité des wrappers locaux.\n";

        MeetingAiDraft parsed =
            MeetingAiResponseParser.Parse(
                response);

        Assert(
            parsed.Summary.Contains(
                "fonctionnement local",
                StringComparison.Ordinal) &&
            parsed.Proposals.Count ==
                4 &&
            parsed.Proposals.Count(proposal =>
                proposal.Kind ==
                MeetingAiProposalKind.Action) ==
                1,
            "Meeting AI responses must be parsed into one editable item per proposal.");

        MeetingAiProposal editedAction =
            new MeetingAiProposal
            {
                Kind =
                    MeetingAiProposalKind.Action,
                Text =
                    "Documenter le flux local avec un exemple validé."
            };
        MeetingAiProposal acceptedDecision =
            parsed.Proposals.Single(proposal =>
                proposal.Kind ==
                MeetingAiProposalKind.Decision);

        MeetingAiDraft approved =
            new MeetingAiDraft
            {
                Summary =
                    parsed.Summary,
                Proposals =
                [
                    acceptedDecision,
                    editedAction
                ]
            };

        MeetingAiResultDocumentService service =
            new MeetingAiResultDocumentService(
                root);

        string resultPath =
            await service.WriteAsync(
                sourcePath,
                "meeting-summary-v1",
                approved);

        string sourceAfter =
            await File.ReadAllTextAsync(
                sourcePath);
        string resultContent =
            await File.ReadAllTextAsync(
                resultPath);

        Assert(
            sourceAfter ==
                sourceContent,
            "Accepting AI proposals must never modify the source meeting document.");
        Assert(
            !string.Equals(
                Path.GetFullPath(
                    sourcePath),
                Path.GetFullPath(
                    resultPath),
                StringComparison.OrdinalIgnoreCase),
            "Accepted AI output must be persisted separately from its source.");
        Assert(
            resultContent.Contains(
                "source: Applications/App/Projects/Demo/Réunions/2026-10-06-comite.md",
                StringComparison.Ordinal) &&
            resultContent.Contains(
                "prompt: meeting-summary-v1",
                StringComparison.Ordinal) &&
            resultContent.Contains(
                "Documenter le flux local avec un exemple validé.",
                StringComparison.Ordinal) &&
            !resultContent.Contains(
                "Faut-il ajouter un exemple ?",
                StringComparison.Ordinal),
            "The persisted review must preserve provenance, edits, and explicit rejections.");

        bool invalidRejected =
            false;

        try
        {
            MeetingAiResponseParser.Parse(
                "Réponse libre sans sections attendues.");
        }
        catch (InvalidDataException)
        {
            invalidRejected =
                true;
        }

        Assert(
            invalidRejected,
            "Unstructured meeting AI responses must be rejected instead of being inserted blindly.");
    }

    /// <summary>
    /// Throws when a meeting AI smoke-test condition fails.
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
}
