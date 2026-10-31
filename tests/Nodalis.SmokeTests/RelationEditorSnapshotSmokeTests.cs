using Nodalis.Core.Relations;

/// <summary>Read-only approval-boundary regressions for Markdown relations.</summary>
internal static class RelationEditorSnapshotSmokeTests
{
    /// <summary>Exercises source revision/selection validation and rejection cases.</summary>
    public static void Run()
    {
        Guid id = Guid.NewGuid();
        string path = Path.Combine(Path.GetTempPath(), "Nodalis", "RelationSource.md");
        string markdown = "# Story\n## Scenario\nInitial text\n";
        RelationEditorSnapshot approved =
            RelationEditorSnapshot.Capture(id, path, markdown, 20, 0);

        Assert(approved.Matches(id, path, markdown, 20, 0),
            "An untouched approved source must be accepted.");
        Assert(!approved.Matches(Guid.NewGuid(), path, markdown, 20, 0),
            "Another document must not inherit a source approval.");
        Assert(!approved.Matches(id, path + ".bak", markdown, 20, 0),
            "A moved document must invalidate the original path approval.");
        Assert(!approved.Matches(id, path, markdown + "external", 20, 0),
            "A changed editor revision must invalidate approval.");
        Assert(!approved.Matches(id, path, markdown, 21, 0) &&
            !approved.Matches(id, path, markdown, 20, 1),
            "A changed cursor/selection must not silently re-target the relation.");

        bool invalidRejected = false;
        try
        {
            RelationEditorSnapshot.Capture(id, path, markdown, markdown.Length + 1, 0);
        }
        catch (ArgumentOutOfRangeException)
        {
            invalidRejected = true;
        }

        Assert(invalidRejected, "An impossible source selection cannot be approved.");
    }

    /// <summary>Fails when an approval constraint is not maintained.</summary>
    /// <param name="condition">Expected invariant.</param>
    /// <param name="message">Regression diagnostics.</param>
    private static void Assert(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
