using Nodalis.Core.Importing;

namespace Nodalis.Infrastructure.Importing;

/// <summary>
/// Constructs the next immutable DOCX history from an explicitly reviewed
/// reimport. This is a pure preparation step apart from hashing the staged
/// source file. It never copies the DOCX or writes a manifest/Markdown.
/// </summary>
public sealed class DocxReimportProvenanceBuilder
{
    private readonly DocxImportManifestStore _paths;

    /// <summary>Initializes a local workspace lineage builder.</summary>
    public DocxReimportProvenanceBuilder(string workspaceRoot)
    {
        _paths = new DocxImportManifestStore(workspaceRoot);
    }

    /// <summary>
    /// Prepares a versioned manifest for the already approved plan. Word and
    /// manual resolutions advance the baseline; retained Nodalis content keeps
    /// the previous imported baseline and records the newer Word text for
    /// inspection. Word-deleted sections remain in prior immutable history.
    /// </summary>
    public async Task<DocxImportManifest> BuildAsync(
        DocxImportManifest previous,
        DocxReimportReview review,
        DocxReimportDecisionPlan plan,
        DocxImportPreview latest,
        IReadOnlyCollection<DocxReimportResolution> decisions,
        Guid operationId,
        string newSourceCopyRelativePath,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(previous);
        ArgumentNullException.ThrowIfNull(review);
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(latest);
        ArgumentNullException.ThrowIfNull(decisions);
        _paths.Validate(previous);

        if (operationId == Guid.Empty || previous.ImportId == operationId ||
            review.PreviousImportId != previous.ImportId ||
            review.ProjectId != previous.ProjectId ||
            plan.PreviousImportId != previous.ImportId ||
            plan.ProjectId != previous.ProjectId || !plan.ReviewComplete ||
            !string.Equals(Path.GetExtension(newSourceCopyRelativePath),
                ".docx", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                "Provenance de réimport incomplète, non approuvée ou mal ciblée.");
        }

        // A deterministic per-operation source path makes replay safe and
        // lets recovery distinguish a new Word copy from unrelated files.
        string requiredSourcePath =
            "Imports/Sources/reimport-" + operationId.ToString("N") + ".docx";
        if (!string.Equals(newSourceCopyRelativePath, requiredSourcePath,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                "La nouvelle copie DOCX doit utiliser le chemin immuable de cette opération.");
        }

        string sourceCopy = _paths.ResolveWorkspaceRelativePath(
            newSourceCopyRelativePath);
        string oldCopy = _paths.ResolveWorkspaceRelativePath(
            previous.SourceCopyRelativePath);
        if (string.Equals(sourceCopy, oldCopy, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                "Un réimport ne peut pas remplacer la copie Word historique.");
        }

        Dictionary<Guid, DocxReimportResolution> choices = [];
        foreach (DocxReimportResolution decision in decisions)
        {
            if (!choices.TryAdd(decision.FragmentId, decision))
            {
                throw new InvalidDataException(
                    "Décisions de réimport en doublon.");
            }
        }

        HashSet<Guid> known = previous.Fragments
            .Select(fragment => fragment.FragmentId).ToHashSet();
        if (choices.Keys.Any(id => !known.Contains(id)) ||
            review.Fragments.Count != previous.Fragments.Count ||
            review.Matching.Associations.Count != previous.Fragments.Count ||
            review.Fragments.Select(row => row.Association.FragmentId)
                .Distinct().Count() != previous.Fragments.Count)
        {
            throw new InvalidDataException(
                "Rapport de réimport ou décisions incompatibles avec le manifeste initial.");
        }

        Dictionary<Guid, DocxReimportProposedWrite> writes = [];
        foreach (DocxReimportProposedWrite write in plan.Writes)
        {
            if (!writes.TryAdd(write.FragmentId, write) ||
                !known.Contains(write.FragmentId))
            {
                throw new InvalidDataException(
                    "Écriture du réimport inconnue ou répétée.");
            }
        }

        List<DocxImportManifestFragment> fragments = [];
        foreach (DocxImportManifestFragment original in previous.Fragments)
        {
            cancellationToken.ThrowIfCancellationRequested();
            DocxReimportReviewedFragment reviewed = review.Fragments.Single(row =>
                row.Association.FragmentId == original.FragmentId);
            DocxReimportResolutionAction action =
                choices.TryGetValue(original.FragmentId, out DocxReimportResolution? choice)
                    ? choice.Action : DocxReimportResolutionAction.KeepNodalis;
            DocxReimportSectionAssociation association = reviewed.Association;
            if (association.CurrentSectionIndex is not int selected ||
                association.Kind == DocxReimportAssociationKind.ConfirmedMissingFromWord ||
                association.Kind == DocxReimportAssociationKind.Ambiguous ||
                association.Kind == DocxReimportAssociationKind.MissingFromWord)
            {
                if (writes.ContainsKey(original.FragmentId))
                {
                    throw new InvalidDataException(
                        "Une section absente du Word ne peut pas être réécrite.");
                }

                continue;
            }

            if (association.RequiresConfirmation &&
                (action is DocxReimportResolutionAction.UseWord or
                    DocxReimportResolutionAction.ManualMerge or
                    DocxReimportResolutionAction.ManualWholeDocument))
            {
                throw new InvalidDataException(
                    "Une association de section Word doit être confirmée.");
            }

            DocxImportSectionPreview section = latest.Sections.Single(item =>
                item.Index == selected);
            string? wordText = reviewed.LatestWordMarkdown;
            if (wordText is null)
            {
                throw new InvalidDataException(
                    "Snapshot Word absent pour une section réimportée.");
            }

            string baseline = action switch
            {
                DocxReimportResolutionAction.UseWord => wordText,
                DocxReimportResolutionAction.ManualMerge or
                    DocxReimportResolutionAction.ManualWholeDocument =>
                    choice?.ManualMarkdown ?? throw new InvalidDataException(
                        "Fusion manuelle absente de la décision."),
                _ => original.BaseMarkdown
            };
            if (string.IsNullOrWhiteSpace(baseline))
            {
                throw new InvalidDataException(
                    "Fragment source du nouvel historique vide.");
            }

            string? local = reviewed.CurrentNodalisMarkdown;
            if (local is null)
            {
                throw new InvalidDataException(
                    "Markdown Nodalis absent : impossible de versionner le réimport.");
            }

            string result = local;
            if (writes.TryGetValue(original.FragmentId,
                    out DocxReimportProposedWrite? write))
            {
                if (action is not (DocxReimportResolutionAction.UseWord or
                    DocxReimportResolutionAction.ManualMerge or
                    DocxReimportResolutionAction.ManualWholeDocument) ||
                    !string.Equals(write.DestinationRelativePath,
                        original.DestinationRelativePath,
                        StringComparison.OrdinalIgnoreCase) ||
                    !string.Equals(write.ExpectedCurrentSha256,
                        DocxImportManifestStore.ComputeSha256(local),
                        StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException(
                        "L'écriture projetée n'est pas cohérente avec le fragment source.");
                }

                string expected;
                if (action == DocxReimportResolutionAction.ManualWholeDocument)
                {
                    if (choice?.ConfirmWholeDocumentReplacement != true ||
                        previous.Fragments.Count(fragment => string.Equals(
                            fragment.DestinationRelativePath,
                            original.DestinationRelativePath,
                            StringComparison.OrdinalIgnoreCase)) != 1)
                    {
                        throw new InvalidDataException(
                            "Fusion du fichier complet non confirmée ou source partagée.");
                    }

                    expected = baseline;
                }
                else
                {
                    int start = local.IndexOf(original.BaseMarkdown, StringComparison.Ordinal);
                    if (start < 0 || local.IndexOf(original.BaseMarkdown,
                            start + original.BaseMarkdown.Length,
                            StringComparison.Ordinal) >= 0)
                    {
                        throw new InvalidDataException(
                            "Fragment source Nodalis introuvable ou non unique.");
                    }

                    expected = string.Concat(local.AsSpan(0, start),
                        baseline.AsSpan(),
                        local.AsSpan(start + original.BaseMarkdown.Length));
                }
                if (!string.Equals(expected, write.ProjectedMarkdown,
                        StringComparison.Ordinal) ||
                    !string.Equals(DocxImportManifestStore.ComputeSha256(expected),
                        write.ProjectedSha256, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException(
                        "Projection de réimport différente de l'arbitrage approuvé.");
                }

                result = expected;
            }
            else if (((action is DocxReimportResolutionAction.UseWord or
                DocxReimportResolutionAction.ManualMerge) &&
                !string.Equals(original.BaseMarkdown, baseline, StringComparison.Ordinal)) ||
                (action == DocxReimportResolutionAction.ManualWholeDocument &&
                 (!string.Equals(local, baseline, StringComparison.Ordinal) ||
                  choice?.ConfirmWholeDocumentReplacement != true)))
            {
                throw new InvalidDataException(
                    "Changement Word ou fusion sans projection d'écriture.");
            }

            DocxMappingSourceSection source = DocxMappingSourceBuilder.Build(
                section, latest.StagedImport.Document);
            fragments.Add(original with
            {
                SourceSectionIndex = section.Index,
                HeadingBlockIndex = section.HeadingBlockIndex,
                SourceHeading = section.SourceHeading,
                SourceParentPath = source.ParentPath,
                SourceWordStyleId = source.WordStyleId,
                SourceHeadingLevel = source.HeadingLevel,
                SelectedBlockIndexes = section.Blocks.Select(item =>
                    item.BlockIndex).OrderBy(index => index).ToList(),
                OriginalSourceBlockCount = section.Blocks.Count,
                BaseMarkdown = baseline,
                BaselineCoversWholeDestination =
                    original.BaselineCoversWholeDestination ||
                    action == DocxReimportResolutionAction.ManualWholeDocument,
                FragmentSha256 = DocxImportManifestStore.ComputeSha256(baseline),
                DestinationSha256 = DocxImportManifestStore.ComputeSha256(result),
                LatestWordMarkdownSnapshot = wordText,
                LatestWordSnapshotSha256 = DocxImportManifestStore.ComputeSha256(wordText),
                ReimportResolution = action switch
                {
                    DocxReimportResolutionAction.UseWord => "Word",
                    DocxReimportResolutionAction.ManualMerge => "Manual",
                    DocxReimportResolutionAction.ManualWholeDocument => "ManualFullDocument",
                    _ => "Nodalis"
                }
            });
        }

        foreach (DocxReimportProposedCreation created in plan.NewSectionCreations)
        {
            cancellationToken.ThrowIfCancellationRequested();
            DocxImportSectionPreview section = latest.Sections.Single(item =>
                item.Index == created.SectionIndex);
            string expectedMarkdown = DocxImportSelectionRenderer.RenderAll(section).TrimEnd();
            if (!string.Equals(expectedMarkdown, created.Markdown,
                    StringComparison.Ordinal) ||
                !string.Equals(created.Sha256,
                    DocxImportManifestStore.ComputeSha256(expectedMarkdown),
                    StringComparison.OrdinalIgnoreCase) ||
                fragments.Any(item =>
                    item.FragmentId == created.FragmentId ||
                    string.Equals(item.DestinationRelativePath,
                        created.DestinationRelativePath, StringComparison.OrdinalIgnoreCase)))
            {
                throw new InvalidDataException(
                    "La nouvelle section Word ne correspond pas au plan approuvé.");
            }

            DocxMappingSourceSection newSource = DocxMappingSourceBuilder.Build(
                section, latest.StagedImport.Document);
            string destinationFolder = Path.GetFileName(
                Path.GetDirectoryName(created.DestinationRelativePath) ??
                string.Empty);
            fragments.Add(new DocxImportManifestFragment
            {
                FragmentId = created.FragmentId,
                SourceSectionIndex = section.Index,
                HeadingBlockIndex = section.HeadingBlockIndex,
                SourceHeading = section.SourceHeading,
                SourceParentPath = newSource.ParentPath,
                SourceWordStyleId = newSource.WordStyleId,
                SourceHeadingLevel = newSource.HeadingLevel,
                TargetSection = string.IsNullOrWhiteSpace(destinationFolder)
                    ? "Notes" : destinationFolder,
                DestinationRelativePath = created.DestinationRelativePath,
                SelectedBlockIndexes = section.Blocks
                    .Select(item => item.BlockIndex).OrderBy(index => index).ToList(),
                OriginalSourceBlockCount = section.Blocks.Count,
                BaseMarkdown = created.Markdown,
                FragmentSha256 = created.Sha256,
                DestinationSha256 = created.Sha256,
                LatestWordMarkdownSnapshot = created.Markdown,
                LatestWordSnapshotSha256 = created.Sha256,
                ReimportResolution = "Word"
            });
        }

        if (fragments.Count == 0 ||
            fragments.Select(fragment => fragment.SourceSectionIndex)
                .Distinct().Count() != fragments.Count ||
            writes.Keys.Any(id => !fragments.Any(fragment =>
                fragment.FragmentId == id)))
        {
            throw new InvalidDataException(
                "Le nouvel historique Word n'a aucune section active ou contient des doublons.");
        }

        string stagedHash = await DocxImportManifestStore.ComputeFileSha256Async(
            latest.StagedImport.StagedCopyPath, cancellationToken);

        DocxImportManifest resultManifest = new DocxImportManifest
        {
            ImportId = operationId,
            ProjectId = previous.ProjectId,
            PreviousImportId = previous.ImportId,
            ReimportOperationId = operationId,
            ImportedAtUtc = DateTimeOffset.UtcNow,
            SourceFileName = Path.GetFileName(latest.StagedImport.OriginalSourcePath),
            SourceSha256 = stagedHash,
            SourceCopyRelativePath = newSourceCopyRelativePath,
            AppliedMappingProfile = previous.AppliedMappingProfile,
            ApprovedPlanFingerprint = DocxReimportTransactionService.Fingerprint(plan),
            ExcludedSectionIndexes = plan.ExcludedNewSectionIndexes.ToList(),
            Fragments = fragments
        };
        _paths.Validate(resultManifest);
        return resultManifest;
    }
}
