using Nodalis.Core.Markdown;

/// <summary>
/// Regression coverage for portable v2.0 block identities without filesystem writes.
/// </summary>
internal static class MarkdownBlockIdentitySmokeTests
{
    /// <summary>
    /// Exercises opt-in assignment, moving/renaming, code fences and collision refusal.
    /// </summary>
    public static void Run()
    {
        Guid criterionId = Guid.NewGuid();
        Guid testId = Guid.NewGuid();
        string original = "# Specification\r\nTexte libre\r\n## Critere\r\nPreuve\r\n";
        MarkdownBlockIdentityScan legacy = MarkdownBlockIdentityCodec.Inspect(original);
        Assert(legacy.Blocks.Count == 2 && legacy.Blocks.All(block => block.Id is null) &&
               legacy.Issues.Count == 0, "Legacy Markdown must remain readable without IDs.");

        string identified = MarkdownBlockIdentityCodec.InsertMarker(original, 3, criterionId);
        Assert(identified.Contains("## Critere\r\n" +
               MarkdownBlockIdentityCodec.FormatMarker(criterionId) + "\r\nPreuve",
               StringComparison.Ordinal), "Assignment must preserve CRLF and original content.");
        Assert(MarkdownBlockIdentityCodec.InsertMarker(identified, 3, criterionId) == identified,
            "Repeating an identity assignment must be idempotent.");

        string renamed = identified.Replace("## Critere", "## Critere renomme", StringComparison.Ordinal);
        MarkdownBlockIdentityEntry preserved = MarkdownBlockIdentityCodec.Inspect(renamed)
            .Blocks.Single(block => block.Id == criterionId);
        Assert(preserved.Title == "Critere renomme" && preserved.HeadingLine == 3,
            "Heading rename must not change the persistent identity.");

        string reordered = "# New opening\r\n" + renamed;
        Assert(MarkdownBlockIdentityCodec.Inspect(reordered).Blocks.Single(
            block => block.Id == criterionId).HeadingLine == 4,
            "Moving content must not tie identity to a saved line number.");

        string second = MarkdownBlockIdentityCodec.InsertMarker("# Tests\n## Case\n", 2, testId);
        Assert(MarkdownBlockIdentityCodec.Inspect(second).Blocks.Single(
            block => block.Id == testId).HeadingLine == 2,
            "Independent documents can identify internal tests.");
        Assert(MarkdownBlockIdentityCodec.FindCrossDocumentCollisions(
            [new MarkdownBlockIdentitySource("Specifications/a.md", renamed),
             new MarkdownBlockIdentitySource("Tests/b.md", second)]).Count == 0,
            "Different IDs must not conflict across documents.");

        IReadOnlyList<MarkdownBlockIdentityCollision> collisions =
            MarkdownBlockIdentityCodec.FindCrossDocumentCollisions(
                [new MarkdownBlockIdentitySource("Specifications/a.md", renamed),
                 new MarkdownBlockIdentitySource("Tests/b.md", second.Replace(
                     testId.ToString("D"), criterionId.ToString("D"), StringComparison.Ordinal))]);
        Assert(collisions.Count == 1 && collisions[0].Id == criterionId &&
               collisions[0].FirstLine == 3 && collisions[0].SecondLine == 2,
            "Cross-document identity reuse must be diagnosed, not silently corrected.");

        string duplicate = identified + "## Other\r\n" +
            MarkdownBlockIdentityCodec.FormatMarker(criterionId) + "\r\n";
        Assert(MarkdownBlockIdentityCodec.Inspect(duplicate).Issues.Single().Kind ==
               MarkdownBlockIdentityIssueKind.DuplicateId,
            "Same-document duplicates must be visible.");
        AssertRejects(() => MarkdownBlockIdentityCodec.InsertMarker(duplicate, 1, testId),
            "Ambiguous documents must not be rewritten.");

        string malformed = "# A\n<!-- nodalis:block:not-a-guid -->\n";
        Assert(MarkdownBlockIdentityCodec.Inspect(malformed).Issues.Single().Kind ==
               MarkdownBlockIdentityIssueKind.MalformedMarker,
            "Malformed explicit markers must not be treated as absent.");
        AssertRejects(() => MarkdownBlockIdentityCodec.InsertMarker(malformed, 1, testId),
            "Malformed markers must not be overwritten.");

        string fenced = "# Real\n" + MarkdownBlockIdentityCodec.FormatMarker(testId) +
            "\n~~~markdown\n## Fake\n" +
            MarkdownBlockIdentityCodec.FormatMarker(criterionId) + "\n~~~\n";
        Assert(MarkdownBlockIdentityCodec.Inspect(fenced).Blocks.Count == 1,
            "Headings and markers inside fenced examples must be ignored.");

        AssertRejects(() => MarkdownBlockIdentityCodec.InsertMarker(identified, 3, testId),
            "An assigned heading must reject another identity.");
        AssertRejects(() => MarkdownBlockIdentityCodec.InsertMarker(identified, 1, criterionId),
            "A different heading must not reuse an existing identity.");

        string singleLine = MarkdownBlockIdentityCodec.InsertMarker("# A", 1, testId);
        Assert(singleLine == "# A\n" + MarkdownBlockIdentityCodec.FormatMarker(testId) &&
               MarkdownBlockIdentityCodec.Inspect(singleLine).Blocks.Single().Id == testId,
            "Single-line documents must support canonical marker insertion.");
    }

    /// <summary>
    /// Verifies that an unsafe mutation is rejected.
    /// </summary>
    /// <param name="action">Mutation expected to fail.</param>
    /// <param name="message">Regression assertion message.</param>
    private static void AssertRejects(Action action, string message)
    {
        bool rejected = false;
        try
        {
            action();
        }
        catch (InvalidOperationException)
        {
            rejected = true;
        }

        Assert(rejected, message);
    }

    /// <summary>
    /// Throws when a block-identity invariant is violated.
    /// </summary>
    /// <param name="condition">Expected result.</param>
    /// <param name="message">Failure detail.</param>
    private static void Assert(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
