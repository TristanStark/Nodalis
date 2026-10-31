using Nodalis.Core.Markdown;
using Nodalis.Core.Relations;

/// <summary>
/// Deterministic regression tests for safe relation-to-heading source ownership.
/// </summary>
internal static class BlockRelationSourceSmokeTests
{
    /// <summary>
    /// Verifies nested, sibling, unmarked and repeated block headings without disk writes.
    /// </summary>
    public static void Run()
    {
        Guid first = Guid.NewGuid();
        Guid nested = Guid.NewGuid();
        Guid target = Guid.NewGuid();

        string markdown = "# Main\n## Marked section\n" +
            MarkdownBlockIdentityCodec.FormatMarker(first) + "\n" +
            MarkdownRelationParser.Format("vérifie", target, "criterion") + "\n" +
            "### Unmarked child\n" +
            MarkdownRelationParser.Format("concerne", target, "criterion") + "\n" +
            "### Marked child\n" + MarkdownBlockIdentityCodec.FormatMarker(nested) + "\n" +
            MarkdownRelationParser.Format("dépend de", target, "criterion") + "\n" +
            "## Unmarked sibling\n" +
            MarkdownRelationParser.Format("revalide", target, "criterion") + "\n";

        MarkdownBlockIdentityScan scan = MarkdownBlockIdentityCodec.Inspect(markdown);
        IReadOnlyList<MarkdownRelationSourceBinding> sources =
            MarkdownRelationSourceResolver.Bind(MarkdownRelationParser.Parse(markdown), scan);

        Check(sources.Count == 4 &&
            sources[0].SourceBlockId == first &&
            sources[1].SourceBlockId is null &&
            sources[2].SourceBlockId == nested &&
            sources[3].SourceBlockId is null,
            "An unmarked nested heading cannot inherit its parent block identity.");

        string duplicate = markdown + "## Repeated\n" +
            MarkdownBlockIdentityCodec.FormatMarker(first) + "\n" +
            MarkdownRelationParser.Format("vérifie", target, "criterion") + "\n";
        IReadOnlyList<MarkdownRelationSourceBinding> repeated =
            MarkdownRelationSourceResolver.Bind(
                MarkdownRelationParser.Parse(duplicate),
                MarkdownBlockIdentityCodec.Inspect(duplicate));
        Check(repeated.Count == 5 &&
            repeated[0].SourceBlockId is null &&
            repeated[^1].SourceBlockId is null &&
            repeated[2].SourceBlockId == nested,
            "Neither occurrence of a duplicate block ID may claim a relation.");

        string fenced = "# Page\n~~~markdown\n## Fake\n" +
            MarkdownBlockIdentityCodec.FormatMarker(first) + "\n" +
            MarkdownRelationParser.Format("vérifie", target, "criterion") +
            "\n~~~\n## Actual\n" + MarkdownBlockIdentityCodec.FormatMarker(nested) + "\n" +
            MarkdownRelationParser.Format("vérifie", target, "criterion");
        IReadOnlyList<MarkdownRelationSourceBinding> clean =
            MarkdownRelationSourceResolver.Bind(
                MarkdownRelationParser.Parse(fenced),
                MarkdownBlockIdentityCodec.Inspect(fenced));
        Check(clean.Count == 1 && clean[0].SourceBlockId == nested,
            "Relations inside fenced code and fake headings must remain ignored.");
    }

    /// <summary>Throws on incorrect conservative source attribution.</summary>
    /// <param name="valid">Expected regression invariant.</param>
    /// <param name="message">Failure description.</param>
    private static void Check(bool valid, string message)
    {
        if (!valid)
        {
            throw new InvalidOperationException(message);
        }
    }
}
