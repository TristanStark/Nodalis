using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Nodalis.Core.Importing;
using Nodalis.Core.Links;
using Nodalis.Infrastructure.Links;

namespace Nodalis.Infrastructure.Importing;

public sealed class DocxImportAnalyzer
{
    private readonly WorkspaceLinkIndexService _linkIndex;
    private readonly DocxImportRuleStore _ruleStore;

    /// <summary>
    /// Initializes a new instance of <see cref="DocxImportAnalyzer"/>.
    /// </summary>
    /// <param name="workspaceRoot">The <c>workspaceRoot</c> value.</param>
    public DocxImportAnalyzer(string workspaceRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRoot);

        string root = Path.GetFullPath(workspaceRoot);
        _linkIndex = new WorkspaceLinkIndexService(root);
        _ruleStore = new DocxImportRuleStore(root);
    }

    /// <summary>
    /// Performs the <c>AnalyzeAsync</c> operation.
    /// </summary>
    /// <param name="document">The <c>document</c> value.</param>
    /// <param name="sourceFileName">The <c>sourceFileName</c> value.</param>
    /// <param name="cancellationToken">The <c>cancellationToken</c> value.</param>
    /// <returns>The result of the operation.</returns>
    public async Task<DocxImportAnalysis> AnalyzeAsync(
            ParsedDocxDocument document,
            string sourceFileName,
            CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceFileName);

        global::Nodalis.Core.Importing.DocxImportRuleCatalog rules = await _ruleStore.LoadAsync(cancellationToken);
        global::Nodalis.Core.Links.LinkIndexCatalog links = await _linkIndex.RefreshAsync(cancellationToken);

        global::System.Collections.Generic.IReadOnlyList<global::Nodalis.Infrastructure.Importing.DocxImportAnalyzer.EvidenceItem> evidence = BuildEvidence(
            document,
            sourceFileName,
            rules.Detection.MaxContentParagraphs);

        global::System.Collections.Generic.IReadOnlyList<global::Nodalis.Infrastructure.Importing.DocxImportAnalyzer.LabeledValue> explicitApplications = ExtractLabeledValues(
            evidence,
            rules.Detection.ApplicationLabels);

        global::System.Collections.Generic.IReadOnlyList<global::Nodalis.Infrastructure.Importing.DocxImportAnalyzer.LabeledValue> explicitProjects = ExtractLabeledValues(
            evidence,
            rules.Detection.ProjectLabels);

        global::Nodalis.Core.Links.LinkTargetEntry[] applicationTargets = links.Targets
            .Where(target => target.Kind == LinkTargetKind.Application)
            .ToArray();

        global::System.Collections.Generic.List<global::Nodalis.Core.Importing.DocxDetectedTarget> applicationCandidates = RankTargets(
            applicationTargets,
            evidence,
            explicitApplications);

        Guid? resolvedApplicationId =
            applicationCandidates.Count == 1
                ? applicationCandidates[0].Id
                : null;

        global::Nodalis.Core.Links.LinkTargetEntry[] projectTargets = links.Targets
            .Where(target => target.Kind == LinkTargetKind.Project)
            .ToArray();

        if (resolvedApplicationId is Guid applicationId)
        {
            global::Nodalis.Core.Links.LinkTargetEntry application = applicationTargets.Single(target =>
                target.Id == applicationId);

            projectTargets = projectTargets
                .Where(project =>
                    IsRelativeAncestor(
                        application.RelativePath,
                        project.RelativePath))
                .ToArray();
        }

        global::System.Collections.Generic.List<global::Nodalis.Core.Importing.DocxDetectedTarget> projectCandidates = RankTargets(
            projectTargets,
            evidence,
            explicitProjects);

        global::System.Collections.Generic.List<global::Nodalis.Core.Importing.DocxMappedSection> mapped = MapSections(
            document.Blocks,
            rules.SectionMappings,
            out global::System.Collections.Generic.List<global::Nodalis.Core.Importing.DocxBlock>? unmapped);

        string? proposedApplication = FindStrongestUnmatchedLabel(
            explicitApplications,
            applicationTargets);

        string? proposedProject = FindStrongestUnmatchedLabel(
            explicitProjects,
            projectTargets);

        global::System.Collections.Generic.List<string> notes = BuildDetectionNotes(
            applicationCandidates,
            projectCandidates,
            proposedApplication,
            proposedProject,
            mapped);

        return new DocxImportAnalysis
        {
            ApplicationCandidates = applicationCandidates,
            ProjectCandidates = projectCandidates,
            ProposedApplicationName = proposedApplication,
            ProposedProjectName = proposedProject,
            DetectionNotes = notes,
            MappedSections = mapped,
            UnmappedBlocks = unmapped
        };
    }

    /// <summary>
    /// Performs the <c>BuildEvidence</c> operation.
    /// </summary>
    /// <param name="document">The <c>document</c> value.</param>
    /// <param name="sourceFileName">The <c>sourceFileName</c> value.</param>
    /// <param name="maximumParagraphs">The <c>maximumParagraphs</c> value.</param>
    /// <returns>The result of the operation.</returns>
    private static IReadOnlyList<EvidenceItem> BuildEvidence(
            ParsedDocxDocument document,
            string sourceFileName,
            int maximumParagraphs)
    {
        global::System.Collections.Generic.List<global::Nodalis.Infrastructure.Importing.DocxImportAnalyzer.EvidenceItem> result = new List<EvidenceItem>();

        AddEvidence(
            result,
            EvidenceKind.FileName,
            Path.GetFileNameWithoutExtension(sourceFileName));

        AddEvidence(
            result,
            EvidenceKind.Metadata,
            document.Metadata.Title);
        AddEvidence(
            result,
            EvidenceKind.Metadata,
            document.Metadata.Subject);
        AddEvidence(
            result,
            EvidenceKind.Metadata,
            document.Metadata.Keywords);
        AddEvidence(
            result,
            EvidenceKind.Metadata,
            document.Metadata.Category);
        AddEvidence(
            result,
            EvidenceKind.Metadata,
            document.Metadata.Description);

        foreach (string header in document.Headers)
        {
            AddEvidence(
                result,
                EvidenceKind.Header,
                header);
        }

        foreach (global::Nodalis.Core.Importing.DocxParagraph paragraph in document.Blocks
                     .Where(block =>
                         block.Kind == DocxBlockKind.Paragraph &&
                         block.Paragraph is not null)
                     .Select(block => block.Paragraph!)
                     .Where(paragraph =>
                         !string.IsNullOrWhiteSpace(paragraph.Text))
                     .Take(maximumParagraphs))
        {
            AddEvidence(
                result,
                EvidenceKind.Content,
                paragraph.Text);
        }

        return result;
    }

    /// <summary>
    /// Performs the <c>AddEvidence</c> operation.
    /// </summary>
    /// <param name="target">The <c>target</c> value.</param>
    /// <param name="kind">The <c>kind</c> value.</param>
    /// <param name="value">The <c>value</c> value.</param>
    private static void AddEvidence(
            ICollection<EvidenceItem> target,
            EvidenceKind kind,
            string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        foreach (string line in NormalizeNewlines(value)
                     .Split(
                         '\n',
                         StringSplitOptions.TrimEntries |
                         StringSplitOptions.RemoveEmptyEntries))
        {
            target.Add(
                new EvidenceItem(
                    kind,
                    line));
        }
    }

    /// <summary>
    /// Performs the <c>ExtractLabeledValues</c> operation.
    /// </summary>
    /// <param name="evidence">The <c>evidence</c> value.</param>
    /// <param name="labels">The <c>labels</c> value.</param>
    /// <returns>The result of the operation.</returns>
    private static IReadOnlyList<LabeledValue> ExtractLabeledValues(
            IReadOnlyList<EvidenceItem> evidence,
            IReadOnlyList<string> labels)
    {
        global::System.Collections.Generic.List<global::Nodalis.Infrastructure.Importing.DocxImportAnalyzer.LabeledValue> result = new List<LabeledValue>();

        foreach (global::Nodalis.Infrastructure.Importing.DocxImportAnalyzer.EvidenceItem item in evidence)
        {
            string source = item.Text.Trim()
                .TrimStart('-', '*', '•')
                .Trim();

            foreach (string label in labels
                         .Where(label =>
                             !string.IsNullOrWhiteSpace(label)))
            {
                string pattern =
                    "^\\s*" +
                    Regex.Escape(label.Trim()) +
                    "\\s*[:=]\\s*(?<value>.+?)\\s*$";

                global::System.Text.RegularExpressions.Match match = Regex.Match(
                    source,
                    pattern,
                    RegexOptions.IgnoreCase |
                    RegexOptions.CultureInvariant,
                    TimeSpan.FromMilliseconds(100));

                if (!match.Success)
                {
                    continue;
                }

                string value = match.Groups["value"].Value.Trim();

                if (!string.IsNullOrWhiteSpace(value))
                {
                    result.Add(
                        new LabeledValue(
                            item.Kind,
                            value,
                            source));
                }

                break;
            }
        }

        return result
            .GroupBy(
                item =>
                    (item.Kind, NormalizeForComparison(item.Value)))
            .Select(group => group.First())
            .ToArray();
    }

    /// <summary>
    /// Performs the <c>RankTargets</c> operation.
    /// </summary>
    /// <param name="targets">The <c>targets</c> value.</param>
    /// <param name="evidence">The <c>evidence</c> value.</param>
    /// <param name="labeledValues">The <c>labeledValues</c> value.</param>
    /// <returns>The result of the operation.</returns>
    private static List<DocxDetectedTarget> RankTargets(
            IReadOnlyList<LinkTargetEntry> targets,
            IReadOnlyList<EvidenceItem> evidence,
            IReadOnlyList<LabeledValue> labeledValues)
    {
        global::System.Collections.Generic.List<global::Nodalis.Core.Importing.DocxDetectedTarget> ranked = new List<DocxDetectedTarget>();

        foreach (global::Nodalis.Core.Links.LinkTargetEntry target in targets)
        {
            string normalizedName = NormalizeForComparison(
                target.DisplayName);

            if (string.IsNullOrWhiteSpace(normalizedName))
            {
                continue;
            }

            int confidence = 0;
            global::System.Collections.Generic.List<string> reasons = new List<string>();

            global::Nodalis.Infrastructure.Importing.DocxImportAnalyzer.LabeledValue[] explicitMatches = labeledValues
                .Where(value =>
                    string.Equals(
                        NormalizeForComparison(value.Value),
                        normalizedName,
                        StringComparison.Ordinal))
                .ToArray();

            if (explicitMatches.Length > 0)
            {
                global::Nodalis.Infrastructure.Importing.DocxImportAnalyzer.LabeledValue strongest = explicitMatches
                    .OrderByDescending(value =>
                        ExplicitScore(value.Kind))
                    .First();

                confidence = Math.Max(
                    confidence,
                    ExplicitScore(strongest.Kind));

                reasons.Add(
                    $"Identifiant explicite « {strongest.SourceText} »");
            }

            foreach (global::Nodalis.Infrastructure.Importing.DocxImportAnalyzer.EvidenceKind kind in Enum.GetValues<EvidenceKind>())
            {
                global::Nodalis.Infrastructure.Importing.DocxImportAnalyzer.EvidenceItem? occurrence = evidence.FirstOrDefault(item =>
                    item.Kind == kind &&
                    ContainsNormalizedPhrase(
                        item.Text,
                        normalizedName));

                if (occurrence is null)
                {
                    continue;
                }

                confidence = Math.Max(
                    confidence,
                    OccurrenceScore(kind));

                reasons.Add(
                    $"{EvidenceLabel(kind)} : « {TrimEvidence(occurrence.Text)} »");
            }

            if (confidence == 0)
            {
                continue;
            }

            ranked.Add(
                new DocxDetectedTarget
                {
                    Id = target.Id,
                    DisplayName = target.DisplayName,
                    QualifiedName = target.QualifiedName,
                    RelativePath = target.RelativePath,
                    Confidence = confidence,
                    Evidence = reasons
                        .Distinct(
                            StringComparer.CurrentCultureIgnoreCase)
                        .ToList()
                });
        }

        global::Nodalis.Core.Importing.DocxDetectedTarget[] ordered = ranked
            .OrderByDescending(candidate =>
                candidate.Confidence)
            .ThenBy(candidate =>
                candidate.DisplayName,
                StringComparer.CurrentCultureIgnoreCase)
            .ToArray();

        if (ordered.Length == 0)
        {
            return [];
        }

        int strongestScore = ordered[0].Confidence;

        return ordered
            .Where(candidate =>
                candidate.Confidence >= 20 &&
                candidate.Confidence >= strongestScore - 12)
            .Take(5)
            .ToList();
    }

    /// <summary>
    /// Performs the <c>MapSections</c> operation.
    /// </summary>
    /// <param name="blocks">The <c>blocks</c> value.</param>
    /// <param name="rules">The <c>rules</c> value.</param>
    /// <param name="unmappedBlocks">The <c>unmappedBlocks</c> value.</param>
    /// <returns>The result of the operation.</returns>
    private static List<DocxMappedSection> MapSections(
            IReadOnlyList<DocxBlock> blocks,
            IReadOnlyList<DocxSectionMappingRule> rules,
            out List<DocxBlock> unmappedBlocks)
    {
        global::System.Collections.Generic.List<global::Nodalis.Core.Importing.DocxMappedSection> result = new List<DocxMappedSection>();
        unmappedBlocks = [];

        DocxMappedSection? current = null;
        int? currentHeadingLevel = null;

        for (int index = 0;
             index < blocks.Count;
             index++)
        {
            global::Nodalis.Core.Importing.DocxBlock block = blocks[index];

            if (block.Kind == DocxBlockKind.Paragraph &&
                block.Paragraph is { HeadingLevel: int headingLevel } paragraph)
            {
                global::Nodalis.Core.Importing.DocxSectionMappingRule? rule = FindMappingRule(
                    paragraph.Text,
                    headingLevel,
                    rules);

                if (rule is not null)
                {
                    current = new DocxMappedSection
                    {
                        TargetSection = rule.TargetSection,
                        SourceHeading = paragraph.Text.Trim(),
                        HeadingBlockIndex = index,
                        Blocks = []
                    };

                    result.Add(current);
                    currentHeadingLevel = headingLevel;
                    continue;
                }

                if (current is not null &&
                    currentHeadingLevel is int sectionHeadingLevel &&
                    headingLevel > sectionHeadingLevel)
                {
                    current.Blocks.Add(block);
                    continue;
                }

                current = null;
                currentHeadingLevel = null;
                unmappedBlocks.Add(block);
                continue;
            }

            if (current is not null)
            {
                current.Blocks.Add(block);
            }
            else
            {
                unmappedBlocks.Add(block);
            }
        }

        return result;
    }

    /// <summary>
    /// Performs the <c>FindMappingRule</c> operation.
    /// </summary>
    /// <param name="heading">The <c>heading</c> value.</param>
    /// <param name="headingLevel">The <c>headingLevel</c> value.</param>
    /// <param name="rules">The <c>rules</c> value.</param>
    /// <returns>The result of the operation.</returns>
    private static DocxSectionMappingRule? FindMappingRule(
            string heading,
            int headingLevel,
            IReadOnlyList<DocxSectionMappingRule> rules)
    {
        string normalizedHeading = NormalizeForComparison(
            heading);

        foreach (global::Nodalis.Core.Importing.DocxSectionMappingRule rule in rules)
        {
            if (headingLevel > rule.MaximumHeadingLevel)
            {
                continue;
            }

            foreach (string alias in rule.HeadingAliases)
            {
                string normalizedAlias = NormalizeForComparison(
                    alias);

                if (string.Equals(
                        normalizedHeading,
                        normalizedAlias,
                        StringComparison.Ordinal) ||
                    rule.AllowPrefixMatch &&
                    normalizedHeading.StartsWith(
                        normalizedAlias + " ",
                        StringComparison.Ordinal))
                {
                    return rule;
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Performs the <c>FindStrongestUnmatchedLabel</c> operation.
    /// </summary>
    /// <param name="labeledValues">The <c>labeledValues</c> value.</param>
    /// <param name="targets">The <c>targets</c> value.</param>
    /// <returns>The result of the operation.</returns>
    private static string? FindStrongestUnmatchedLabel(
            IReadOnlyList<LabeledValue> labeledValues,
            IReadOnlyList<LinkTargetEntry> targets)
    {
        foreach (global::Nodalis.Infrastructure.Importing.DocxImportAnalyzer.LabeledValue value in labeledValues
                     .OrderByDescending(item =>
                         ExplicitScore(item.Kind)))
        {
            string normalized = NormalizeForComparison(
                value.Value);

            if (targets.Any(target =>
                    string.Equals(
                        NormalizeForComparison(target.DisplayName),
                        normalized,
                        StringComparison.Ordinal)))
            {
                continue;
            }

            return value.Value.Trim();
        }

        return null;
    }

    /// <summary>
    /// Performs the <c>BuildDetectionNotes</c> operation.
    /// </summary>
    /// <param name="applications">The <c>applications</c> value.</param>
    /// <param name="projects">The <c>projects</c> value.</param>
    /// <param name="proposedApplication">The <c>proposedApplication</c> value.</param>
    /// <param name="proposedProject">The <c>proposedProject</c> value.</param>
    /// <param name="sections">The <c>sections</c> value.</param>
    /// <returns>The result of the operation.</returns>
    private static List<string> BuildDetectionNotes(
            IReadOnlyList<DocxDetectedTarget> applications,
            IReadOnlyList<DocxDetectedTarget> projects,
            string? proposedApplication,
            string? proposedProject,
            IReadOnlyList<DocxMappedSection> sections)
    {
        global::System.Collections.Generic.List<string> notes = new List<string>();

        notes.Add(
            applications.Count switch
            {
                0 when !string.IsNullOrWhiteSpace(proposedApplication) =>
                    $"Application mentionnée mais introuvable : {proposedApplication}",
                0 =>
                    "Aucune application existante détectée.",
                1 =>
                    $"Application détectée : {applications[0].DisplayName} ({applications[0].Confidence} %).",
                _ =>
                    $"Détection application ambiguë : {applications.Count} candidats."
            });

        notes.Add(
            projects.Count switch
            {
                0 when !string.IsNullOrWhiteSpace(proposedProject) =>
                    $"Projet mentionné mais introuvable : {proposedProject}",
                0 =>
                    "Aucun projet existant détecté.",
                1 =>
                    $"Projet détecté : {projects[0].DisplayName} ({projects[0].Confidence} %).",
                _ =>
                    $"Détection projet ambiguë : {projects.Count} candidats."
            });

        notes.Add(
            sections.Count == 0
                ? "Aucune section Word n'a été mappée."
                : $"{sections.Count} section(s) Word mappée(s).");

        return notes;
    }

    /// <summary>
    /// Performs the <c>ExplicitScore</c> operation.
    /// </summary>
    /// <param name="kind">The <c>kind</c> value.</param>
    /// <returns>The result of the operation.</returns>
    private static int ExplicitScore(EvidenceKind kind) =>
            kind switch
            {
                EvidenceKind.Header => 98,
                EvidenceKind.Metadata => 95,
                EvidenceKind.Content => 90,
                EvidenceKind.FileName => 85,
                _ => 80
            };

    /// <summary>
    /// Performs the <c>OccurrenceScore</c> operation.
    /// </summary>
    /// <param name="kind">The <c>kind</c> value.</param>
    /// <returns>The result of the operation.</returns>
    private static int OccurrenceScore(EvidenceKind kind) =>
            kind switch
            {
                EvidenceKind.Header => 72,
                EvidenceKind.Metadata => 62,
                EvidenceKind.Content => 48,
                EvidenceKind.FileName => 40,
                _ => 20
            };

    /// <summary>
    /// Performs the <c>EvidenceLabel</c> operation.
    /// </summary>
    /// <param name="kind">The <c>kind</c> value.</param>
    /// <returns>The result of the operation.</returns>
    private static string EvidenceLabel(EvidenceKind kind) =>
            kind switch
            {
                EvidenceKind.FileName => "Nom du fichier",
                EvidenceKind.Metadata => "Métadonnée",
                EvidenceKind.Header => "En-tête",
                EvidenceKind.Content => "Contenu",
                _ => "Source"
            };

    /// <summary>
    /// Performs the <c>ContainsNormalizedPhrase</c> operation.
    /// </summary>
    /// <param name="source">The <c>source</c> value.</param>
    /// <param name="normalizedPhrase">The <c>normalizedPhrase</c> value.</param>
    /// <returns>The result of the operation.</returns>
    private static bool ContainsNormalizedPhrase(
            string source,
            string normalizedPhrase)
    {
        string normalizedSource = NormalizeForComparison(
            source);

        if (string.IsNullOrWhiteSpace(normalizedSource) ||
            string.IsNullOrWhiteSpace(normalizedPhrase))
        {
            return false;
        }

        return string.Equals(
                   normalizedSource,
                   normalizedPhrase,
                   StringComparison.Ordinal) ||
               normalizedSource.StartsWith(
                   normalizedPhrase + " ",
                   StringComparison.Ordinal) ||
               normalizedSource.EndsWith(
                   " " + normalizedPhrase,
                   StringComparison.Ordinal) ||
               normalizedSource.Contains(
                   " " + normalizedPhrase + " ",
                   StringComparison.Ordinal);
    }

    /// <summary>
    /// Performs the <c>NormalizeForComparison</c> operation.
    /// </summary>
    /// <param name="value">The <c>value</c> value.</param>
    /// <returns>The result of the operation.</returns>
    private static string NormalizeForComparison(string value)
    {
        string decomposed = (value ?? string.Empty)
            .Normalize(
                NormalizationForm.FormD);

        global::System.Text.StringBuilder builder = new StringBuilder();

        foreach (char character in decomposed)
        {
            global::System.Globalization.UnicodeCategory category = CharUnicodeInfo.GetUnicodeCategory(
                character);

            if (category ==
                UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            builder.Append(
                char.IsLetterOrDigit(character)
                    ? char.ToLowerInvariant(character)
                    : ' ');
        }

        return string.Join(
            " ",
            builder
                .ToString()
                .Split(
                    ' ',
                    StringSplitOptions.TrimEntries |
                    StringSplitOptions.RemoveEmptyEntries));
    }

    /// <summary>
    /// Performs the <c>TrimEvidence</c> operation.
    /// </summary>
    /// <param name="value">The <c>value</c> value.</param>
    /// <returns>The result of the operation.</returns>
    private static string TrimEvidence(string value)
    {
        string trimmed = value.Trim();

        return trimmed.Length <= 100
            ? trimmed
            : trimmed[..97] + "...";
    }

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
    /// Performs the <c>IsRelativeAncestor</c> operation.
    /// </summary>
    /// <param name="candidateParent">The <c>candidateParent</c> value.</param>
    /// <param name="child">The <c>child</c> value.</param>
    /// <returns>The result of the operation.</returns>
    private static bool IsRelativeAncestor(
            string candidateParent,
            string child)
    {
        string parent = candidateParent.Trim('/');
        string descendant = child.Trim('/');

        return descendant.StartsWith(
            parent + "/",
            StringComparison.OrdinalIgnoreCase);
    }

    private sealed record EvidenceItem(
        EvidenceKind Kind,
        string Text);

    private sealed record LabeledValue(
        EvidenceKind Kind,
        string Value,
        string SourceText);

    private enum EvidenceKind
    {
        FileName,
        Metadata,
        Header,
        Content
    }
}
