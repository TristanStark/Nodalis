using Nodalis.Core.Importing;

namespace Nodalis.Infrastructure.Importing;

/// <summary>
/// Applies explicitly approved structural corrections to an in-memory DOCX preview.
/// The original DOCX and previously parsed source blocks are never changed.
/// </summary>
public static class DocxImportStructureEditor
{
    /// <summary>
    /// Reclassifies a paragraph as body text or a heading, preserving its source identity.
    /// </summary>
    /// <param name="section">Selected source section.</param>
    /// <param name="blockIndex">Original source block index.</param>
    /// <param name="headingLevel">Null for paragraph, or a heading level 1–9.</param>
    /// <returns>The original heading level, for a reversible edit.</returns>
    public static int? ChangeHeading(
        DocxImportSectionPreview section,
        int blockIndex,
        int? headingLevel)
    {
        ArgumentNullException.ThrowIfNull(section);
        if (headingLevel is not null and (< 1 or > 9))
        {
            throw new InvalidDataException("Le niveau de titre doit être compris entre 1 et 9.");
        }

        int position = section.Blocks.FindIndex(candidate =>
            candidate.BlockIndex == blockIndex);
        if (position < 0)
        {
            throw new InvalidDataException("Bloc Word introuvable pour la correction.");
        }

        DocxImportBlockPreview original = section.Blocks[position];
        if (original.Block.Kind != DocxBlockKind.Paragraph ||
            original.Block.Paragraph is null)
        {
            throw new InvalidDataException("Seul un paragraphe Word peut être reclassé.");
        }

        int? previousLevel = original.Block.Paragraph.HeadingLevel;
        DocxBlock revised = original.Block with
        {
            Paragraph = original.Block.Paragraph with
            {
                HeadingLevel = headingLevel
            }
        };

        section.Blocks[position] = original with
        {
            Block = revised,
            KindLabel = headingLevel is int level
                ? "Titre " + level
                : revised.Paragraph!.IsListItem ? "Liste" : "Paragraphe",
            MarkdownPreview = DocxMarkdownConverter.ConvertBlocks([revised]).Trim(),
            IsSectionHeading = headingLevel is not null
        };

        // A new heading owns subsequent blocks until the next heading at its level.
        // Only the hierarchy within the same pre-existing mapped section is corrected.
        List<(int Index, int Level)> headings = [];
        int? lastSourceIndex = null;
        for (int index = 0; index < section.Blocks.Count; index++)
        {
            DocxImportBlockPreview block = section.Blocks[index];
            if (lastSourceIndex is int previous && block.BlockIndex != previous + 1)
            {
                headings.Clear();
            }

            int? parent;
            if (block.Block.Paragraph?.HeadingLevel is int level2)
            {
                while (headings.Count > 0 && headings[^1].Level >= level2)
                {
                    headings.RemoveAt(headings.Count - 1);
                }

                parent = headings.Count == 0 ? null : headings[^1].Index;
                headings.Add((block.BlockIndex, level2));
            }
            else
            {
                parent = headings.Count == 0 ? null : headings[^1].Index;
            }

            section.Blocks[index] = block with { ParentBlockIndex = parent };
            lastSourceIndex = block.BlockIndex;
        }

        return previousLevel;
    }
}
