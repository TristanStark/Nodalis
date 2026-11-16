using Nodalis.Core.Sfd;

namespace Nodalis.Infrastructure.Projects;

/// <summary>Read-only project SFD completeness snapshot with explicit evidence gaps.</summary>
/// <param name="ProjectId">Validated project GUID.</param>
/// <param name="Findings">Gaps and risks without automatic approval or mutation.</param>
public sealed record SfdCompletionSnapshot(
    Guid ProjectId, IReadOnlyList<SfdCompletionFinding> Findings)
{
    /// <summary>Number of findings that mean an expected item is not yet documented.</summary>
    public int IncompleteCount => Findings.Count(item =>
        item.Severity == SfdCompletionSeverity.Incomplete);

    /// <summary>Number of actual identity, source or structure inconsistencies.</summary>
    public int InvalidCount => Findings.Count(item =>
        item.Severity == SfdCompletionSeverity.Invalid);
}

/// <summary>
/// Computes SFD completion without generating fictitious C, SC, RM, FCT or EMO.
/// Source confidence and document validity are independent from human approval.
/// </summary>
public sealed class SfdCompletionReviewService
{
    private readonly SfdDocumentRegistryService _documents;
    private readonly SfdStructuredAuthoringService _structured;
    private readonly SfdActivityFunctionalityService _links;
    private readonly ProjectJourneySourceService _sources;

    /// <summary>Initializes one bounded, project-isolated completeness projection.</summary>
    /// <param name="workspaceRoot">Active workspace root.</param>
    public SfdCompletionReviewService(string workspaceRoot)
    {
        _documents = new SfdDocumentRegistryService(workspaceRoot);
        _structured = new SfdStructuredAuthoringService(workspaceRoot);
        _links = new SfdActivityFunctionalityService(workspaceRoot);
        _sources = new ProjectJourneySourceService(workspaceRoot);
    }

    /// <summary>
    /// Inspects SFD business links, documented C/SC and canonical RM references.
    /// ACT without FCT remains allowed and is informational, not invalid.
    /// </summary>
    /// <param name="projectDirectory">SFD project.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    /// <returns>Deterministic read-only findings.</returns>
    public async Task<SfdCompletionSnapshot> ReviewAsync(
        string projectDirectory, CancellationToken cancellationToken = default)
    {
        SfdDocumentCatalog catalog = await _documents.ListAsync(
            projectDirectory, cancellationToken);
        List<SfdCompletionFinding> findings = [];
        foreach (string diagnostic in catalog.Diagnostics)
        {
            Add(findings, SfdCompletionSeverity.Invalid,
                catalog.ProjectId, "SFD", "SFD_IDENTITY", diagnostic);
        }

        SfdBusinessRuleSnapshot rules;
        try
        {
            rules = await _structured.ReadRulesAsync(projectDirectory,
                cancellationToken);
        }
        catch (InvalidDataException exception)
        {
            Add(findings, SfdCompletionSeverity.Invalid, catalog.ProjectId,
                "RM", "SFD_RM_CATALOG_INVALID", exception.Message);
            return new SfdCompletionSnapshot(catalog.ProjectId, findings);
        }

        SfdActivityFunctionalitySnapshot relationships;
        try
        {
            relationships = await _links.ReadAsync(projectDirectory,
                cancellationToken);
        }
        catch (InvalidDataException exception)
        {
            Add(findings, SfdCompletionSeverity.Invalid, catalog.ProjectId,
                "ACT/FCT", "SFD_LINKS_INVALID", exception.Message);
            return new SfdCompletionSnapshot(catalog.ProjectId, findings);
        }

        Dictionary<Guid, SfdDocumentEntry> byId = catalog.Entries
            .GroupBy(item => item.Card.Id).ToDictionary(group => group.Key,
                group => group.First());
        HashSet<Guid> ruleIds = rules.Rules.Select(rule => rule.Id).ToHashSet();
        HashSet<Guid> usedRules = [];

        if (!catalog.Entries.Any(item =>
            item.Card.Kind == SfdDocumentKind.Process))
        {
            Add(findings, SfdCompletionSeverity.Information,
                catalog.ProjectId, "SFD", "SFD_NO_PROCESS",
                "Aucun PM documenté : projet brouillon vide autorisé.");
        }

        foreach (SfdDocumentEntry entry in catalog.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            SfdDocumentCard card = entry.Card;
            if (card.Kind == SfdDocumentKind.Process &&
                !catalog.Entries.Any(item =>
                    item.Card.Kind == SfdDocumentKind.Activity &&
                    item.Card.ParentId == card.Id))
            {
                Add(findings, SfdCompletionSeverity.Incomplete,
                    card.Id, card.Reference, "SFD_PM_NO_ACTIVITY",
                    "Le PM n'a aucune activité ACT renseignée.");
            }

            if (card.Kind == SfdDocumentKind.Activity &&
                !relationships.Registry.Links.Any(link => link.ActivityId == card.Id))
            {
                Add(findings, SfdCompletionSeverity.Information,
                    card.Id, card.Reference, "SFD_ACT_NO_FCT",
                    "ACT sans fonctionnalité : activité manuelle, hors périmètre ou couverture à qualifier.");
            }

            if (card.Kind == SfdDocumentKind.Functionality &&
                !relationships.Registry.Links.Any(link => link.FunctionalityId == card.Id))
            {
                Add(findings, SfdCompletionSeverity.Incomplete,
                    card.Id, card.Reference, "SFD_FCT_NO_ACTIVITY",
                    "La fonctionnalité doit être reliée à au moins une activité ACT.");
            }

            if (card.Kind is not (SfdDocumentKind.UserRequirement or
                SfdDocumentKind.SystemRequirement or
                SfdDocumentKind.RegulatoryRequirement))
            {
                continue;
            }

            try
            {
                _sources.GetReferenceForExistingFile(entry.AbsolutePath);
                SfdTextRevision content = await SfdMarkdownRevisionWriter.ReadAsync(
                    entry.AbsolutePath, cancellationToken);
                SfdDocumentCard currentCard =
                    SfdDocumentMarkdownCodec.Parse(content.Content);
                if (currentCard.Id != card.Id || currentCard.ProjectId != catalog.ProjectId)
                {
                    throw new InvalidDataException("Identité de fiche modifiée.");
                }

                EvaluateRequirement(findings, card, content.Content,
                    SfdConditionBlockCodec.Parse(content.Content), ruleIds, usedRules);
            }
            catch (InvalidDataException exception)
            {
                Add(findings, SfdCompletionSeverity.Invalid,
                    card.Id, card.Reference, "SFD_BLOCKS_INVALID",
                    "Blocs structurés invalides : " + exception.Message);
            }
        }

        foreach (SfdActivityFunctionalityLink link in relationships.Registry.Links)
        {
            if (!byId.TryGetValue(link.ActivityId, out SfdDocumentEntry? activity) ||
                activity.Card.Kind != SfdDocumentKind.Activity ||
                !byId.TryGetValue(link.FunctionalityId,
                    out SfdDocumentEntry? functionality) ||
                functionality.Card.Kind != SfdDocumentKind.Functionality)
            {
                Add(findings, SfdCompletionSeverity.Invalid,
                    catalog.ProjectId, "ACT/FCT", "SFD_LINK_ORPHAN",
                    "Une relation ACT/FCT vise un objet supprimé ou de type incompatible.");
            }
        }

        foreach (SfdBusinessRule rule in rules.Rules)
        {
            if (!usedRules.Contains(rule.Id))
            {
                Add(findings, SfdCompletionSeverity.Information,
                    rule.Id, rule.Reference, "SFD_RM_UNUSED",
                    "RM déclarée mais non encore référencée par une SC.");
            }

            if (rule.Certainty == SfdAssertionCertainty.ToConfirm)
            {
                Add(findings, SfdCompletionSeverity.Information,
                    rule.Id, rule.Reference, "SFD_RM_TO_CONFIRM",
                    "RM encore à confirmer par une source et une revue métier.");
            }
        }

        return new SfdCompletionSnapshot(catalog.ProjectId,
            findings.OrderBy(item => item.Reference, StringComparer.Ordinal)
                .ThenBy(item => item.Code, StringComparer.Ordinal).ToArray());
    }

    /// <summary>Checks the confirmed EXU/EXS/EXR cardinalities without changing draft content.</summary>
    /// <param name="findings">Output list.</param>
    /// <param name="card">Validated requirement identity.</param>
    /// <param name="source">Current Markdown source.</param>
    /// <param name="blocks">Parsed C/SC/linked-RM structure.</param>
    /// <param name="ruleIds">Known canonical RM identities.</param>
    /// <param name="usedRules">Referenced canonical RM identities, for orphan diagnostics.</param>
    private static void EvaluateRequirement(
        List<SfdCompletionFinding> findings, SfdDocumentCard card,
        string source, IReadOnlyList<SfdConditionBlock> blocks,
        HashSet<Guid> ruleIds, HashSet<Guid> usedRules)
    {
        if (blocks.Count == 0)
        {
            Add(findings, SfdCompletionSeverity.Incomplete,
                card.Id, card.Reference, "SFD_C_MISSING",
                "Exigence sans condition C (1..N attendue à complétude).");
        }

        foreach (SfdConditionBlock condition in blocks)
        {
            try
            {
                SfdConditionBlockCodec.ValidateFormalism(card.Kind,
                    condition.Statement, isSubcondition: false);
            }
            catch (InvalidDataException)
            {
                Add(findings, SfdCompletionSeverity.Invalid,
                    card.Id, card.Reference, "SFD_C_FORMALISM",
                    condition.Reference + " ne suit pas le patron confirmé.");
            }

            if (condition.Subconditions.Count == 0)
            {
                Add(findings, SfdCompletionSeverity.Incomplete,
                    card.Id, card.Reference, "SFD_SC_MISSING",
                    condition.Reference + " n'a aucune sous-condition SC.");
            }

            foreach (SfdSubconditionBlock subcondition in condition.Subconditions)
            {
                try
                {
                    SfdConditionBlockCodec.ValidateFormalism(card.Kind,
                        subcondition.Statement, isSubcondition: true);
                }
                catch (InvalidDataException)
                {
                    Add(findings, SfdCompletionSeverity.Invalid,
                        card.Id, card.Reference, "SFD_SC_FORMALISM",
                        subcondition.Reference + " ne suit pas le patron SC confirmé.");
                }

                if (card.Kind == SfdDocumentKind.UserRequirement &&
                    subcondition.Statement.Contains("[CANAL À CONFIRMER]",
                        StringComparison.Ordinal))
                {
                    Add(findings, SfdCompletionSeverity.Incomplete,
                        card.Id, card.Reference, "SFD_SC_CHANNEL_TO_CONFIRM",
                        subcondition.Reference + " contient un canal non confirmé.");
                }

                if (subcondition.BusinessRuleIds.Count == 0)
                {
                    Add(findings, SfdCompletionSeverity.Incomplete,
                        card.Id, card.Reference, "SFD_RM_MISSING",
                        condition.Reference + "/" + subcondition.Reference +
                        " ne référence aucune RM (1..N attendu).");
                }

                foreach (Guid ruleId in subcondition.BusinessRuleIds)
                {
                    usedRules.Add(ruleId);
                    if (!ruleIds.Contains(ruleId))
                    {
                        Add(findings, SfdCompletionSeverity.Invalid,
                            card.Id, card.Reference, "SFD_RM_UNRESOLVED",
                            "Référence RM absente : " + ruleId.ToString("D"));
                    }
                }
            }
        }

        if (card.Kind == SfdDocumentKind.RegulatoryRequirement &&
            !HasRegulatorySource(source))
        {
            Add(findings,
                card.Certainty == SfdAssertionCertainty.Confirmed
                    ? SfdCompletionSeverity.Invalid
                    : SfdCompletionSeverity.Incomplete,
                card.Id, card.Reference, "SFD_EXR_SOURCE_MISSING",
                "EXR sans source réglementaire applicable vérifiable : à confirmer.");
        }
    }

    /// <summary>Finds authored text under the regulatory-source heading, excluding template hints.</summary>
    /// <param name="markdown">Complete source Markdown.</param>
    /// <returns>Whether an explicit non-placeholder source is present.</returns>
    private static bool HasRegulatorySource(string markdown)
    {
        string[] lines = markdown.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n').Split('\n');
        int header = Array.FindIndex(lines, line =>
            line == "## Source réglementaire applicable");
        if (header < 0)
        {
            return false;
        }

        for (int index = header + 1; index < lines.Length; index++)
        {
            string line = lines[index].Trim();
            if (line.StartsWith("## ", StringComparison.Ordinal))
            {
                return false;
            }

            if (line.Length > 0 && !line.StartsWith("_À confirmer",
                StringComparison.Ordinal) &&
                !line.StartsWith("<!--", StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Adds one typed diagnostic with stable machine code and human guidance.</summary>
    /// <param name="findings">Result collection.</param>
    /// <param name="severity">Information, incomplete or invalid.</param>
    /// <param name="id">Owning canonical identity.</param>
    /// <param name="reference">Visible object reference.</param>
    /// <param name="code">Stable diagnostic code.</param>
    /// <param name="message">Human-facing explanation.</param>
    private static void Add(List<SfdCompletionFinding> findings,
        SfdCompletionSeverity severity, Guid id, string reference,
        string code, string message)
    {
        findings.Add(new SfdCompletionFinding
        {
            Severity = severity, DocumentId = id,
            Reference = reference, Code = code, Message = message
        });
    }
}
