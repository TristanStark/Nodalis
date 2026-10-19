namespace Nodalis.Core.Importing;

/// <summary>
/// Validates and projects a structured import tree without reading or writing a workspace.
/// </summary>
public static class ImportDocumentPlanBuilder
{
    private const int MaximumNodes = 100_000;
    private const int MaximumTextLength = 4_000_000;
    private const int MaximumRelativePathLength = 512;

    private static readonly HashSet<string> ReservedWindowsSegments =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "CON", "PRN", "AUX", "NUL",
            "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
            "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"
        };

    /// <summary>
    /// Applies explicit mapping rules with deterministic inherited destinations.
    /// Exclusion of a parent excludes its entire subtree, even if a child has an include rule.
    /// </summary>
    /// <param name="document">A tree produced by any compatible reader.</param>
    /// <param name="selections">User-confirmed mapping overrides.</param>
    /// <returns>A validated dry-run plan that performs no disk operations.</returns>
    public static ImportDocumentPlan Prepare(
        ImportDocumentTree document,
        IReadOnlyList<ImportSelectionRule> selections)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(selections);

        if (string.IsNullOrWhiteSpace(document.FormatId) ||
            string.IsNullOrWhiteSpace(document.SourceFileName) ||
            document.SourceFileName != Path.GetFileName(document.SourceFileName) ||
            document.SourceSha256.Length != 64 ||
            !document.SourceSha256.All(Uri.IsHexDigit))
        {
            throw new InvalidDataException("Identité ou empreinte de la source d'import invalide.");
        }

        Dictionary<string, ImportSelectionRule> overrides =
            new Dictionary<string, ImportSelectionRule>(StringComparer.Ordinal);

        foreach (ImportSelectionRule selection in selections)
        {
            if (string.IsNullOrWhiteSpace(selection.SourceId) ||
                !overrides.TryAdd(selection.SourceId, selection))
            {
                throw new InvalidDataException("Sélection d'import vide ou en doublon.");
            }

            if (selection.DestinationRelativePath is not null)
            {
                ValidateDestination(selection.DestinationRelativePath);
            }

            if (selection.TargetSection is not null &&
                (selection.TargetSection.Length > 256 ||
                 selection.TargetSection.Contains('\0')))
            {
                throw new InvalidDataException("Section cible d'import invalide.");
            }
        }

        List<ImportPlannedFragment> fragments = [];
        HashSet<string> seenIds = new HashSet<string>(StringComparer.Ordinal);
        int nodeCount = 0;
        foreach (ImportDocumentNode root in document.Roots)
        {
            Walk(root, null, false, null, null, false);
        }

        foreach (string selectedId in overrides.Keys)
        {
            if (!seenIds.Contains(selectedId))
            {
                throw new InvalidDataException("Une sélection ne correspond à aucun bloc source : " + selectedId);
            }
        }

        return new ImportDocumentPlan
        {
            FormatId = document.FormatId,
            SourceFileName = document.SourceFileName,
            SourceSha256 = document.SourceSha256,
            Selections = selections.ToList(),
            Fragments = fragments,
            Warnings = document.Warnings.ToList()
        };

        void Walk(
            ImportDocumentNode node,
            string? inheritedPath,
            bool inheritedInclude,
            string? inheritedSection,
            int? previousAncestorOrder,
            bool parentExcluded)
        {
            nodeCount++;
            if (nodeCount > MaximumNodes ||
                node.SourceOrder < 0 ||
                node.Text.Length > MaximumTextLength ||
                string.IsNullOrWhiteSpace(node.SourceId) ||
                !seenIds.Add(node.SourceId))
            {
                throw new InvalidDataException("Arbre importé trop volumineux ou identifiants de source invalides.");
            }

            if (previousAncestorOrder is int parentOrder &&
                node.SourceOrder < parentOrder)
            {
                throw new InvalidDataException("L'ordre des sous-sections importées est incohérent.");
            }

            bool included = inheritedInclude;
            string? destination = inheritedPath;
            string? section = inheritedSection;

            bool explicitlyExcluded = false;
            if (overrides.TryGetValue(node.SourceId, out ImportSelectionRule? rule))
            {
                explicitlyExcluded = !rule.Include;
                included = rule.Include;
                destination = rule.DestinationRelativePath ?? inheritedPath;
                section = rule.TargetSection ?? inheritedSection;
            }

            included = included && !parentExcluded;

            if (included && string.IsNullOrWhiteSpace(destination))
            {
                throw new InvalidDataException("Un bloc retenu n'a aucune destination Markdown.");
            }

            if (included && destination is not null)
            {
                fragments.Add(new ImportPlannedFragment
                {
                    SourceId = node.SourceId,
                    Kind = node.Kind,
                    SourceOrder = node.SourceOrder,
                    DestinationRelativePath = destination,
                    TargetSection = section,
                    Text = node.Text
                });
            }

            foreach (ImportDocumentNode child in node.Children)
            {
                Walk(child, destination, included, section, node.SourceOrder,
                    parentExcluded || explicitlyExcluded);
            }
        }
    }

    /// <summary>
    /// Rejects absolute, traversal, device, stream and non-Markdown target paths on all OSes.
    /// </summary>
    /// <param name="relativePath">Desired workspace-relative Markdown path.</param>
    public static void ValidateDestination(string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath) ||
            relativePath.Length > MaximumRelativePathLength ||
            relativePath.StartsWith('/') ||
            relativePath.StartsWith('\\') ||
            relativePath.Contains(':') ||
            relativePath.Any(char.IsControl))
        {
            throw new InvalidDataException("Chemin de destination non relatif ou invalide.");
        }

        string normalized = relativePath.Replace('\\', '/');
        string[] segments = normalized.Split('/');
        if (segments.Length == 0 ||
            !normalized.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("Une destination doit être un fichier Markdown.");
        }

        foreach (string segment in segments)
        {
            string basename = segment.Split('.')[0];
            if (segment.Length == 0 ||
                segment is "." or ".." ||
                segment.EndsWith('.') ||
                segment.EndsWith(' ') ||
                segment.IndexOfAny(['<', '>', '"', '|', '?', '*']) >= 0 ||
                ReservedWindowsSegments.Contains(basename))
            {
                throw new InvalidDataException("Segment interdit dans une destination Markdown : " + segment);
            }
        }
    }
}
