using System.Globalization;
using Nodalis.Core.Decisions;
using Nodalis.Core.Links;
using Nodalis.Core.Templates;
using Nodalis.Infrastructure.Links;
using Nodalis.Infrastructure.Persistence;
using Nodalis.Infrastructure.Reliability;
using Nodalis.Infrastructure.Templates;

namespace Nodalis.Infrastructure.Decisions;

public sealed class WorkspaceDecisionService
{
    public const string DecisionsDirectoryName = "Décisions";
    public const string ActiveStatus = "Active";
    public const string SupersededStatus = "Superseded";
    public const string DeprecatedStatus = "Deprecated";

    private readonly string _workspaceRoot;
    private readonly WorkspaceLinkIndexService _linkIndex;
    private readonly FileSystemTemplateStore _templateStore;

    /// <summary>
    /// Initializes a new instance of <see cref="WorkspaceDecisionService"/>.
    /// </summary>
    /// <param name="workspaceRoot">The <c>workspaceRoot</c> value.</param>
    public WorkspaceDecisionService(string workspaceRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRoot);

        _workspaceRoot = Path.GetFullPath(workspaceRoot);
        _linkIndex = new WorkspaceLinkIndexService(_workspaceRoot);
        _templateStore = new FileSystemTemplateStore(_workspaceRoot);
    }

    /// <summary>
    /// Performs the <c>CreateAsync</c> operation.
    /// </summary>
    /// <param name="contextPath">The <c>contextPath</c> value.</param>
    /// <param name="draft">The <c>draft</c> value.</param>
    /// <param name="sourceDocumentPath">The <c>sourceDocumentPath</c> value.</param>
    /// <param name="cancellationToken">The <c>cancellationToken</c> value.</param>
    /// <returns>The result of the operation.</returns>
    public async Task<DecisionCreationResult> CreateAsync(
            string? contextPath,
            DecisionDraft draft,
            string? sourceDocumentPath = null,
            CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(draft);
        ArgumentException.ThrowIfNullOrWhiteSpace(draft.Title);
        ArgumentException.ThrowIfNullOrWhiteSpace(draft.Decision);

        global::Nodalis.Core.Links.LinkIndexCatalog links = await _linkIndex.RefreshAsync(cancellationToken);
        (global::Nodalis.Core.Links.LinkTargetEntry Target, string KindLabel) scope = ResolveScope(contextPath, links)
            ?? throw new InvalidOperationException(
                "Sélectionnez une application, un projet ou un document rattaché avant de créer une décision.");

        string scopeDirectory = ResolveWorkspacePath(
            scope.Target.RelativePath);
        string decisionsDirectory = Path.Combine(
            scopeDirectory,
            DecisionsDirectoryName);

        Directory.CreateDirectory(decisionsDirectory);

        global::Nodalis.Core.Links.LinkTargetEntry? source = ResolveSourceTarget(
            sourceDocumentPath,
            links);

        global::System.Guid documentId = Guid.NewGuid();
        global::System.Collections.Generic.Dictionary<string, string> variables = MarkdownTemplateRenderer.CreateStandardVariables(
            draft.Title.Trim(),
            documentId,
            DateTimeOffset.Now,
            new Dictionary<string, string>(
                StringComparer.OrdinalIgnoreCase)
            {
                ["context.name"] = scope.Target.DisplayName,
                ["context.kind"] = scope.KindLabel
            });

        variables["date"] =
            draft.Date.ToString(
                "yyyy-MM-dd",
                CultureInfo.InvariantCulture);

        string content = await _templateStore.RenderAsync(
            "decision",
            variables,
            cancellationToken);

        content = InsertMetadata(
            content,
            scope.KindLabel,
            scope.Target.DisplayName,
            draft.Status,
            source?.QualifiedName);

        content = ReplaceSection(
            content,
            "Décision",
            draft.Decision);
        content = ReplaceSection(
            content,
            "Contexte",
            draft.Context);
        content = ReplaceSection(
            content,
            "Justification",
            draft.Justification);
        content = ReplaceSection(
            content,
            "Impacts",
            draft.Impacts);

        string linksBody = BuildLinksBody(
            draft.Links,
            source);

        content = ReplaceSection(
            content,
            "Sources et liens",
            linksBody);

        string safeTitle = WindowsPathRules.SanitizeSegment(
            draft.Title.Trim());

        string filePath = WindowsPathRules.GetUniqueFilePath(
            decisionsDirectory,
            $"{draft.Date:yyyy-MM-dd} - {safeTitle}.md");

        await AtomicFileWriter.WriteAllTextAsync(
            filePath,
            EnsureTrailingNewline(content),
            cancellationToken);

        return new DecisionCreationResult
        {
            FilePath = filePath,
            ScopeName = scope.Target.DisplayName,
            ScopeKind = scope.KindLabel
        };
    }

    /// <summary>
    /// Performs the <c>GetDecisionsAsync</c> operation.
    /// </summary>
    /// <param name="contextPath">The <c>contextPath</c> value.</param>
    /// <param name="cancellationToken">The <c>cancellationToken</c> value.</param>
    /// <returns>The result of the operation.</returns>
    public async Task<IReadOnlyList<DecisionRecord>> GetDecisionsAsync(
            string? contextPath,
            CancellationToken cancellationToken = default)
    {
        global::Nodalis.Core.Links.LinkIndexCatalog links = await _linkIndex.RefreshAsync(cancellationToken);
        (global::Nodalis.Core.Links.LinkTargetEntry Target, string KindLabel)? scope = ResolveScope(contextPath, links);

        if (scope is null)
        {
            return [];
        }

        string directory = Path.Combine(
            ResolveWorkspacePath(scope.Value.Target.RelativePath),
            DecisionsDirectoryName);

        if (!Directory.Exists(directory))
        {
            return [];
        }

        global::System.Collections.Generic.List<global::Nodalis.Core.Decisions.DecisionRecord> result = new List<DecisionRecord>();

        foreach (string filePath in Directory.EnumerateFiles(
                     directory,
                     "*.md",
                     SearchOption.TopDirectoryOnly))
        {
            cancellationToken.ThrowIfCancellationRequested();

            string content = await File.ReadAllTextAsync(
                filePath,
                cancellationToken);

            result.Add(
                ParseDecision(
                    filePath,
                    content,
                    scope.Value.KindLabel,
                    scope.Value.Target.DisplayName));
        }

        IReadOnlyList<DecisionRecord> validated =
            ApplyLifecycleWarnings(
                result,
                links);

        return validated
            .OrderByDescending(item => item.Date ?? DateOnly.MinValue)
            .ThenBy(
                item => item.Title,
                StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
    }

    /// <summary>
    /// Performs the <c>SearchAsync</c> operation.
    /// </summary>
    /// <param name="contextPath">The <c>contextPath</c> value.</param>
    /// <param name="query">The <c>query</c> value.</param>
    /// <param name="cancellationToken">The <c>cancellationToken</c> value.</param>
    /// <returns>The result of the operation.</returns>
    public async Task<IReadOnlyList<DecisionRecord>> SearchAsync(
            string? contextPath,
            string query,
            CancellationToken cancellationToken = default)
    {
        global::System.Collections.Generic.IReadOnlyList<global::Nodalis.Core.Decisions.DecisionRecord> decisions = await GetDecisionsAsync(
            contextPath,
            cancellationToken);

        if (string.IsNullOrWhiteSpace(query))
        {
            return decisions;
        }

        string needle = query.Trim();

        return decisions
            .Where(item =>
                Contains(item.Title, needle) ||
                Contains(item.Status, needle) ||
                Contains(item.Decision, needle) ||
                Contains(item.Context, needle) ||
                Contains(item.Justification, needle) ||
                Contains(item.Impacts, needle) ||
                Contains(item.SourceReference, needle) ||
                Contains(item.Links, needle) ||
                Contains(item.SupersedesReference, needle) ||
                Contains(item.SupersededByReference, needle) ||
                Contains(item.LifecycleWarning, needle))
            .ToArray();
    }

    /// <summary>
    /// Updates the lifecycle status of one Decision Record without deleting or rewriting its history.
    /// </summary>
    /// <param name="decision">The Decision Record to update.</param>
    /// <param name="status">The canonical lifecycle status.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task representing the local Markdown update.</returns>
    public async Task SetLifecycleStatusAsync(
            DecisionRecord decision,
            string status,
            CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(
            decision);

        string canonicalStatus =
            NormalizeLifecycleStatus(
                status);

        if (canonicalStatus == ActiveStatus &&
            !string.IsNullOrWhiteSpace(
                decision.SupersededByReference))
        {
            throw new InvalidOperationException(
                "Cette décision pointe déjà vers une remplaçante. Corrigez d'abord la chaîne de remplacement avant de la réactiver.");
        }

        string path =
            ResolveDecisionPath(
                decision);

        TextDocumentSession session =
            await TextDocumentSession.OpenAsync(
                path,
                cancellationToken);

        string updated =
            UpsertMetadata(
                session.Content,
                "Statut",
                canonicalStatus);

        await session.SaveAsync(
            EnsureTrailingNewline(
                updated),
            cancellationToken);

        await _linkIndex.RefreshAsync(
            cancellationToken);
    }

    /// <summary>
    /// Links two Decision Records as an explicit replacement while preserving both Markdown documents.
    /// </summary>
    /// <param name="previous">The decision that becomes superseded.</param>
    /// <param name="replacement">The decision that becomes active and replaces the previous decision.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task representing the bidirectional lifecycle update.</returns>
    public async Task SupersedeAsync(
            DecisionRecord previous,
            DecisionRecord replacement,
            CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(
            previous);
        ArgumentNullException.ThrowIfNull(
            replacement);

        if (string.Equals(
                previous.SourceRelativePath,
                replacement.SourceRelativePath,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "Une décision ne peut pas se remplacer elle-même.");
        }

        if (!string.Equals(
                previous.ScopeKind,
                replacement.ScopeKind,
                StringComparison.CurrentCultureIgnoreCase) ||
            !string.Equals(
                previous.ScopeName,
                replacement.ScopeName,
                StringComparison.CurrentCultureIgnoreCase))
        {
            throw new InvalidOperationException(
                "Les deux décisions doivent appartenir au même contexte.");
        }

        LinkIndexCatalog links =
            await _linkIndex.RefreshAsync(
                cancellationToken);

        LinkTargetEntry previousTarget =
            ResolveDecisionTarget(
                previous,
                links);
        LinkTargetEntry replacementTarget =
            ResolveDecisionTarget(
                replacement,
                links);

        if (!string.IsNullOrWhiteSpace(
                previous.SupersededByReference))
        {
            LinkTargetEntry? existingReplacement =
                ResolveDecisionReferenceTarget(
                    previous.SupersededByReference,
                    links);

            if (existingReplacement is null ||
                existingReplacement.Id != replacementTarget.Id)
            {
                throw new InvalidOperationException(
                    "La décision source est déjà remplacée par une autre décision.");
            }
        }

        if (!string.IsNullOrWhiteSpace(
                replacement.SupersedesReference))
        {
            LinkTargetEntry? existingPrevious =
                ResolveDecisionReferenceTarget(
                    replacement.SupersedesReference,
                    links);

            if (existingPrevious is null ||
                existingPrevious.Id != previousTarget.Id)
            {
                throw new InvalidOperationException(
                    "La décision remplaçante remplace déjà une autre décision.");
            }
        }

        if (!string.IsNullOrWhiteSpace(
                replacement.SupersededByReference))
        {
            throw new InvalidOperationException(
                "Une décision déjà remplacée ne peut pas devenir la remplaçante active.");
        }

        string previousPath =
            ResolveDecisionPath(
                previous);
        string replacementPath =
            ResolveDecisionPath(
                replacement);

        TextDocumentSession previousSession =
            await TextDocumentSession.OpenAsync(
                previousPath,
                cancellationToken);
        TextDocumentSession replacementSession =
            await TextDocumentSession.OpenAsync(
                replacementPath,
                cancellationToken);
        string originalPreviousContent =
            previousSession.Content;

        string previousContent =
            UpsertMetadata(
                previousSession.Content,
                "Statut",
                SupersededStatus);
        previousContent =
            UpsertMetadata(
                previousContent,
                "Remplacée par",
                $"[[{replacementTarget.QualifiedName}]]");

        string replacementContent =
            UpsertMetadata(
                replacementSession.Content,
                "Statut",
                ActiveStatus);
        replacementContent =
            UpsertMetadata(
                replacementContent,
                "Remplace",
                $"[[{previousTarget.QualifiedName}]]");

        await previousSession.SaveAsync(
            EnsureTrailingNewline(
                previousContent),
            cancellationToken);

        try
        {
            await replacementSession.SaveAsync(
                EnsureTrailingNewline(
                    replacementContent),
                cancellationToken);
        }
        catch
        {
            await previousSession.SaveAsync(
                originalPreviousContent,
                cancellationToken);
            throw;
        }

        await _linkIndex.RefreshAsync(
            cancellationToken);
    }

    /// <summary>
    /// Performs the <c>ExtractDecisionCandidatesAsync</c> operation.
    /// </summary>
    /// <param name="sourceDocumentPath">The <c>sourceDocumentPath</c> value.</param>
    /// <param name="cancellationToken">The <c>cancellationToken</c> value.</param>
    /// <returns>The result of the operation.</returns>
    public async Task<IReadOnlyList<string>> ExtractDecisionCandidatesAsync(
            string sourceDocumentPath,
            CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceDocumentPath);

        string fullPath = Path.GetFullPath(sourceDocumentPath);

        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException(
                "Le document source est introuvable.",
                fullPath);
        }

        if (!IsWithinWorkspace(fullPath))
        {
            throw new InvalidDataException(
                "Le document source se trouve hors du workspace.");
        }

        string content = await File.ReadAllTextAsync(
            fullPath,
            cancellationToken);

        string section = ReadSection(
            content,
            "Décisions");

        if (string.IsNullOrWhiteSpace(section))
        {
            return [];
        }

        return NormalizeNewlines(section)
            .Split(
                '\n',
                StringSplitOptions.TrimEntries |
                StringSplitOptions.RemoveEmptyEntries)
            .Select(StripBullet)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
    }

    /// <summary>
    /// Performs the <c>ResolveScopeAsync</c> operation.
    /// </summary>
    /// <param name="contextPath">The <c>contextPath</c> value.</param>
    /// <param name="cancellationToken">The <c>cancellationToken</c> value.</param>
    /// <returns>The result of the operation.</returns>
    public async Task<(string ScopeKind, string ScopeName)?> ResolveScopeAsync(
            string? contextPath,
            CancellationToken cancellationToken = default)
    {
        global::Nodalis.Core.Links.LinkIndexCatalog links = await _linkIndex.RefreshAsync(cancellationToken);
        (global::Nodalis.Core.Links.LinkTargetEntry Target, string KindLabel)? scope = ResolveScope(contextPath, links);

        if (scope is null)
        {
            return null;
        }

        (global::Nodalis.Core.Links.LinkTargetEntry Target, string KindLabel) resolved = scope.Value;

        return (
            resolved.KindLabel,
            resolved.Target.DisplayName);
    }

    /// <summary>
    /// Performs the <c>ResolveScope</c> operation.
    /// </summary>
    /// <param name="contextPath">The <c>contextPath</c> value.</param>
    /// <param name="links">The <c>links</c> value.</param>
    /// <returns>The result of the operation.</returns>
    private (LinkTargetEntry Target, string KindLabel)? ResolveScope(
            string? contextPath,
            LinkIndexCatalog links)
    {
        if (string.IsNullOrWhiteSpace(contextPath))
        {
            return null;
        }

        string fullPath = Path.GetFullPath(contextPath);

        if (!IsWithinWorkspace(fullPath))
        {
            throw new InvalidDataException(
                "Le contexte de décision se trouve hors du workspace.");
        }

        string contextDirectory = File.Exists(fullPath)
            ? Path.GetDirectoryName(fullPath) ?? _workspaceRoot
            : fullPath;

        string relativePath = NormalizeRelativePath(
            Path.GetRelativePath(
                _workspaceRoot,
                contextDirectory));

        global::Nodalis.Core.Links.LinkTargetEntry? project = links.Targets
            .Where(target =>
                target.Kind == LinkTargetKind.Project &&
                IsRelativeAncestorOrEqual(
                    target.RelativePath,
                    relativePath))
            .OrderByDescending(target =>
                target.RelativePath.Length)
            .FirstOrDefault();

        if (project is not null)
        {
            return (project, "Projet");
        }

        global::Nodalis.Core.Links.LinkTargetEntry? application = links.Targets
            .Where(target =>
                target.Kind == LinkTargetKind.Application &&
                IsRelativeAncestorOrEqual(
                    target.RelativePath,
                    relativePath))
            .OrderByDescending(target =>
                target.RelativePath.Length)
            .FirstOrDefault();

        return application is null
            ? null
            : (application, "Application");
    }

    /// <summary>
    /// Performs the <c>ResolveSourceTarget</c> operation.
    /// </summary>
    /// <param name="sourceDocumentPath">The <c>sourceDocumentPath</c> value.</param>
    /// <param name="links">The <c>links</c> value.</param>
    /// <returns>The result of the operation.</returns>
    private LinkTargetEntry? ResolveSourceTarget(
            string? sourceDocumentPath,
            LinkIndexCatalog links)
    {
        if (string.IsNullOrWhiteSpace(sourceDocumentPath))
        {
            return null;
        }

        string fullPath = Path.GetFullPath(sourceDocumentPath);

        if (!File.Exists(fullPath) ||
            !IsWithinWorkspace(fullPath))
        {
            return null;
        }

        string relativePath = NormalizeRelativePath(
            Path.GetRelativePath(
                _workspaceRoot,
                fullPath));

        return links.Targets.FirstOrDefault(target =>
            target.Kind == LinkTargetKind.Document &&
            string.Equals(
                target.RelativePath,
                relativePath,
                StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Performs the <c>ParseDecision</c> operation.
    /// </summary>
    /// <param name="filePath">The <c>filePath</c> value.</param>
    /// <param name="content">The <c>content</c> value.</param>
    /// <param name="scopeKind">The <c>scopeKind</c> value.</param>
    /// <param name="scopeName">The <c>scopeName</c> value.</param>
    /// <returns>The result of the operation.</returns>
    private DecisionRecord ParseDecision(
            string filePath,
            string content,
            string scopeKind,
            string scopeName)
    {
        string[] lines = NormalizeNewlines(content)
            .Split('\n');

        string? titleLine = lines.FirstOrDefault(line =>
            line.StartsWith(
                "# ",
                StringComparison.Ordinal));

        string title = titleLine is null
            ? Path.GetFileNameWithoutExtension(filePath)
            : titleLine[2..].Trim();

        if (title.StartsWith(
                "Décision — ",
                StringComparison.CurrentCultureIgnoreCase))
        {
            title = title["Décision — ".Length..].Trim();
        }

        string dateText = ReadMetadata(lines, "Date");
        DateOnly? date = null;

        if (DateOnly.TryParseExact(
                dateText,
                "yyyy-MM-dd",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out global::System.DateOnly parsedDate))
        {
            date = parsedDate;
        }

        return new DecisionRecord
        {
            SourceRelativePath = NormalizeRelativePath(
                Path.GetRelativePath(
                    _workspaceRoot,
                    filePath)),
            Title = title,
            Date = date,
            Status = ReadMetadata(lines, "Statut"),
            LifecycleState = ParseLifecycleState(
                ReadMetadata(
                    lines,
                    "Statut")),
            ScopeName = scopeName,
            ScopeKind = scopeKind,
            SupersedesReference = ReadMetadata(
                lines,
                "Remplace"),
            SupersededByReference = ReadMetadata(
                lines,
                "Remplacée par"),
            Decision = ReadSection(content, "Décision"),
            Context = ReadSection(content, "Contexte"),
            Justification = ReadSection(content, "Justification"),
            Impacts = ReadSection(content, "Impacts"),
            SourceReference = ReadMetadata(lines, "Source"),
            Links = ReadSection(content, "Sources et liens")
        };
    }

    /// <summary>
    /// Applies relationship and chain consistency warnings to parsed Decision Records.
    /// </summary>
    /// <param name="decisions">The parsed decisions in one scope.</param>
    /// <param name="links">The current workspace link index.</param>
    /// <returns>Decision Records decorated with lifecycle warnings.</returns>
    private static IReadOnlyList<DecisionRecord> ApplyLifecycleWarnings(
            IReadOnlyList<DecisionRecord> decisions,
            LinkIndexCatalog links)
    {
        Dictionary<string, DecisionRecord> decisionsByPath =
            decisions.ToDictionary(
                decision =>
                    decision.SourceRelativePath,
                StringComparer.OrdinalIgnoreCase);

        global::System.Collections.Generic.List<DecisionRecord> result =
            new List<DecisionRecord>(
                decisions.Count);

        foreach (DecisionRecord decision in decisions)
        {
            global::System.Collections.Generic.List<string> warnings =
                new List<string>();

            LinkTargetEntry? previousTarget =
                ResolveDecisionReferenceTarget(
                    decision.SupersedesReference,
                    links);
            LinkTargetEntry? nextTarget =
                ResolveDecisionReferenceTarget(
                    decision.SupersededByReference,
                    links);

            DecisionRecord? previousDecision =
                ResolveDecisionRecord(
                    previousTarget,
                    decisionsByPath);
            DecisionRecord? nextDecision =
                ResolveDecisionRecord(
                    nextTarget,
                    decisionsByPath);

            if (decision.LifecycleState == DecisionLifecycleState.Superseded &&
                string.IsNullOrWhiteSpace(
                    decision.SupersededByReference))
            {
                warnings.Add(
                    "Statut Superseded sans « Remplacée par ».");
            }

            if (decision.LifecycleState != DecisionLifecycleState.Superseded &&
                !string.IsNullOrWhiteSpace(
                    decision.SupersededByReference))
            {
                warnings.Add(
                    "Une relation « Remplacée par » existe alors que le statut n'est pas Superseded.");
            }

            if (!string.IsNullOrWhiteSpace(
                    decision.SupersedesReference) &&
                previousDecision is null)
            {
                warnings.Add(
                    "La décision référencée par « Remplace » est introuvable dans ce contexte.");
            }

            if (!string.IsNullOrWhiteSpace(
                    decision.SupersededByReference) &&
                nextDecision is null)
            {
                warnings.Add(
                    "La décision référencée par « Remplacée par » est introuvable dans ce contexte.");
            }

            if (previousDecision is not null)
            {
                LinkTargetEntry? reverseTarget =
                    ResolveDecisionReferenceTarget(
                        previousDecision.SupersededByReference,
                        links);

                LinkTargetEntry? currentTarget =
                    FindDecisionTarget(
                        decision,
                        links);

                if (currentTarget is null ||
                    reverseTarget is null ||
                    reverseTarget.Id != currentTarget.Id)
                {
                    warnings.Add(
                        "La relation « Remplace » n'est pas réciproque.");
                }

                if (previousDecision.LifecycleState != DecisionLifecycleState.Superseded)
                {
                    warnings.Add(
                        "La décision remplacée n'est pas au statut Superseded.");
                }
            }

            if (nextDecision is not null)
            {
                LinkTargetEntry? reverseTarget =
                    ResolveDecisionReferenceTarget(
                        nextDecision.SupersedesReference,
                        links);

                LinkTargetEntry? currentTarget =
                    FindDecisionTarget(
                        decision,
                        links);

                if (currentTarget is null ||
                    reverseTarget is null ||
                    reverseTarget.Id != currentTarget.Id)
                {
                    warnings.Add(
                        "La relation « Remplacée par » n'est pas réciproque.");
                }
            }

            if (HasLifecycleCycle(
                    decision,
                    decisionsByPath,
                    links))
            {
                warnings.Add(
                    "Cycle de remplacement détecté.");
            }

            result.Add(
                decision with
                {
                    LifecycleWarning =
                        string.Join(
                            " ",
                            warnings.Distinct(
                                StringComparer.CurrentCultureIgnoreCase))
                });
        }

        return result;
    }

    /// <summary>
    /// Detects a cycle by following successive « Remplacée par » references.
    /// </summary>
    /// <param name="start">The decision from which traversal starts.</param>
    /// <param name="decisionsByPath">Decision records indexed by source path.</param>
    /// <param name="links">The current workspace link index.</param>
    /// <returns><see langword="true"/> when the replacement chain loops.</returns>
    private static bool HasLifecycleCycle(
            DecisionRecord start,
            IReadOnlyDictionary<string, DecisionRecord> decisionsByPath,
            LinkIndexCatalog links)
    {
        global::System.Collections.Generic.HashSet<string> visited =
            new HashSet<string>(
                StringComparer.OrdinalIgnoreCase);
        DecisionRecord? current =
            start;

        while (current is not null &&
               !string.IsNullOrWhiteSpace(
                   current.SupersededByReference))
        {
            if (!visited.Add(
                    current.SourceRelativePath))
            {
                return true;
            }

            LinkTargetEntry? nextTarget =
                ResolveDecisionReferenceTarget(
                    current.SupersededByReference,
                    links);

            current =
                ResolveDecisionRecord(
                    nextTarget,
                    decisionsByPath);
        }

        return false;
    }

    /// <summary>
    /// Resolves one lifecycle link to a parsed decision in the current scope.
    /// </summary>
    /// <param name="target">The resolved link target.</param>
    /// <param name="decisionsByPath">Decision records indexed by source path.</param>
    /// <returns>The matching decision, or <see langword="null"/>.</returns>
    private static DecisionRecord? ResolveDecisionRecord(
            LinkTargetEntry? target,
            IReadOnlyDictionary<string, DecisionRecord> decisionsByPath)
    {
        if (target is null)
        {
            return null;
        }

        return decisionsByPath.TryGetValue(
            target.RelativePath,
            out DecisionRecord? decision)
            ? decision
            : null;
    }

    /// <summary>
    /// Resolves a Decision Record to its current indexed document target.
    /// </summary>
    /// <param name="decision">The Decision Record.</param>
    /// <param name="links">The current workspace link index.</param>
    /// <returns>The indexed document target.</returns>
    private static LinkTargetEntry ResolveDecisionTarget(
            DecisionRecord decision,
            LinkIndexCatalog links) =>
            FindDecisionTarget(
                decision,
                links)
            ?? throw new InvalidDataException(
                $"Le Decision Record n'est plus indexé : {decision.SourceRelativePath}");

    /// <summary>
    /// Finds a Decision Record in the current link index.
    /// </summary>
    /// <param name="decision">The Decision Record.</param>
    /// <param name="links">The current workspace link index.</param>
    /// <returns>The indexed document target, or <see langword="null"/>.</returns>
    private static LinkTargetEntry? FindDecisionTarget(
            DecisionRecord decision,
            LinkIndexCatalog links) =>
            links.Targets.FirstOrDefault(target =>
                target.Kind == LinkTargetKind.Document &&
                string.Equals(
                    target.RelativePath,
                    decision.SourceRelativePath,
                    StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Resolves a readable lifecycle metadata reference through the workspace link index.
    /// </summary>
    /// <param name="reference">The raw metadata reference, with or without wiki-link delimiters.</param>
    /// <param name="links">The current workspace link index.</param>
    /// <returns>The resolved target, or <see langword="null"/> when missing or ambiguous.</returns>
    private static LinkTargetEntry? ResolveDecisionReferenceTarget(
            string reference,
            LinkIndexCatalog links)
    {
        string target =
            StripWikiLink(
                reference);

        if (string.IsNullOrWhiteSpace(
                target))
        {
            return null;
        }

        LinkResolution resolution =
            WorkspaceLinkIndexService.Resolve(
                links,
                target);

        return resolution.Status == LinkResolutionStatus.Resolved &&
               resolution.Target?.Kind == LinkTargetKind.Document
            ? resolution.Target
            : null;
    }

    /// <summary>
    /// Removes wiki-link delimiters from one metadata reference.
    /// </summary>
    /// <param name="reference">The raw reference.</param>
    /// <returns>The internal link target text.</returns>
    private static string StripWikiLink(
            string reference)
    {
        string value =
            reference.Trim();

        if (value.StartsWith(
                "[[",
                StringComparison.Ordinal) &&
            value.EndsWith(
                "]]",
                StringComparison.Ordinal) &&
            value.Length > 4)
        {
            return value[2..^2].Trim();
        }

        return value;
    }

    /// <summary>
    /// Converts legacy and canonical status labels to a lifecycle state.
    /// </summary>
    /// <param name="status">The status text stored in Markdown.</param>
    /// <returns>The interpreted lifecycle state.</returns>
    private static DecisionLifecycleState ParseLifecycleState(
            string status)
    {
        string normalized =
            status.Trim()
                .ToLowerInvariant();

        return normalized switch
        {
            "active" or
            "actée" or
            "actee" =>
                DecisionLifecycleState.Active,
            "superseded" or
            "remplacée" or
            "remplacee" =>
                DecisionLifecycleState.Superseded,
            "deprecated" or
            "dépréciée" or
            "depreciee" or
            "déprécié" or
            "deprecie" =>
                DecisionLifecycleState.Deprecated,
            _ =>
                DecisionLifecycleState.Other
        };
    }

    /// <summary>
    /// Validates and canonicalizes a requested lifecycle status.
    /// </summary>
    /// <param name="status">The requested status.</param>
    /// <returns>The canonical English lifecycle value.</returns>
    private static string NormalizeLifecycleStatus(
            string status)
    {
        DecisionLifecycleState state =
            ParseLifecycleState(
                status);

        return state switch
        {
            DecisionLifecycleState.Active =>
                ActiveStatus,
            DecisionLifecycleState.Superseded =>
                SupersededStatus,
            DecisionLifecycleState.Deprecated =>
                DeprecatedStatus,
            _ =>
                throw new ArgumentException(
                    "Le statut doit être Active, Superseded ou Deprecated.",
                    nameof(status))
        };
    }

    /// <summary>
    /// Updates or inserts one readable metadata line before the first level-two section.
    /// </summary>
    /// <param name="content">The Decision Record Markdown content.</param>
    /// <param name="key">The metadata key.</param>
    /// <param name="value">The metadata value.</param>
    /// <returns>The updated Markdown content.</returns>
    private static string UpsertMetadata(
            string content,
            string key,
            string value)
    {
        global::System.Collections.Generic.List<string> lines =
            NormalizeNewlines(
                    content)
                .Split(
                    '\n')
                .ToList();
        string prefix =
            $"**{key} :**";

        int existingIndex =
            lines.FindIndex(line =>
                line.TrimStart().StartsWith(
                    prefix,
                    StringComparison.CurrentCultureIgnoreCase));

        string metadata =
            $"{prefix} {value.Trim()}";

        if (existingIndex >= 0)
        {
            lines[existingIndex] =
                metadata;
            return string.Join(
                "\n",
                lines);
        }

        int headingIndex =
            lines.FindIndex(line =>
                line.TrimStart().StartsWith(
                    "## ",
                    StringComparison.Ordinal));

        int insertIndex =
            headingIndex >= 0
                ? headingIndex
                : lines.Count;

        while (insertIndex > 0 &&
               string.IsNullOrWhiteSpace(
                   lines[insertIndex - 1]))
        {
            insertIndex--;
        }

        lines.Insert(
            insertIndex,
            metadata);

        if (insertIndex + 1 < lines.Count &&
            !string.IsNullOrWhiteSpace(
                lines[insertIndex + 1]))
        {
            lines.Insert(
                insertIndex + 1,
                string.Empty);
        }

        return string.Join(
            "\n",
            lines);
    }

    /// <summary>
    /// Resolves and validates the source file path for one Decision Record.
    /// </summary>
    /// <param name="decision">The Decision Record.</param>
    /// <returns>The absolute Markdown file path.</returns>
    private string ResolveDecisionPath(
            DecisionRecord decision)
    {
        string path =
            ResolveWorkspacePath(
                decision.SourceRelativePath);

        if (!IsWithinWorkspace(
                path) ||
            !File.Exists(
                path))
        {
            throw new FileNotFoundException(
                "Le Decision Record est introuvable.",
                path);
        }

        return path;
    }

    /// <summary>
    /// Performs the <c>InsertMetadata</c> operation.
    /// </summary>
    /// <param name="content">The <c>content</c> value.</param>
    /// <param name="scopeKind">The <c>scopeKind</c> value.</param>
    /// <param name="scopeName">The <c>scopeName</c> value.</param>
    /// <param name="status">The <c>status</c> value.</param>
    /// <param name="sourceQualifiedName">The <c>sourceQualifiedName</c> value.</param>
    /// <returns>The result of the operation.</returns>
    private static string InsertMetadata(
            string content,
            string scopeKind,
            string scopeName,
            string status,
            string? sourceQualifiedName)
    {
        global::System.Collections.Generic.List<string> lines = NormalizeNewlines(content)
            .Split('\n')
            .ToList();

        int dateIndex = lines.FindIndex(line =>
            line.TrimStart().StartsWith(
                "**Date :**",
                StringComparison.CurrentCultureIgnoreCase));

        int insertIndex = dateIndex >= 0
            ? dateIndex + 1
            : Math.Min(
                1,
                lines.Count);

        global::System.Collections.Generic.List<string> metadata = new List<string>
        {
            $"**{scopeKind} :** {scopeName}"
        };

        if (!string.IsNullOrWhiteSpace(status))
        {
            metadata.Add(
                $"**Statut :** {status.Trim()}");
        }

        if (!string.IsNullOrWhiteSpace(sourceQualifiedName))
        {
            metadata.Add(
                $"**Source :** [[{sourceQualifiedName}]]");
        }

        lines.InsertRange(
            insertIndex,
            metadata);

        return string.Join(
            "\n",
            lines);
    }

    /// <summary>
    /// Performs the <c>BuildLinksBody</c> operation.
    /// </summary>
    /// <param name="links">The <c>links</c> value.</param>
    /// <param name="source">The <c>source</c> value.</param>
    /// <returns>The result of the operation.</returns>
    private static string BuildLinksBody(
            string links,
            LinkTargetEntry? source)
    {
        global::System.Collections.Generic.List<string> entries = new List<string>();

        if (source is not null)
        {
            entries.Add(
                $"- Source : [[{source.QualifiedName}]]");
        }

        entries.AddRange(
            NormalizeNewlines(links)
                .Split(
                    '\n',
                    StringSplitOptions.TrimEntries |
                    StringSplitOptions.RemoveEmptyEntries)
                .Select(line =>
                    line.StartsWith(
                        "- ",
                        StringComparison.Ordinal)
                        ? line
                        : $"- {line}"));

        return string.Join(
            "\n",
            entries);
    }

    /// <summary>
    /// Performs the <c>ReplaceSection</c> operation.
    /// </summary>
    /// <param name="content">The <c>content</c> value.</param>
    /// <param name="heading">The <c>heading</c> value.</param>
    /// <param name="body">The <c>body</c> value.</param>
    /// <returns>The result of the operation.</returns>
    private static string ReplaceSection(
            string content,
            string heading,
            string body)
    {
        string normalized = NormalizeNewlines(content);
        global::System.Collections.Generic.List<string> lines = normalized.Split('\n').ToList();
        string expected = $"## {heading}";

        int headingIndex = lines.FindIndex(line =>
            string.Equals(
                line.Trim(),
                expected,
                StringComparison.CurrentCultureIgnoreCase));

        if (headingIndex < 0)
        {
            return normalized.TrimEnd() +
                   "\n\n" +
                   expected +
                   "\n\n" +
                   body.Trim() +
                   "\n";
        }

        int nextHeading = lines.FindIndex(
            headingIndex + 1,
            line => line.TrimStart().StartsWith(
                "## ",
                StringComparison.Ordinal));

        if (nextHeading < 0)
        {
            nextHeading = lines.Count;
        }

        lines.RemoveRange(
            headingIndex + 1,
            nextHeading - headingIndex - 1);

        global::System.Collections.Generic.List<string> replacement = new List<string>
        {
            string.Empty
        };

        if (!string.IsNullOrWhiteSpace(body))
        {
            replacement.AddRange(
                NormalizeNewlines(body)
                    .Trim()
                    .Split('\n'));
            replacement.Add(
                string.Empty);
        }

        lines.InsertRange(
            headingIndex + 1,
            replacement);

        return string.Join(
            "\n",
            lines);
    }

    /// <summary>
    /// Performs the <c>ReadSection</c> operation.
    /// </summary>
    /// <param name="content">The <c>content</c> value.</param>
    /// <param name="heading">The <c>heading</c> value.</param>
    /// <returns>The result of the operation.</returns>
    private static string ReadSection(
            string content,
            string heading)
    {
        string[] lines = NormalizeNewlines(content)
            .Split('\n');

        string expected = $"## {heading}";
        int start = Array.FindIndex(
            lines,
            line => string.Equals(
                line.Trim(),
                expected,
                StringComparison.CurrentCultureIgnoreCase));

        if (start < 0)
        {
            return string.Empty;
        }

        int end = Array.FindIndex(
            lines,
            start + 1,
            line => line.TrimStart().StartsWith(
                "## ",
                StringComparison.Ordinal));

        if (end < 0)
        {
            end = lines.Length;
        }

        return string.Join(
                "\n",
                lines[(start + 1)..end])
            .Trim();
    }

    /// <summary>
    /// Performs the <c>ReadMetadata</c> operation.
    /// </summary>
    /// <param name="lines">The <c>lines</c> value.</param>
    /// <param name="key">The <c>key</c> value.</param>
    /// <returns>The result of the operation.</returns>
    private static string ReadMetadata(
            IReadOnlyList<string> lines,
            string key)
    {
        string prefix = $"**{key} :**";

        string? line = lines.FirstOrDefault(candidate =>
            candidate.TrimStart().StartsWith(
                prefix,
                StringComparison.CurrentCultureIgnoreCase));

        return line is null
            ? string.Empty
            : line.Trim()[
                prefix.Length..]
                .Trim();
    }

    /// <summary>
    /// Performs the <c>StripBullet</c> operation.
    /// </summary>
    /// <param name="line">The <c>line</c> value.</param>
    /// <returns>The result of the operation.</returns>
    private static string StripBullet(string line)
    {
        string value = line.Trim();

        if (value.StartsWith(
                "- ",
                StringComparison.Ordinal) ||
            value.StartsWith(
                "* ",
                StringComparison.Ordinal))
        {
            return value[2..].Trim();
        }

        return value;
    }

    /// <summary>
    /// Performs the <c>Contains</c> operation.
    /// </summary>
    /// <param name="value">The <c>value</c> value.</param>
    /// <param name="needle">The <c>needle</c> value.</param>
    /// <returns>The result of the operation.</returns>
    private static bool Contains(
            string value,
            string needle) =>
            !string.IsNullOrWhiteSpace(value) &&
            value.Contains(
                needle,
                StringComparison.CurrentCultureIgnoreCase);

    /// <summary>
    /// Performs the <c>EnsureTrailingNewline</c> operation.
    /// </summary>
    /// <param name="value">The <c>value</c> value.</param>
    /// <returns>The result of the operation.</returns>
    private static string EnsureTrailingNewline(string value) =>
            NormalizeNewlines(value).TrimEnd() + "\n";

    /// <summary>
    /// Performs the <c>NormalizeNewlines</c> operation.
    /// </summary>
    /// <param name="value">The <c>value</c> value.</param>
    /// <returns>The result of the operation.</returns>
    private static string NormalizeNewlines(string value) =>
            (value ?? string.Empty)
                .Replace(
                    "\r\n",
                    "\n",
                    StringComparison.Ordinal)
                .Replace(
                    '\r',
                    '\n');

    /// <summary>
    /// Performs the <c>ResolveWorkspacePath</c> operation.
    /// </summary>
    /// <param name="relativePath">The <c>relativePath</c> value.</param>
    /// <returns>The result of the operation.</returns>
    private string ResolveWorkspacePath(string relativePath) =>
            Path.GetFullPath(
                Path.Combine(
                    _workspaceRoot,
                    relativePath.Replace(
                        '/',
                        Path.DirectorySeparatorChar)));

    /// <summary>
    /// Performs the <c>IsWithinWorkspace</c> operation.
    /// </summary>
    /// <param name="path">The <c>path</c> value.</param>
    /// <returns>The result of the operation.</returns>
    private bool IsWithinWorkspace(string path)
    {
        string root = _workspaceRoot.TrimEnd(
            Path.DirectorySeparatorChar,
            Path.AltDirectorySeparatorChar);

        return string.Equals(
                   path,
                   root,
                   StringComparison.OrdinalIgnoreCase) ||
               path.StartsWith(
                   root + Path.DirectorySeparatorChar,
                   StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Performs the <c>IsRelativeAncestorOrEqual</c> operation.
    /// </summary>
    /// <param name="candidateParent">The <c>candidateParent</c> value.</param>
    /// <param name="child">The <c>child</c> value.</param>
    /// <returns>The result of the operation.</returns>
    private static bool IsRelativeAncestorOrEqual(
            string candidateParent,
            string child)
    {
        string parent = candidateParent.Trim('/');
        string descendant = child.Trim('/');

        return string.Equals(
                   parent,
                   descendant,
                   StringComparison.OrdinalIgnoreCase) ||
               descendant.StartsWith(
                   parent + "/",
                   StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Performs the <c>NormalizeRelativePath</c> operation.
    /// </summary>
    /// <param name="path">The <c>path</c> value.</param>
    /// <returns>The result of the operation.</returns>
    private static string NormalizeRelativePath(string path) =>
            path.Replace(
                Path.DirectorySeparatorChar,
                '/');
}
