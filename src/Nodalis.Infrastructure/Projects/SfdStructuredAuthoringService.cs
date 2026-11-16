using Nodalis.Core.Domain;
using Nodalis.Core.Sfd;
using Nodalis.Infrastructure.Persistence;

namespace Nodalis.Infrastructure.Projects;

/// <summary>Canonical RM catalog and its optimistic Markdown byte revision.</summary>
/// <param name="Rules">Identified reusable rules.</param>
/// <param name="Sha256">Current file SHA-256.</param>
public sealed record SfdBusinessRuleSnapshot(
    IReadOnlyList<SfdBusinessRule> Rules, string Sha256);

/// <summary>One requirement's persisted, nested C/SC blocks and source revision.</summary>
/// <param name="Card">Owning SFD requirement.</param>
/// <param name="Blocks">Authored C/SC/RM GUID links.</param>
/// <param name="Sha256">Original byte-level revision.</param>
public sealed record SfdRequirementBlockSnapshot(
    SfdDocumentCard Card,
    IReadOnlyList<SfdConditionBlock> Blocks,
    string Sha256);

/// <summary>
/// Human-authorized SFD C/SC/RM authoring. The single canonical RM catalog
/// owns all rule text; requirement Markdown stores only persistent RM GUIDs.
/// </summary>
public sealed class SfdStructuredAuthoringService
{
    private readonly SfdDocumentRegistryService _documents;
    private readonly ProjectJourneySourceService _sources;

    /// <summary>Creates a workspace-bound structured authoring service.</summary>
    /// <param name="workspaceRoot">Single active Nodalis workspace.</param>
    public SfdStructuredAuthoringService(string workspaceRoot)
    {
        _documents = new SfdDocumentRegistryService(workspaceRoot);
        _sources = new ProjectJourneySourceService(workspaceRoot);
    }

    /// <summary>Reads the authoritative shared RM catalogue, never creating files.</summary>
    /// <param name="projectDirectory">SFD project folder.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    /// <returns>Validated reusable rule identities.</returns>
    public async Task<SfdBusinessRuleSnapshot> ReadRulesAsync(
        string projectDirectory, CancellationToken cancellationToken = default)
    {
        await _documents.ListAsync(projectDirectory, cancellationToken);
        string path = RulesPath(projectDirectory);
        _sources.GetReferenceForExistingFile(path);
        SfdTextRevision source = await SfdMarkdownRevisionWriter.ReadAsync(
            path, cancellationToken);
        return new SfdBusinessRuleSnapshot(
            SfdBusinessRuleMarkdownCodec.Parse(source.Content), source.Sha256);
    }

    /// <summary>
    /// Adds exactly one explicitly authored rule to the unique shared RM file.
    /// Regulatory, calculation or validation categories are not inferred.
    /// </summary>
    /// <param name="projectDirectory">SFD project.</param>
    /// <param name="statement">One user-provided atomic rule.</param>
    /// <param name="source">Optional cited business source.</param>
    /// <param name="certainty">Explicitly reviewed reliability degree.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    /// <returns>Persisted rule GUID and stable RM code.</returns>
    public async Task<SfdBusinessRule> AddRuleAsync(
        string projectDirectory, string statement, string source = "",
        SfdAssertionCertainty certainty = SfdAssertionCertainty.ToConfirm,
        CancellationToken cancellationToken = default)
    {
        await EnsureActiveAsync(projectDirectory, cancellationToken);
        string path = RulesPath(projectDirectory);
        _sources.GetReferenceForExistingFile(path);
        SfdTextRevision current = await SfdMarkdownRevisionWriter.ReadAsync(
            path, cancellationToken);
        IReadOnlyList<SfdBusinessRule> rules =
            SfdBusinessRuleMarkdownCodec.Parse(current.Content);
        SfdBusinessRule rule = new()
        {
            Id = Guid.NewGuid(),
            Reference = SfdBusinessRuleMarkdownCodec.NextReference(rules),
            Statement = SfdConditionBlockCodec.RequireStatement(statement),
            Source = source.Trim(),
            Certainty = certainty
        };
        SfdBusinessRuleMarkdownCodec.Validate(rule);
        string updated = SfdBusinessRuleMarkdownCodec.Apply(current.Content,
            rules.Append(rule).ToArray());
        await SfdMarkdownRevisionWriter.ReplaceAsync(path, updated,
            current.Sha256, cancellationToken);
        return rule;
    }

    /// <summary>Reads authored C/SC under one SFD requirement GUID.</summary>
    /// <param name="projectDirectory">SFD project.</param>
    /// <param name="requirementId">Existing EXU/EXS/EXR identity.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    /// <returns>Human-readable typed structural blocks and source revision.</returns>
    public async Task<SfdRequirementBlockSnapshot> ReadRequirementAsync(
        string projectDirectory, Guid requirementId,
        CancellationToken cancellationToken = default)
    {
        (SfdDocumentEntry entry, SfdTextRevision source) = await LoadRequirementAsync(
            projectDirectory, requirementId, cancellationToken);
        return new SfdRequirementBlockSnapshot(entry.Card,
            SfdConditionBlockCodec.Parse(source.Content), source.Sha256);
    }

    /// <summary>Adds one human-authored, formally validated C, with no fabricated SC.</summary>
    /// <param name="projectDirectory">SFD project.</param>
    /// <param name="requirementId">Owning requirement GUID.</param>
    /// <param name="statement">Text matching the confirmed EXU/EXS/EXR condition pattern.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    /// <returns>Committed C identity.</returns>
    public async Task<SfdConditionBlock> AddConditionAsync(
        string projectDirectory, Guid requirementId, string statement,
        CancellationToken cancellationToken = default)
    {
        await EnsureActiveAsync(projectDirectory, cancellationToken);
        (SfdDocumentEntry entry, SfdTextRevision source) = await LoadRequirementAsync(
            projectDirectory, requirementId, cancellationToken);
        SfdConditionBlockCodec.ValidateFormalism(entry.Card.Kind,
            statement, isSubcondition: false);
        IReadOnlyList<SfdConditionBlock> blocks =
            SfdConditionBlockCodec.Parse(source.Content);
        SfdConditionBlock condition = new()
        {
            Id = Guid.NewGuid(),
            Reference = SfdConditionBlockCodec.NextReference(
                blocks.Select(block => block.Reference), "C-"),
            Statement = statement.Trim()
        };
        string updated = SfdConditionBlockCodec.Apply(source.Content,
            blocks.Append(condition).ToArray());
        await SfdMarkdownRevisionWriter.ReplaceAsync(entry.AbsolutePath,
            updated, source.Sha256, cancellationToken);
        return condition;
    }

    /// <summary>Adds one verified SC under a previously identified C, without RM by default.</summary>
    /// <param name="projectDirectory">SFD project.</param>
    /// <param name="requirementId">Owning requirement GUID.</param>
    /// <param name="conditionId">Owning C GUID.</param>
    /// <param name="statement">Human-authored EXU/EXS/EXR SC statement.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    /// <returns>Committed SC identity.</returns>
    public async Task<SfdSubconditionBlock> AddSubconditionAsync(
        string projectDirectory, Guid requirementId, Guid conditionId,
        string statement, CancellationToken cancellationToken = default)
    {
        await EnsureActiveAsync(projectDirectory, cancellationToken);
        (SfdDocumentEntry entry, SfdTextRevision source) = await LoadRequirementAsync(
            projectDirectory, requirementId, cancellationToken);
        SfdConditionBlockCodec.ValidateFormalism(entry.Card.Kind,
            statement, isSubcondition: true);
        IReadOnlyList<SfdConditionBlock> blocks =
            SfdConditionBlockCodec.Parse(source.Content);
        SfdConditionBlock parent = blocks.SingleOrDefault(block =>
            block.Id == conditionId) ??
            throw new InvalidDataException("Condition C absente de cette exigence.");

        SfdSubconditionBlock subcondition = new()
        {
            Id = Guid.NewGuid(),
            Reference = SfdConditionBlockCodec.NextReference(
                parent.Subconditions.Select(block => block.Reference), "SC-"),
            Statement = statement.Trim()
        };
        SfdConditionBlock[] updatedBlocks = blocks.Select(block =>
            block.Id == conditionId
                ? block with
                {
                    Subconditions = block.Subconditions.Append(subcondition).ToList()
                }
                : block).ToArray();
        string updated = SfdConditionBlockCodec.Apply(source.Content, updatedBlocks);
        await SfdMarkdownRevisionWriter.ReplaceAsync(entry.AbsolutePath,
            updated, source.Sha256, cancellationToken);
        return subcondition;
    }

    /// <summary>Links an existing shared RM into an existing SC by GUID, never by copy.</summary>
    /// <param name="projectDirectory">SFD project.</param>
    /// <param name="requirementId">Owning requirement GUID.</param>
    /// <param name="subconditionId">Existing SC GUID.</param>
    /// <param name="ruleId">Existing canonical RM GUID.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    /// <returns>Current reviewed requirement structure.</returns>
    public async Task<SfdRequirementBlockSnapshot> LinkRuleAsync(
        string projectDirectory, Guid requirementId, Guid subconditionId,
        Guid ruleId, CancellationToken cancellationToken = default)
    {
        await EnsureActiveAsync(projectDirectory, cancellationToken);
        SfdBusinessRuleSnapshot rules = await ReadRulesAsync(
            projectDirectory, cancellationToken);
        if (!rules.Rules.Any(rule => rule.Id == ruleId))
        {
            throw new InvalidDataException("RM absente du catalogue canonique.");
        }

        (SfdDocumentEntry entry, SfdTextRevision source) = await LoadRequirementAsync(
            projectDirectory, requirementId, cancellationToken);
        IReadOnlyList<SfdConditionBlock> blocks =
            SfdConditionBlockCodec.Parse(source.Content);
        if (!blocks.Any(condition => condition.Subconditions.Any(subcondition =>
            subcondition.Id == subconditionId)))
        {
            throw new InvalidDataException("SC inexistante dans cette exigence.");
        }

        if (blocks.Any(condition => condition.Subconditions.Any(subcondition =>
            subcondition.Id == subconditionId &&
            subcondition.BusinessRuleIds.Contains(ruleId))))
        {
            return new SfdRequirementBlockSnapshot(entry.Card, blocks, source.Sha256);
        }

        SfdConditionBlock[] updatedBlocks = blocks.Select(condition =>
            condition with
            {
                Subconditions = condition.Subconditions.Select(subcondition =>
                    subcondition.Id == subconditionId
                        ? subcondition with
                        {
                            BusinessRuleIds = subcondition.BusinessRuleIds
                                .Append(ruleId).ToList()
                        }
                        : subcondition).ToList()
            }).ToArray();
        string updated = SfdConditionBlockCodec.Apply(source.Content, updatedBlocks);
        string sha = await SfdMarkdownRevisionWriter.ReplaceAsync(
            entry.AbsolutePath, updated, source.Sha256, cancellationToken);
        return new SfdRequirementBlockSnapshot(entry.Card, updatedBlocks, sha);
    }

    /// <summary>Removes one explicit SC→RM link, keeping the shared RM unchanged.</summary>
    /// <param name="projectDirectory">SFD project.</param>
    /// <param name="requirementId">Owning requirement GUID.</param>
    /// <param name="subconditionId">Existing SC GUID.</param>
    /// <param name="ruleId">Canonical RM GUID to dissociate.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    /// <returns>Persisted structure with its updated byte revision.</returns>
    public async Task<SfdRequirementBlockSnapshot> UnlinkRuleAsync(
        string projectDirectory, Guid requirementId, Guid subconditionId,
        Guid ruleId, CancellationToken cancellationToken = default)
    {
        await EnsureActiveAsync(projectDirectory, cancellationToken);
        (SfdDocumentEntry entry, SfdTextRevision source) = await LoadRequirementAsync(
            projectDirectory, requirementId, cancellationToken);
        IReadOnlyList<SfdConditionBlock> blocks =
            SfdConditionBlockCodec.Parse(source.Content);
        SfdSubconditionBlock? matched = blocks
            .SelectMany(condition => condition.Subconditions)
            .SingleOrDefault(subcondition => subcondition.Id == subconditionId);
        if (matched is null)
        {
            throw new InvalidDataException("SC inexistante dans cette exigence.");
        }

        if (!matched.BusinessRuleIds.Contains(ruleId))
        {
            return new SfdRequirementBlockSnapshot(entry.Card, blocks, source.Sha256);
        }

        SfdConditionBlock[] updatedBlocks = blocks.Select(condition =>
            condition with
            {
                Subconditions = condition.Subconditions.Select(subcondition =>
                    subcondition.Id == subconditionId
                        ? subcondition with
                        {
                            BusinessRuleIds = subcondition.BusinessRuleIds
                                .Where(id => id != ruleId).ToList()
                        }
                        : subcondition).ToList()
            }).ToArray();
        string updated = SfdConditionBlockCodec.Apply(source.Content, updatedBlocks);
        string sha = await SfdMarkdownRevisionWriter.ReplaceAsync(
            entry.AbsolutePath, updated, source.Sha256, cancellationToken);
        return new SfdRequirementBlockSnapshot(entry.Card, updatedBlocks, sha);
    }

    /// <summary>Read-only manual completeness checks are handled by the projection service.</summary>
    /// <param name="projectDirectory">SFD project folder.</param>
    /// <param name="requirementId">User selected requirement GUID.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    /// <returns>Verified physical source and exact revision.</returns>
    private async Task<(SfdDocumentEntry, SfdTextRevision)> LoadRequirementAsync(
        string projectDirectory, Guid requirementId, CancellationToken cancellationToken)
    {
        SfdDocumentCatalog catalog = await _documents.ListAsync(
            projectDirectory, cancellationToken);
        if (catalog.Diagnostics.Count > 0)
        {
            throw new InvalidDataException(
                "Identités de fiches SFD ambiguës ; aucune écriture autorisée.");
        }

        SfdDocumentEntry entry = catalog.Entries.SingleOrDefault(item =>
            item.Card.Id == requirementId) ??
            throw new InvalidDataException("Exigence SFD introuvable.");
        if (entry.Card.Kind is not (SfdDocumentKind.UserRequirement or
            SfdDocumentKind.SystemRequirement or
            SfdDocumentKind.RegulatoryRequirement))
        {
            throw new InvalidDataException("Le document sélectionné n'est pas EXU/EXS/EXR.");
        }

        _sources.GetReferenceForExistingFile(entry.AbsolutePath);
        SfdTextRevision source = await SfdMarkdownRevisionWriter.ReadAsync(
            entry.AbsolutePath, cancellationToken);
        SfdDocumentCard card = SfdDocumentMarkdownCodec.Parse(source.Content);
        if (card.Id != entry.Card.Id || card.ProjectId != catalog.ProjectId)
        {
            throw new InvalidDataException(
                "Identité de la fiche SFD modifiée pendant la lecture.");
        }

        return (entry, source);
    }

    /// <summary>Checks that the source is an active SFD project before changing files.</summary>
    /// <param name="projectDirectory">SFD project folder.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    private async Task EnsureActiveAsync(string projectDirectory,
        CancellationToken cancellationToken)
    {
        SfdDocumentCatalog catalog = await _documents.ListAsync(
            projectDirectory, cancellationToken);
        string manifestPath = Path.Combine(Path.GetFullPath(projectDirectory),
            WorkspaceLayout.ProjectManifestFileName);
        _sources.GetReferenceForExistingFile(manifestPath);
        ProjectManifest manifest = await AtomicJsonFile.ReadAsync<ProjectManifest>(
            manifestPath, cancellationToken);
        if (manifest.Id != catalog.ProjectId || manifest.Kind != ProjectKind.WithSpecifications ||
            manifest.ArchivedUtc.HasValue || manifest.ArchiveOriginalRelativePath is not null)
        {
            throw new InvalidOperationException("Projet SFD archivé ou identité modifiée.");
        }
    }

    /// <summary>Gets the unique, known canonical RM catalog location.</summary>
    /// <param name="projectDirectory">SFD project directory.</param>
    /// <returns>Fixed shared Markdown catalog source.</returns>
    private static string RulesPath(string projectDirectory) =>
        Path.Combine(Path.GetFullPath(projectDirectory),
            "DOCUMENTATION", "liste des règles métiers.md");
}
