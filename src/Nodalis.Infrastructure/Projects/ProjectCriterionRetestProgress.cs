using Nodalis.Core.Tests;

namespace Nodalis.Infrastructure.Projects;

/// <summary>
/// Observed placement of a CA-related test within the selected campaign's
/// latest frozen passage. These are NOT requalification verdicts.
/// </summary>
public enum ProjectCriterionRetestProgressState
{
    NoPassage,
    EarlierThanBaseline,
    NotSelected,
    SelectedWithoutResult,
    HumanResultDeclared,
    SourceMismatch
}

/// <summary>
/// Read-only observation of a linked test in the latest REC passage.
/// The outcome is a historic human declaration, never a verified proof.
/// </summary>
/// <param name="TestId">Stable current test GUID.</param>
/// <param name="Reference">Current display title.</param>
/// <param name="RelatedCriterionIds">Changed CA GUIDs motivating the proposal.</param>
/// <param name="State">Observable placement/result, not business approval.</param>
/// <param name="PassageId">Latest passage GUID if usable, otherwise null.</param>
/// <param name="PassageNumber">Latest passage number if usable.</param>
/// <param name="Outcome">Explicit human-declared outcome, or null.</param>
/// <param name="SourceMismatch">Frozen source location differs from current link.</param>
public sealed record ProjectCriterionRetestProgressRow(
    Guid TestId, string Reference, IReadOnlyList<Guid> RelatedCriterionIds,
    ProjectCriterionRetestProgressState State,
    Guid? PassageId, int? PassageNumber,
    ProjectTestOutcome? Outcome, bool SourceMismatch);

/// <summary>
/// One campaign's nonbinding follow-up of changed-CA-linked tests.
/// It does not preserve any completion decision and never writes REC/EXI.
/// </summary>
/// <param name="CampaignId">Exact REC identity.</param>
/// <param name="BaselineSequence">Human CA observation being compared.</param>
/// <param name="Rows">One unique test GUID per currently proposed test.</param>
/// <param name="Warnings">Uncertain source/link/historical context.</param>
public sealed record ProjectCriterionRetestProgressOverview(
    Guid CampaignId, int? BaselineSequence,
    IReadOnlyList<ProjectCriterionRetestProgressRow> Rows,
    IReadOnlyList<string> Warnings)
{
    /// <summary>Tests absent from a usable latest REC passage.</summary>
    public int NotSelectedCount => Rows.Count(item =>
        item.State is ProjectCriterionRetestProgressState.NoPassage or
            ProjectCriterionRetestProgressState.EarlierThanBaseline or
            ProjectCriterionRetestProgressState.NotSelected);

    /// <summary>Selected tests with no declared result in this passage.</summary>
    public int WithoutResultCount => Rows.Count(item =>
        item.State == ProjectCriterionRetestProgressState.SelectedWithoutResult);

    /// <summary>
    /// Tests with historical human results. This does NOT mark them
    /// requalified or exempt them from human review.
    /// </summary>
    public int HumanResultCount => Rows.Count(item =>
        item.State is ProjectCriterionRetestProgressState.HumanResultDeclared or
            ProjectCriterionRetestProgressState.SourceMismatch &&
            item.Outcome.HasValue);

    /// <summary>
    /// Bounded user-friendly overview with no claims of current conformity.
    /// </summary>
    /// <returns>Text displayed in the REC manager, with warnings.</returns>
    public string Describe()
    {
        List<string> lines =
        [
            "SUIVI DES TESTS LIÉS AUX CA MODIFIÉS — LECTURE SEULE",
            "Campagne REC : " + CampaignId.ToString("D"),
            "Référence humaine CA : " +
                (BaselineSequence?.ToString() ?? "absente"),
            "Tests actuellement proposés : " + Rows.Count,
            "Absents du dernier passage / passage non comparable : " +
                NotSelectedCount,
            "Sélectionnés, sans résultat humain : " + WithoutResultCount,
            "Résultats humains déclarés dans le dernier passage : " +
                HumanResultCount,
            "",
            "Un résultat déclaré ne prouve PAS une requalification après " +
                "changement CA. La date exacte de modification du CA " +
                "n'est pas conservée dans la baseline."
        ];
        foreach (ProjectCriterionRetestProgressRow row in Rows.Take(75))
        {
            string state = row.State switch
            {
                ProjectCriterionRetestProgressState.NoPassage =>
                    "aucun passage REC",
                ProjectCriterionRetestProgressState.EarlierThanBaseline =>
                    "passage antérieur à la référence CA",
                ProjectCriterionRetestProgressState.NotSelected =>
                    "absent du dernier passage",
                ProjectCriterionRetestProgressState.SelectedWithoutResult =>
                    "figé, résultat NON EXÉCUTÉ",
                ProjectCriterionRetestProgressState.HumanResultDeclared =>
                    "résultat humain historique : " + row.Outcome,
                ProjectCriterionRetestProgressState.SourceMismatch =>
                    "source figée déplacée : " +
                    (row.Outcome?.ToString() ?? "NON EXÉCUTÉ"),
                _ => "inconnu"
            };
            lines.Add("• " + row.Reference + " (" +
                row.TestId.ToString("D") + ") : " + state +
                " · " + row.RelatedCriterionIds.Count + " CA lié(s)");
        }

        if (Rows.Count > 75)
        {
            lines.Add("… " + (Rows.Count - 75) +
                " autre(s) test(s) dans la proposition.");
        }

        if (Warnings.Count > 0)
        {
            lines.Add("");
            lines.Add("LIMITES À QUALIFIER :");
            lines.AddRange(Warnings.Take(12));
            if (Warnings.Count > 12)
            {
                lines.Add("… " + (Warnings.Count - 12) +
                    " autre(s) avertissement(s).");
            }
        }

        lines.Add("");
        lines.Add("La matrice, les campagnes et les anciens verdicts " +
            "restent inchangés. Nouvelle exécution et résultat : " +
            "décisions humaines explicites.");
        return string.Join(Environment.NewLine, lines);
    }
}

/// <summary>
/// Read-only join of the *current* CA-driven proposal with the latest
/// frozen passage of one validated REC campaign. A baseline does not record
/// the precise CA edit time, so NO outcome is treated as proof of replay.
/// </summary>
public static class ProjectCriterionRetestProgress
{
    /// <summary>
    /// Classifies latest-passage inclusion and declared outcomes without
    /// changing the campaign, its history or any requirement state.
    /// </summary>
    /// <param name="baseline">Same-project latest human CA observation.</param>
    /// <param name="proposal">Freshly derived from that baseline and links.</param>
    /// <param name="campaign">Selected, validated REC campaign snapshot.</param>
    /// <returns>Conservative observations for human follow-up.</returns>
    public static ProjectCriterionRetestProgressOverview Build(
        ProjectCriterionBaselineOverview baseline,
        ProjectCriterionRetestProposal proposal,
        ProjectTestCampaign campaign)
    {
        ArgumentNullException.ThrowIfNull(baseline);
        ArgumentNullException.ThrowIfNull(proposal);
        ArgumentNullException.ThrowIfNull(campaign);
        ProjectTestCampaignMarkdownCodec.Validate(campaign);

        if (baseline.Snapshot is null ||
            baseline.Snapshot.ProjectId != campaign.ProjectId ||
            baseline.Snapshot.Sequence != proposal.BaselineSequence ||
            campaign.Id == Guid.Empty)
        {
            throw new InvalidDataException(
                "Campagne REC et référence CA incohérentes ou absentes.");
        }

        List<string> warnings = [.. proposal.Warnings];
        ProjectTestPassage? passage = campaign.Passages.LastOrDefault();
        bool older = passage is not null &&
            passage.StartedUtc < baseline.Snapshot.RecordedUtc;
        if (older)
        {
            warnings.Add("Le dernier passage est antérieur à la " +
                "référence CA enregistrée ; ses verdicts ne peuvent " +
                "pas attester une reprise de modifications ultérieures.");
        }

        Dictionary<Guid, ProjectTestFrozenDefinition> selected =
            !older && passage is not null
                ? passage.Tests.ToDictionary(item => item.TestId)
                : [];
        Dictionary<Guid, ProjectTestResultEvent> results =
            !older && passage is not null
                ? passage.Results.ToDictionary(item => item.TestId)
                : [];

        HashSet<Guid> seen = [];
        List<ProjectCriterionRetestProgressRow> rows = [];
        foreach (ProjectCriterionRetestCandidate candidate in proposal.Candidates)
        {
            if (candidate.TestId == Guid.Empty ||
                !seen.Add(candidate.TestId) ||
                candidate.CriterionIds.Count == 0 ||
                candidate.CriterionIds.Distinct().Count() !=
                    candidate.CriterionIds.Count)
            {
                throw new InvalidDataException(
                    "Proposition CA→REC dupliquée ou identité absente.");
            }

            ProjectCriterionRetestProgressState state;
            ProjectTestOutcome? outcome = null;
            bool sourceMismatch = false;
            if (passage is null)
            {
                state = ProjectCriterionRetestProgressState.NoPassage;
            }
            else if (older)
            {
                state = ProjectCriterionRetestProgressState.EarlierThanBaseline;
            }
            else if (!selected.TryGetValue(candidate.TestId,
                    out ProjectTestFrozenDefinition? frozen))
            {
                state = ProjectCriterionRetestProgressState.NotSelected;
            }
            else
            {
                sourceMismatch = !string.Equals(frozen.SourceRelativePath,
                    candidate.RelativePath, StringComparison.OrdinalIgnoreCase);
                if (results.TryGetValue(candidate.TestId,
                        out ProjectTestResultEvent? result))
                {
                    outcome = result.Outcome;
                }

                state = sourceMismatch
                    ? ProjectCriterionRetestProgressState.SourceMismatch
                    : outcome.HasValue
                        ? ProjectCriterionRetestProgressState.HumanResultDeclared
                        : ProjectCriterionRetestProgressState.SelectedWithoutResult;
            }

            rows.Add(new ProjectCriterionRetestProgressRow(
                candidate.TestId, candidate.Label, candidate.CriterionIds,
                state, !older ? passage?.Id : null,
                !older ? passage?.Number : null, outcome, sourceMismatch));
        }

        if (rows.Any(item =>
            item.State == ProjectCriterionRetestProgressState.SourceMismatch))
        {
            warnings.Add("Certaines sources figées ont changé de chemin " +
                "depuis le passage : les verdicts ne doivent pas " +
                "être rapprochés automatiquement.");
        }

        warnings.Add("Une sélection REC et un verdict historique ne " +
            "valident pas le changement CA. Vérifier les preuves et " +
            "émettre une nouvelle décision humaine, si nécessaire.");
        return new ProjectCriterionRetestProgressOverview(
            campaign.Id, proposal.BaselineSequence, rows, warnings);
    }
}
