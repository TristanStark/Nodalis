using Nodalis.Core.Importing;

namespace Nodalis.Infrastructure.Importing;

/// <summary>
/// Builds the actual heading hierarchy context of a Word section for deterministic profile rules.
/// </summary>
public static class DocxMappingSourceBuilder
{
    /// <summary>
    /// Reads source style, level and ancestor path without modifying the DOCX or its preview.
    /// </summary>
    /// <param name="section">A selected import section.</param>
    /// <param name="document">The parsed original Word blocks.</param>
    /// <returns>Matching metadata with a stable source heading.</returns>
    public static DocxMappingSourceSection Build(
        DocxImportSectionPreview section, ParsedDocxDocument document)
    {
        ArgumentNullException.ThrowIfNull(section);
        ArgumentNullException.ThrowIfNull(document);
        int? sourceIndex = section.HeadingBlockIndex;
        if (sourceIndex is not int current ||
            current < 0 || current >= document.Blocks.Count)
        {
            return new DocxMappingSourceSection { Heading = section.SourceHeading };
        }

        IReadOnlyList<DocxBlock> blocks = document.Blocks;
        DocxParagraph? heading = blocks[current].Paragraph;
        if (heading is null)
        {
            return new DocxMappingSourceSection { Heading = section.SourceHeading };
        }

        List<(int Level, string Text)> ancestry = [];
        for (int index = 0; index < current; index++)
        {
            DocxParagraph? earlier = blocks[index].Paragraph;
            if (earlier?.HeadingLevel is not int level ||
                string.IsNullOrWhiteSpace(earlier.Text))
            {
                continue;
            }

            while (ancestry.Count > 0 && ancestry[^1].Level >= level)
            {
                ancestry.RemoveAt(ancestry.Count - 1);
            }

            ancestry.Add((level, earlier.Text));
        }

        if (heading.HeadingLevel is int ownLevel)
        {
            ancestry.RemoveAll(entry => entry.Level >= ownLevel);
        }

        return new DocxMappingSourceSection
        {
            Heading = section.SourceHeading,
            WordStyleId = heading.StyleId,
            HeadingLevel = heading.HeadingLevel,
            ParentPath = string.Join(" / ", ancestry.Select(entry => entry.Text))
        };
    }
}
