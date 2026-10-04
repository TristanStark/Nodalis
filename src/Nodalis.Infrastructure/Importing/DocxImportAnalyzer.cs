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

    public DocxImportAnalyzer(string workspaceRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRoot);

        var root = Path.GetFullPath(workspaceRoot);
        _linkIndex = new WorkspaceLinkIndexService(root);
        _ruleStore = new DocxImportRuleStore(root);
    }

    public async Task<DocxImportAnalysis> AnalyzeAsync(
        ParsedDocxDocument document,
        string sourceFileName,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceFileName);

        var rules = await _ruleStore.LoadAsync(cancellationToken);
        var links = await _linkIndex.RefreshAsync(cancellationToken);

        var evidence = BuildEvidence(
            document,
            sourceFileName,
            rules.Detection.MaxContentParagraphs);

        var explicitApplications = ExtractLabeledValues(
            evidence,
            rules.Detection.ApplicationLabels);

        var explicitProjects = ExtractLabeledValues(
            evidence,
            rules.Detection.ProjectLabels);

        var applicationTargets = links.Targets
            .Where(target => target.Kind == LinkTargetKind.Application)
            .ToArray();

        var applicationCandidates = RankTargets(
            applicationTargets,
            evidence,
            explicitApplications);

        Guid? resolvedApplicationId =
            applicationCandidates.Count == 1
                ? applicationCandidates[0].Id
                : null;

        var projectTargets = links.Targets
            .Where(target => target.Kind == LinkTargetKind.Project)
            .ToArray();

        if (resolvedApplicationId is Guid applicationId)
        {
            var application = applicationTargets.Single(target =>
                target.Id == applicationId);

            projectTargets = projectTargets
                .Where(project =>
                    IsRelativeAncestor(
                        application.RelativePath,
                        project.RelativePath))
                .ToArray();
        }

        var projectCandidates = RankTargets(
            projectTargets,
            evidence,
            explicitProjects);

        var mapped = MapSections(
            document.Blocks,
            rules.SectionMappings,
            out var unmapped);

        var proposedApplication = FindStrongestUnmatchedLabel(
            explicitApplications,
            applicationTargets);

        var proposedProject = FindStrongestUnmatchedLabel(
            explicitProjects,
            projectTargets);

        var notes = BuildDetectionNotes(
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

    private static IReadOnlyList<EvidenceItem> BuildEvidence(
        ParsedDocxDocument document,
        string sourceFileName,
        int maximumParagraphs)
    {
        var result = new List<EvidenceItem>();

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

        foreach (var header in document.Headers)
        {
            AddEvidence(
                result,
                EvidenceKind.Header,
                header);
        }

        foreach (var paragraph in document.Blocks
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

    private static void AddEvidence(
        ICollection<EvidenceItem> target,
        EvidenceKind kind,
        string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        foreach (var line in NormalizeNewlines(value)
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

    private static IReadOnlyList<LabeledValue> ExtractLabeledValues(
        IReadOnlyList<EvidenceItem> evidence,
        IReadOnlyList<string> labels)
    {
        var result = new List<LabeledValue>();

        foreach (var item in evidence)
        {
            var source = item.Text.Trim()
                .TrimStart('-', '*', '•')
                .Trim();

            foreach (var label in labels
                         .Where(label =>
                             !string.IsNullOrWhiteSpace(label)))
            {
                var pattern =
                    "^\\s*" +
                    Regex.Escape(label.Trim()) +
                    "\\s*[:=]\\s*(?<value>.+?)\\s*$";

                var match = Regex.Match(
                    source,
                    pattern,
                    RegexOptions.IgnoreCase |
                    RegexOptions.CultureInvariant,
                    TimeSpan.FromMilliseconds(100));

                if (!match.Success)
                {
                    continue;
                }

                var value = match.Groups["value"].Value.Trim();

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

    private static List<DocxDetectedTarget> RankTargets(
        IReadOnlyList<LinkTargetEntry> targets,
        IReadOnlyList<EvidenceItem> evidence,
        IReadOnlyList<LabeledValue> labeledValues)
    {
        var ranked = new List<DocxDetectedTarget>();

        foreach (var target in targets)
        {
            var normalizedName = NormalizeForComparison(
                target.DisplayName);

            if (string.IsNullOrWhiteSpace(normalizedName))
            {
                continue;
            }

            var confidence = 0;
            var reasons = new List<string>();

            var explicitMatches = labeledValues
                .Where(value =>
                    string.Equals(
                        NormalizeForComparison(value.Value),
                        normalizedName,
                        StringComparison.Ordinal))
                .ToArray();

            if (explicitMatches.Length > 0)
            {
                var strongest = explicitMatches
                    .OrderByDescending(value =>
                        ExplicitScore(value.Kind))
                    .First();

                confidence = Math.Max(
                    confidence,
                    ExplicitScore(strongest.Kind));

                reasons.Add(
                    $"Identifiant explicite « {strongest.SourceText} »");
            }

            foreach (var kind in Enum.GetValues<EvidenceKind>())
            {
                var occurrence = evidence.FirstOrDefault(item =>
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

        var ordered = ranked
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

        var strongestScore = ordered[0].Confidence;

        return ordered
            .Where(candidate =>
                candidate.Confidence >= 20 &&
                candidate.Confidence >= strongestScore - 12)
            .Take(5)
            .ToList();
    }

    private static List<DocxMappedSection> MapSections(
        IReadOnlyList<DocxBlock> blocks,
        IReadOnlyList<DocxSectionMappingRule> rules,
        out List<DocxBlock> unmappedBlocks)
    {
        var result = new List<DocxMappedSection>();
        unmappedBlocks = [];

        DocxMappedSection? current = null;

        for (var index = 0;
             index < blocks.Count;
             index++)
        {
            var block = blocks[index];

            if (block.Kind == DocxBlockKind.Paragraph &&
                block.Paragraph is { HeadingLevel: int headingLevel } paragraph)
            {
                var rule = FindMappingRule(
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
                    continue;
                }

                current = null;
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

    private static DocxSectionMappingRule? FindMappingRule(
        string heading,
        int headingLevel,
        IReadOnlyList<DocxSectionMappingRule> rules)
    {
        var normalizedHeading = NormalizeForComparison(
            heading);

        foreach (var rule in rules)
        {
            if (headingLevel > rule.MaximumHeadingLevel)
            {
                continue;
            }

            foreach (var alias in rule.HeadingAliases)
            {
                var normalizedAlias = NormalizeForComparison(
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

    private static string? FindStrongestUnmatchedLabel(
        IReadOnlyList<LabeledValue> labeledValues,
        IReadOnlyList<LinkTargetEntry> targets)
    {
        foreach (var value in labeledValues
                     .OrderByDescending(item =>
                         ExplicitScore(item.Kind)))
        {
            var normalized = NormalizeForComparison(
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

    private static List<string> BuildDetectionNotes(
        IReadOnlyList<DocxDetectedTarget> applications,
        IReadOnlyList<DocxDetectedTarget> projects,
        string? proposedApplication,
        string? proposedProject,
        IReadOnlyList<DocxMappedSection> sections)
    {
        var notes = new List<string>();

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

    private static int ExplicitScore(EvidenceKind kind) =>
        kind switch
        {
            EvidenceKind.Header => 98,
            EvidenceKind.Metadata => 95,
            EvidenceKind.Content => 90,
            EvidenceKind.FileName => 85,
            _ => 80
        };

    private static int OccurrenceScore(EvidenceKind kind) =>
        kind switch
        {
            EvidenceKind.Header => 72,
            EvidenceKind.Metadata => 62,
            EvidenceKind.Content => 48,
            EvidenceKind.FileName => 40,
            _ => 20
        };

    private static string EvidenceLabel(EvidenceKind kind) =>
        kind switch
        {
            EvidenceKind.FileName => "Nom du fichier",
            EvidenceKind.Metadata => "Métadonnée",
            EvidenceKind.Header => "En-tête",
            EvidenceKind.Content => "Contenu",
            _ => "Source"
        };

    private static bool ContainsNormalizedPhrase(
        string source,
        string normalizedPhrase)
    {
        var normalizedSource = NormalizeForComparison(
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

    private static string NormalizeForComparison(string value)
    {
        var decomposed = (value ?? string.Empty)
            .Normalize(
                NormalizationForm.FormD);

        var builder = new StringBuilder();

        foreach (var character in decomposed)
        {
            var category = CharUnicodeInfo.GetUnicodeCategory(
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

    private static string TrimEvidence(string value)
    {
        var trimmed = value.Trim();

        return trimmed.Length <= 100
            ? trimmed
            : trimmed[..97] + "...";
    }

    private static string NormalizeNewlines(string value) =>
        (value ?? string.Empty)
            .Replace(
                "\r\n",
                "\n",
                StringComparison.Ordinal)
            .Replace(
                '\r',
                '\n');

    private static bool IsRelativeAncestor(
        string candidateParent,
        string child)
    {
        var parent = candidateParent.Trim('/');
        var descendant = child.Trim('/');

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
