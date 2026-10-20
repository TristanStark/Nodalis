using Nodalis.Core.Importing;

namespace Nodalis.Infrastructure.Importing;

/// <summary>
/// Pure dry-run of explicit DOCX reimport decisions. No file-system mutation.
/// Only a uniquely located imported fragment can be replaced in a destination:
/// its surroundings are preserved byte-for-byte as .NET text.
/// </summary>
public static class DocxReimportDecisionPlanner
{
    private const int MaxManualMarkdownLength = 1_000_000;

    /// <summary>
    /// Validates every decision and returns per-fragment diagnostics plus
    /// projected writes. This is NOT authorization to commit: a writer must
    /// recheck source/destination hashes under its own transaction boundary.
    /// </summary>
    public static DocxReimportDecisionPlan Build(
        DocxReimportReview review,
        DocxImportManifest baseline,
        IReadOnlyCollection<DocxReimportResolution> decisions,
        IReadOnlyCollection<int>? excludedNewSectionIndexes = null,
        DocxImportPreview? latest = null,
        IReadOnlyCollection<DocxReimportNewSectionAssignment>? newSectionAssignments = null)
    {
        ArgumentNullException.ThrowIfNull(review);
        ArgumentNullException.ThrowIfNull(baseline);
        ArgumentNullException.ThrowIfNull(decisions);

        if (review.PreviousImportId != baseline.ImportId ||
            review.ProjectId != baseline.ProjectId)
        {
            throw new InvalidDataException(
                "Le plan de résolution ne correspond pas à cet historique DOCX.");
        }

        Dictionary<Guid, DocxReimportReviewedFragment> reviewed = [];
        foreach (DocxReimportReviewedFragment fragment in review.Fragments)
        {
            if (!reviewed.TryAdd(fragment.Association.FragmentId, fragment))
            {
                throw new InvalidDataException("Fragments du rapport DOCX en doublon.");
            }
        }

        Dictionary<Guid, DocxImportManifestFragment> originals = [];
        foreach (DocxImportManifestFragment fragment in baseline.Fragments)
        {
            if (!originals.TryAdd(fragment.FragmentId, fragment))
            {
                throw new InvalidDataException("Fragments du manifeste DOCX en doublon.");
            }
        }

        if (reviewed.Count != originals.Count ||
            reviewed.Keys.Any(id => !originals.ContainsKey(id)))
        {
            throw new InvalidDataException(
                "L'historique et le rapport ne décrivent plus les mêmes fragments.");
        }

        Dictionary<Guid, DocxReimportResolution> choices = [];
        foreach (DocxReimportResolution decision in decisions)
        {
            if (!originals.ContainsKey(decision.FragmentId) ||
                !choices.TryAdd(decision.FragmentId, decision) ||
                !Enum.IsDefined(decision.Action))
            {
                throw new InvalidDataException("Décision DOCX inconnue, dupliquée ou invalide.");
            }
        }

        // New Word sections never have a previous fragment to overwrite.
        // They may only be explicitly excluded from THIS proposed reimport.
        // An ambiguous section is not a genuine new section and cannot be
        // excluded through this shortcut; its identity must be adjudicated.
        HashSet<int> newlyAdded = review.Matching.AddedSectionIndexes.ToHashSet();
        if (newlyAdded.Count != review.Matching.AddedSectionIndexes.Count)
        {
            throw new InvalidDataException("Nouvelles sections Word en doublon.");
        }

        HashSet<int> excludedNew = [];
        foreach (int index in excludedNewSectionIndexes ?? [])
        {
            if (!newlyAdded.Contains(index) || !excludedNew.Add(index))
            {
                throw new InvalidDataException(
                    "Exclusion de nouvelle section Word invalide ou répétée.");
            }
        }

        List<DocxReimportProposedCreation> newCreations = [];
        HashSet<int> assigned = [];
        HashSet<string> creationDestinations =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (DocxReimportNewSectionAssignment assignment in
                 newSectionAssignments ?? [])
        {
            if (!newlyAdded.Contains(assignment.SectionIndex) ||
                excludedNew.Contains(assignment.SectionIndex) ||
                !assigned.Add(assignment.SectionIndex) ||
                latest is null)
            {
                throw new InvalidDataException(
                    "Affectation de nouvelle section Word inconnue, répétée ou exclue.");
            }

            string folder = assignment.TargetFolderRelativePath.Replace('\\', '/').TrimEnd('/');
            string leaf = "DOCX-reimport-" + baseline.ImportId.ToString("N") +
                "-section-" + assignment.SectionIndex.ToString("D4") + ".md";
            string target = folder + "/" + leaf;
            if (string.IsNullOrWhiteSpace(folder) ||
                folder.StartsWith("/", StringComparison.Ordinal) ||
                folder.Split('/').Any(segment =>
                    segment is "" or "." or ".." || segment.StartsWith('.')) ||
                !creationDestinations.Add(target) ||
                baseline.Fragments.Any(fragment =>
                    string.Equals(fragment.DestinationRelativePath, target,
                        StringComparison.OrdinalIgnoreCase)))
            {
                throw new InvalidDataException(
                    "Dossier de destination Word invalide ou en conflit.");
            }

            DocxImportSectionPreview section = latest.Sections.Single(item =>
                item.Index == assignment.SectionIndex);
            string markdown = DocxImportSelectionRenderer.RenderAll(section).TrimEnd();
            if (string.IsNullOrWhiteSpace(markdown) ||
                markdown.Length > MaxManualMarkdownLength)
            {
                throw new InvalidDataException(
                    "Nouvelle section Word vide ou trop volumineuse.");
            }

            byte[] key = System.Security.Cryptography.SHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes(
                    baseline.ImportId.ToString("N") + ":" + assignment.SectionIndex +
                    ":" + target));
            Guid fragmentId = new Guid(key.AsSpan(0, 16));
            if (originals.ContainsKey(fragmentId))
            {
                throw new InvalidDataException(
                    "Identifiant de fragment Word déjà attribué.");
            }

            newCreations.Add(new DocxReimportProposedCreation
            {
                SectionIndex = assignment.SectionIndex,
                FragmentId = fragmentId,
                DestinationRelativePath = target,
                Markdown = markdown,
                Sha256 = DocxImportManifestStore.ComputeSha256(markdown)
            });
        }

        List<DocxReimportDecisionLine> lines = [];
        List<DocxReimportProposedWrite> writes = [];
        foreach (DocxImportManifestFragment fragment in baseline.Fragments)
        {
            DocxReimportReviewedFragment current = reviewed[fragment.FragmentId];
            DocxReimportResolution? choice = choices.GetValueOrDefault(fragment.FragmentId);
            if (choice is null)
            {
                bool unchanged = current.Change?.Kind == DocxReimportChangeKind.Unchanged &&
                    !current.RequiresExplicitResolution;
                lines.Add(new DocxReimportDecisionLine
                {
                    FragmentId = fragment.FragmentId,
                    Action = null,
                    Resolved = unchanged,
                    Diagnostic = unchanged
                        ? "Aucun changement : aucune écriture proposée."
                        : "Choisissez explicitement comment traiter cette section."
                });
                continue;
            }

            if (choice.Action is DocxReimportResolutionAction.KeepNodalis or
                DocxReimportResolutionAction.KeepHistorical)
            {
                lines.Add(new DocxReimportDecisionLine
                {
                    FragmentId = fragment.FragmentId,
                    Action = choice.Action,
                    Resolved = true,
                    Diagnostic = "Markdown Nodalis conservé ; aucune écriture."
                });
                continue;
            }

            string? replacement = choice.Action switch
            {
                DocxReimportResolutionAction.UseWord => current.LatestWordMarkdown,
                DocxReimportResolutionAction.ManualMerge => choice.ManualMarkdown,
                DocxReimportResolutionAction.ManualWholeDocument => choice.ManualMarkdown,
                _ => null
            };

            string? error = null;
            if (current.Association.CurrentSectionIndex is null ||
                current.Association.Kind is DocxReimportAssociationKind.Ambiguous or
                    DocxReimportAssociationKind.ConfirmedMissingFromWord or
                    DocxReimportAssociationKind.MissingFromWord)
            {
                error = "La correspondance Word doit d'abord être confirmée.";
            }
            else if (current.Association.RequiresConfirmation)
            {
                error = "Confirmez manuellement l'identité de la section Word avant de remplacer Nodalis.";
            }
            else if (current.CurrentNodalisMarkdown is null)
            {
                error = "La destination Nodalis est absente : aucune recréation implicite.";
            }
            else if (replacement is null || string.IsNullOrWhiteSpace(replacement) ||
                     replacement.Length > MaxManualMarkdownLength)
            {
                error = "Le contenu Word ou la fusion manuelle est vide ou trop volumineux.";
            }
            else if (choice.Action == DocxReimportResolutionAction.ManualWholeDocument &&
                     !choice.ConfirmWholeDocumentReplacement)
            {
                error = "Confirmez explicitement le remplacement du fichier Markdown complet.";
            }
            else if (baseline.Fragments.Count(other =>
                         string.Equals(other.DestinationRelativePath,
                             fragment.DestinationRelativePath,
                             StringComparison.OrdinalIgnoreCase)) != 1)
            {
                error = "Plusieurs sections Word partagent ce Markdown : remplacement direct bloqué.";
            }
            else if (choice.Action == DocxReimportResolutionAction.ManualWholeDocument)
            {
                // Explicit full-file replacement is the only recovery path
                // when a user changed the original fragment beyond recognition.
                // It can never be an automatic Word-only choice.
                string local = current.CurrentNodalisMarkdown!;
                string updated = replacement!;
                bool modified = !string.Equals(local, updated, StringComparison.Ordinal);
                if (modified)
                {
                    writes.Add(new DocxReimportProposedWrite
                    {
                        FragmentId = fragment.FragmentId,
                        DestinationRelativePath = fragment.DestinationRelativePath,
                        ExpectedCurrentSha256 =
                            DocxImportManifestStore.ComputeSha256(local),
                        ProjectedSha256 = DocxImportManifestStore.ComputeSha256(updated),
                        ProjectedMarkdown = updated
                    });
                }

                lines.Add(new DocxReimportDecisionLine
                {
                    FragmentId = fragment.FragmentId,
                    Action = choice.Action,
                    Resolved = true,
                    Diagnostic = modified
                        ? "ATTENTION : remplacement EXPLICITE du Markdown ENTIER après fusion manuelle."
                        : "Fichier Markdown complet déjà identique à la fusion manuelle."
                });
                continue;
            }
            else if (string.IsNullOrEmpty(fragment.BaseMarkdown))
            {
                error = "Le fragment source initial n'a pas de délimiteur fiable.";
            }
            else
            {
                string local = current.CurrentNodalisMarkdown;
                int first = local.IndexOf(fragment.BaseMarkdown, StringComparison.Ordinal);
                if (first < 0 || local.IndexOf(fragment.BaseMarkdown,
                        first + fragment.BaseMarkdown.Length,
                        StringComparison.Ordinal) >= 0)
                {
                    error = "Le fragment initial est absent ou répété dans Nodalis ; " +
                            "préservez les modifications locales et fusionnez dans l'éditeur.";
                }
                else
                {
                    string result = string.Concat(local.AsSpan(0, first),
                        replacement.AsSpan(),
                        local.AsSpan(first + fragment.BaseMarkdown.Length));
                    bool modified = !string.Equals(local, result, StringComparison.Ordinal);
                    if (modified)
                    {
                        writes.Add(new DocxReimportProposedWrite
                        {
                            FragmentId = fragment.FragmentId,
                            DestinationRelativePath = fragment.DestinationRelativePath,
                            ExpectedCurrentSha256 =
                                DocxImportManifestStore.ComputeSha256(local),
                            ProjectedSha256 = DocxImportManifestStore.ComputeSha256(result),
                            ProjectedMarkdown = result
                        });
                    }

                    lines.Add(new DocxReimportDecisionLine
                    {
                        FragmentId = fragment.FragmentId,
                        Action = choice.Action,
                        Resolved = true,
                        Diagnostic = modified
                            ? "Remplacement borné au fragment d'origine ; contexte Nodalis conservé."
                            : "Aucun changement de contenu : écriture évitée."
                    });
                    continue;
                }
            }

            lines.Add(new DocxReimportDecisionLine
            {
                FragmentId = fragment.FragmentId,
                Action = choice.Action,
                Resolved = false,
                Diagnostic = error ?? "Résolution impossible."
            });
        }

        return new DocxReimportDecisionPlan
        {
            PreviousImportId = baseline.ImportId,
            ProjectId = baseline.ProjectId,
            Lines = lines,
            Writes = writes,
            Warnings = review.Warnings.ToList(),
            // New Word sections are not silently included in the old import.
            UnmatchedNewSections = newlyAdded.Except(excludedNew).Except(assigned)
                .OrderBy(index => index).ToList(),
            NewSectionCreations = newCreations.OrderBy(item => item.SectionIndex).ToList(),
            ExcludedNewSectionIndexes = excludedNew.OrderBy(index => index).ToList(),
            AmbiguousNewSections = review.Matching.UnresolvedNewSectionIndexes.ToList()
        };
    }
}

/// <summary>
/// Explicit placement of a newly introduced Word section in an existing
/// project folder. Folder paths are workspace-relative; no folder is created.
/// </summary>
public sealed record DocxReimportNewSectionAssignment
{
    public required int SectionIndex { get; init; }

    public required string TargetFolderRelativePath { get; init; }
}

/// <summary>New Markdown destination proposed for a Word-only section.</summary>
public sealed record DocxReimportProposedCreation
{
    public required int SectionIndex { get; init; }

    public required Guid FragmentId { get; init; }

    public required string DestinationRelativePath { get; init; }

    public required string Markdown { get; init; }

    public required string Sha256 { get; init; }
}

/// <summary>User-controlled treatment of one historical section.</summary>
public enum DocxReimportResolutionAction
{
    KeepNodalis,
    UseWord,
    ManualMerge,
    KeepHistorical,
    ManualWholeDocument
}

/// <summary>
/// One explicit treatment; ManualMarkdown is used only with ManualMerge.
/// Decisions are not persisted by the read-only inspector.
/// </summary>
public sealed record DocxReimportResolution
{
    public required Guid FragmentId { get; init; }

    public required DocxReimportResolutionAction Action { get; init; }

    public string? ManualMarkdown { get; init; }

    /// <summary>
    /// Separate informed confirmation that ManualMarkdown is the COMPLETE
    /// replacement document, including any local notes the user wants to keep.
    /// </summary>
    public bool ConfirmWholeDocumentReplacement { get; init; }
}

/// <summary>Explainable resolution status for one previous import fragment.</summary>
public sealed record DocxReimportDecisionLine
{
    public required Guid FragmentId { get; init; }

    public DocxReimportResolutionAction? Action { get; init; }

    public bool Resolved { get; init; }

    public required string Diagnostic { get; init; }
}

/// <summary>
/// Projected local Markdown bytes and an optimistic hash of the original
/// destination; these are inspectable data, not an executable write command.
/// </summary>
public sealed record DocxReimportProposedWrite
{
    public required Guid FragmentId { get; init; }

    public required string DestinationRelativePath { get; init; }

    public required string ExpectedCurrentSha256 { get; init; }

    public required string ProjectedSha256 { get; init; }

    public required string ProjectedMarkdown { get; init; }
}

/// <summary>
/// Dry-run report of all explicit decisions. No method on this type writes.
/// Even a complete report still requires a separate transaction implementation.
/// </summary>
public sealed record DocxReimportDecisionPlan
{
    public required Guid PreviousImportId { get; init; }

    public required Guid ProjectId { get; init; }

    public List<DocxReimportDecisionLine> Lines { get; init; } = [];

    public List<DocxReimportProposedWrite> Writes { get; init; } = [];

    public List<string> Warnings { get; init; } = [];

    public List<int> UnmatchedNewSections { get; init; } = [];

    /// <summary>Explicit creation-only Markdown files in existing project folders.</summary>
    public List<DocxReimportProposedCreation> NewSectionCreations { get; init; } = [];

    /// <summary>New Word sections explicitly excluded from this dry-run.</summary>
    public List<int> ExcludedNewSectionIndexes { get; init; } = [];

    public List<int> AmbiguousNewSections { get; init; } = [];

    /// <summary>Whether all historical fragments have an explicit safe treatment.</summary>
    public bool AllFragmentsResolved => Lines.All(line => line.Resolved);

    /// <summary>
    /// Every old fragment resolved, no unreviewed additions or source warnings.
    /// This is a review-status flag, NOT an authorization to write.
    /// </summary>
    public bool ReviewComplete => AllFragmentsResolved &&
        Warnings.Count == 0 &&
        UnmatchedNewSections.Count == 0 &&
        AmbiguousNewSections.Count == 0;
}
