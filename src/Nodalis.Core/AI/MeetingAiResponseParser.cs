using System.Text;

namespace Nodalis.Core.AI;

/// <summary>
/// Parses the constrained Markdown format requested from the local meeting assistant.
/// </summary>
public static class MeetingAiResponseParser
{
    private enum Section
    {
        None,
        Summary,
        Decisions,
        Actions,
        Questions,
        Risks
    }

    /// <summary>
    /// Parses one local-model Markdown response into editable meeting proposals.
    /// </summary>
    /// <param name="content">The model response.</param>
    /// <returns>The structured meeting draft.</returns>
    public static MeetingAiDraft Parse(
            string content)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(
            content);

        string normalized =
            content.Replace(
                "\r\n",
                "\n",
                StringComparison.Ordinal);

        string[] lines =
            normalized.Split(
                '\n');

        StringBuilder summary =
            new StringBuilder();
        List<MeetingAiProposal> proposals =
            [];
        Section current =
            Section.None;
        bool recognizedSection =
            false;

        foreach (string rawLine in lines)
        {
            string line =
                rawLine.Trim();

            if (line.StartsWith(
                    "## ",
                    StringComparison.Ordinal))
            {
                current =
                    ClassifyHeading(
                        line[3..].Trim());

                if (current !=
                    Section.None)
                {
                    recognizedSection =
                        true;
                }

                continue;
            }

            if (current ==
                Section.Summary)
            {
                if (summary.Length >
                        0)
                {
                    summary.AppendLine();
                }

                summary.Append(
                    rawLine.TrimEnd());
                continue;
            }

            MeetingAiProposalKind? kind =
                ToProposalKind(
                    current);

            if (kind is null ||
                string.IsNullOrWhiteSpace(
                    line))
            {
                continue;
            }

            string proposalText =
                StripBullet(
                    line);

            if (string.IsNullOrWhiteSpace(
                    proposalText))
            {
                continue;
            }

            proposals.Add(
                new MeetingAiProposal
                {
                    Kind =
                        kind.Value,
                    Text =
                        proposalText
                });
        }

        if (!recognizedSection)
        {
            throw new InvalidDataException(
                "La réponse IA ne respecte pas le format Markdown attendu pour une réunion.");
        }

        return new MeetingAiDraft
        {
            Summary =
                summary.ToString().Trim(),
            Proposals =
                proposals
        };
    }

    /// <summary>
    /// Maps one Markdown heading to the expected response section.
    /// </summary>
    /// <param name="heading">The heading text without Markdown markers.</param>
    /// <returns>The parsed section.</returns>
    private static Section ClassifyHeading(
            string heading)
    {
        if (heading.StartsWith(
                "Résumé",
                StringComparison.CurrentCultureIgnoreCase) ||
            heading.StartsWith(
                "Resume",
                StringComparison.OrdinalIgnoreCase))
        {
            return Section.Summary;
        }

        if (heading.StartsWith(
                "Décisions",
                StringComparison.CurrentCultureIgnoreCase) ||
            heading.StartsWith(
                "Decisions",
                StringComparison.OrdinalIgnoreCase))
        {
            return Section.Decisions;
        }

        if (heading.StartsWith(
                "Actions",
                StringComparison.CurrentCultureIgnoreCase))
        {
            return Section.Actions;
        }

        if (heading.StartsWith(
                "Questions",
                StringComparison.CurrentCultureIgnoreCase))
        {
            return Section.Questions;
        }

        if (heading.StartsWith(
                "Risques",
                StringComparison.CurrentCultureIgnoreCase) ||
            heading.StartsWith(
                "Points d'attention",
                StringComparison.CurrentCultureIgnoreCase))
        {
            return Section.Risks;
        }

        return Section.None;
    }

    /// <summary>
    /// Maps an internal parser section to a public proposal kind.
    /// </summary>
    /// <param name="section">The parser section.</param>
    /// <returns>The proposal kind, or null for non-proposal sections.</returns>
    private static MeetingAiProposalKind? ToProposalKind(
            Section section) =>
        section switch
        {
            Section.Decisions => MeetingAiProposalKind.Decision,
            Section.Actions => MeetingAiProposalKind.Action,
            Section.Questions => MeetingAiProposalKind.OpenQuestion,
            Section.Risks => MeetingAiProposalKind.Risk,
            _ => null
        };

    /// <summary>
    /// Removes a conventional Markdown list prefix while keeping free-form proposal text usable.
    /// </summary>
    /// <param name="line">The trimmed response line.</param>
    /// <returns>The proposal text.</returns>
    private static string StripBullet(
            string line)
    {
        if (line.StartsWith(
                "- ",
                StringComparison.Ordinal) ||
            line.StartsWith(
                "* ",
                StringComparison.Ordinal))
        {
            return line[2..].Trim();
        }

        return line.Trim();
    }
}
