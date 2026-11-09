using Nodalis.Core.Importing;
using Nodalis.Infrastructure.Importing;

/// <summary>
/// Enforces the emergency fail-closed policy at every active DOCX import/
/// conversion entry point. No Word file is opened by these tests.
/// </summary>
internal static class DocxImportDisabledSmokeTests
{
    /// <summary>Verifies all active import paths refuse DOCX before any I/O.</summary>
    public static async Task RunAsync(string root)
    {
        WorkspaceDocxImportService importer = new WorkspaceDocxImportService(root);
        string source = Path.Combine(root, "forbidden.docx");

        await AssertForbiddenAsync(() => new DocxParser().ParseAsync(source));
        await AssertForbiddenAsync(() => importer.StageAsync(source));
        await AssertForbiddenAsync(() => importer.ImportAsync(source));
        await AssertForbiddenAsync(() => importer.PreparePreviewAsync(source));
        await AssertForbiddenAsync(() => importer.BuildPlanAsync(null!, null!));
        await AssertForbiddenAsync(() => importer.CommitAsync(null!, null!));
        await AssertForbiddenAsync(() => new DocxStructuredImportReader().ReadAsync(source));
        await AssertForbiddenAsync(() => new DocxReimportTransactionService(root)
            .CommitAsync(root, null!, Guid.NewGuid(), string.Empty));

        AssertForbidden(() => DocxMarkdownConverter.ConvertBlocks([]));
        AssertForbidden(() => DocxStructuredImportReader.FromParsed(
            new ParsedDocxDocument { Blocks = [] }, "forbidden.docx", new string('A', 64)));

        if (File.Exists(source))
        {
            throw new InvalidOperationException("A disabled import created a source DOCX file.");
        }

        // Repair of interrupted historical imports must remain accessible.
        DocxFirstImportRecoveryService recovery = new DocxFirstImportRecoveryService(root);
        await recovery.ListPendingProjectsAsync();
    }

    /// <summary>Asserts an asynchronous entry point is blocked by the compliance policy.</summary>
    private static async Task AssertForbiddenAsync(Func<Task> operation)
    {
        try
        {
            await operation();
        }
        catch (NotSupportedException exception) when (
            exception.Message == DocxImportPolicy.DisabledMessage)
        {
            return;
        }

        throw new InvalidOperationException("DOCX import/processing was not blocked.");
    }

    /// <summary>Asserts a synchronous entry point is blocked by the compliance policy.</summary>
    private static void AssertForbidden(Action operation)
    {
        try
        {
            operation();
        }
        catch (NotSupportedException exception) when (
            exception.Message == DocxImportPolicy.DisabledMessage)
        {
            return;
        }

        throw new InvalidOperationException("DOCX conversion was not blocked.");
    }
}
