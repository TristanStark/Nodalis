using Nodalis.Core.Importing;
using Nodalis.Infrastructure.Importing;

internal static class DocxReimportSectionMatcherSmokeTests
{
    /// <summary>Validates one-to-one matching, moved sections and ambiguity without touching files.</summary>
    public static void Run()
    {
        DocxBlock[] blocks =
        [
            Heading("Groupe B", 1),
            Heading("Tests", 2),
            Paragraph("Corps B initial"),
            Heading("Groupe A", 1),
            Heading("Tests renommés", 2),
            Paragraph("Corps A identique"),
            Heading("Nouvelle rubrique", 1),
            Paragraph("Contenu inédit")
        ];

        DocxImportPreview latest = Preview(blocks,
        [
            Section(0, "Tests", 1, 2, "Corps B initial"),
            Section(1, "Tests renommés", 4, 5, "Corps A identique"),
            Section(2, "Nouvelle rubrique", 6, 7, "Contenu inédit")
        ]);

        DocxImportManifestFragment first = Fragment(0, 2, "Tests",
            "Groupe A", "Corps A identique");
        DocxImportManifestFragment second = Fragment(1, 5, "Tests",
            "Groupe B", "Corps B initial");
        DocxImportManifestFragment removed = Fragment(2, 8,
            "Ancienne annexe", "", "Contenu supprimé");

        DocxReimportMatchReport report = DocxReimportSectionMatcher.Match(
            Manifest([first, second, removed]), latest);
        DocxReimportSectionAssociation firstAssociation =
            report.Associations.Single(row => row.FragmentId == first.FragmentId);
        DocxReimportSectionAssociation secondAssociation =
            report.Associations.Single(row => row.FragmentId == second.FragmentId);

        Assert(firstAssociation.CurrentSectionIndex == 1 &&
               firstAssociation.Kind == DocxReimportAssociationKind.ExactBody &&
               firstAssociation.RequiresConfirmation &&
               secondAssociation.CurrentSectionIndex == 0 &&
               secondAssociation.Kind == DocxReimportAssociationKind.ExactBody &&
               report.Associations.Single(row => row.FragmentId == removed.FragmentId).Kind ==
                   DocxReimportAssociationKind.MissingFromWord &&
               report.AddedSectionIndexes.SequenceEqual([2]),
            "Distinct body fingerprints must match reordered, renamed homonymous headings without positional guessing.");

        // A body edit is still matchable when heading and ancestry are uniquely
        // recorded, even though the paragraph SHA-256 has changed.
        DocxImportManifestFragment context = Fragment(0, 1,
            "Tests", "Groupe B", "Ancien corps");
        DocxReimportMatchReport changedBody = DocxReimportSectionMatcher.Match(
            Manifest([context]), Preview(blocks,
            [
                Section(0, "Tests", 1, 2, "Corps B initial")
            ]));
        Assert(changedBody.Associations[0].Kind ==
               DocxReimportAssociationKind.HeadingAndAncestry &&
               changedBody.Associations[0].CurrentSectionIndex == 0,
            "Unique heading-plus-ancestry must associate changed body text.");

        // Two identically titled sections in the same context must never
        // match simply because their ordinal positions look plausible.
        DocxImportManifestFragment duplicateA = Fragment(0, 0, "Tests", "",
            "Corps doublon", parentKnown: false);
        DocxImportManifestFragment duplicateB = Fragment(1, 2, "Tests", "",
            "Corps doublon", parentKnown: false);
        DocxBlock[] duplicateBlocks =
        [
            Heading("Tests", 1),
            Paragraph("Corps doublon"),
            Heading("Tests", 1),
            Paragraph("Corps doublon")
        ];
        DocxReimportMatchReport duplicates = DocxReimportSectionMatcher.Match(
            Manifest([duplicateA, duplicateB]), Preview(duplicateBlocks,
            [
                Section(0, "Tests", 0, 1, "Corps doublon"),
                Section(1, "Tests", 2, 3, "Corps doublon")
            ]));
        Assert(duplicates.Associations.All(row =>
                   row.Kind == DocxReimportAssociationKind.Ambiguous &&
                   row.CurrentSectionIndex is null &&
                   row.CandidateSectionIndexes.Count == 2) &&
               duplicates.AddedSectionIndexes.Count == 0 &&
               duplicates.UnresolvedNewSectionIndexes.SequenceEqual([0, 1]),
            "Identical headings and contents must remain unresolved, not auto-merged or counted as additions.");

        // User choices must retain one-to-one ownership and must not mutate
        // the original proposal or the read-only Word preview.
        DocxImportPreview duplicatePreview = Preview(duplicateBlocks,
        [
            Section(0, "Tests", 0, 1, "Corps doublon"),
            Section(1, "Tests", 2, 3, "Corps doublon")
        ]);
        DocxReimportMatchReport adjudicated = DocxReimportMatchResolver.Resolve(
            duplicates, duplicatePreview,
            [
                new DocxReimportSectionMatchOverride
                {
                    FragmentId = duplicateA.FragmentId, CurrentSectionIndex = 1
                },
                new DocxReimportSectionMatchOverride
                {
                    FragmentId = duplicateB.FragmentId, CurrentSectionIndex = 0
                }
            ]);
        Assert(adjudicated.Associations.All(row =>
                   row.Kind == DocxReimportAssociationKind.Manual &&
                   !row.RequiresConfirmation) &&
               adjudicated.AddedSectionIndexes.Count == 0 &&
               adjudicated.UnresolvedNewSectionIndexes.Count == 0 &&
               duplicates.Associations.All(row => row.CurrentSectionIndex is null),
            "Two explicit choices must resolve homonyms while keeping the baseline immutable.");

        bool duplicateRejected = false;
        try
        {
            DocxReimportMatchResolver.Resolve(duplicates, duplicatePreview,
            [
                new DocxReimportSectionMatchOverride
                {
                    FragmentId = duplicateA.FragmentId, CurrentSectionIndex = 0
                },
                new DocxReimportSectionMatchOverride
                {
                    FragmentId = duplicateB.FragmentId, CurrentSectionIndex = 0
                }
            ]);
        }
        catch (InvalidDataException)
        {
            duplicateRejected = true;
        }

        Assert(duplicateRejected,
            "Two previous fragments must never share the same chosen Word section.");

        bool staleRejected = false;
        try
        {
            DocxReimportMatchResolver.Resolve(duplicates, duplicatePreview,
            [
                new DocxReimportSectionMatchOverride
                {
                    FragmentId = duplicateA.FragmentId, CurrentSectionIndex = 99
                }
            ]);
        }
        catch (InvalidDataException)
        {
            staleRejected = true;
        }

        Assert(staleRejected, "Stale section indexes must fail closed.");

        DocxReimportMatchReport markedDeleted = DocxReimportMatchResolver.Resolve(
            duplicates, duplicatePreview,
            [
                new DocxReimportSectionMatchOverride
                {
                    FragmentId = duplicateA.FragmentId, CurrentSectionIndex = null
                }
            ]);
        Assert(markedDeleted.Associations.Single(row =>
                   row.FragmentId == duplicateA.FragmentId).Kind ==
                   DocxReimportAssociationKind.ConfirmedMissingFromWord &&
               markedDeleted.Associations.Single(row =>
                   row.FragmentId == duplicateA.FragmentId).RequiresConfirmation == false &&
               markedDeleted.Associations.Single(row =>
                   row.FragmentId == duplicateB.FragmentId).Kind ==
                   DocxReimportAssociationKind.Ambiguous,
            "Confirming Word deletion must not erase Nodalis data or resolve other ambiguities.");

        // Old manifests without ancestry are still usable for globally
        // unique headings, but association must be reviewed.
        DocxImportManifestFragment legacy = Fragment(0, null, "Nouvelle rubrique",
            "", "Corps devenu différent", parentKnown: false);
        DocxReimportMatchReport compatible = DocxReimportSectionMatcher.Match(
            Manifest([legacy]), latest);
        Assert(compatible.Associations.Single().Kind ==
               DocxReimportAssociationKind.UniqueHeading &&
               compatible.Associations.Single().CurrentSectionIndex == 2 &&
               compatible.Associations.Single().RequiresConfirmation,
            "Old context-less provenance must allow tentative matching by a globally unique heading.");
    }

    /// <summary>Builds a baseline fragment including a deterministic heading/body snapshot.</summary>
    private static DocxImportManifestFragment Fragment(
        int index, int? headingIndex, string title,
        string parent, string body, bool parentKnown = true)
    {
        string markdown = "## " + title + "\n\n" + body;
        return new DocxImportManifestFragment
        {
            FragmentId = Guid.NewGuid(),
            SourceSectionIndex = index,
            HeadingBlockIndex = headingIndex,
            SourceHeading = title,
            SourceParentPath = parentKnown ? parent : null,
            TargetSection = "Tests",
            DestinationRelativePath = "Projet/Tests/import.md",
            BaseMarkdown = markdown,
            FragmentSha256 = DocxImportManifestStore.ComputeSha256(markdown),
            DestinationSha256 = DocxImportManifestStore.ComputeSha256(markdown)
        };
    }

    /// <summary>Wraps baseline fragments for the matching engine.</summary>
    private static DocxImportManifest Manifest(List<DocxImportManifestFragment> fragments) =>
        new DocxImportManifest
        {
            ImportId = Guid.NewGuid(),
            ProjectId = Guid.NewGuid(),
            ImportedAtUtc = DateTimeOffset.UtcNow,
            SourceFileName = "spec.docx",
            SourceSha256 = new string('a', 64),
            SourceCopyRelativePath = "Imports/Sources/spec.docx",
            Fragments = fragments
        };

    /// <summary>Builds a synthetic preview with the original Word block indices.</summary>
    private static DocxImportPreview Preview(
        DocxBlock[] blocks, List<DocxImportSectionPreview> sections) =>
        new DocxImportPreview
        {
            StagedImport = new DocxStagedImport
            {
                OriginalSourcePath = "spec.docx",
                StagedCopyPath = "spec.docx",
                Document = new ParsedDocxDocument { Blocks = blocks.ToList() }
            },
            Analysis = new DocxImportAnalysis(),
            Sections = sections
        };

    /// <summary>Builds one selectable source section for a specific Word heading and body.</summary>
    private static DocxImportSectionPreview Section(
        int index, string heading, int headingIndex, int bodyIndex, string body)
    {
        DocxBlock paragraph = Paragraph(body);
        return new DocxImportSectionPreview
        {
            Index = index,
            SourceHeading = heading,
            SuggestedTargetSection = "Tests",
            BlockCount = 1,
            HeadingBlockIndex = headingIndex,
            MarkdownPreview = "## " + heading + "\n\n" + body,
            Blocks =
            [
                new DocxImportBlockPreview
                {
                    BlockIndex = bodyIndex,
                    Block = paragraph,
                    KindLabel = "Paragraphe",
                    DisplayText = body,
                    MarkdownPreview = body
                }
            ]
        };
    }

    /// <summary>Creates a source paragraph.</summary>
    private static DocxBlock Paragraph(string text) =>
        new DocxBlock
        {
            Kind = DocxBlockKind.Paragraph,
            Paragraph = new DocxParagraph { Text = text }
        };

    /// <summary>Creates a source heading.</summary>
    private static DocxBlock Heading(string text, int level) =>
        new DocxBlock
        {
            Kind = DocxBlockKind.Paragraph,
            Paragraph = new DocxParagraph { Text = text, HeadingLevel = level }
        };

    /// <summary>Fails when a section identity is guessed incorrectly.</summary>
    private static void Assert(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
