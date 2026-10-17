using Nodalis.Core.Maintenance;
using Nodalis.Infrastructure.Reliability;

namespace Nodalis.Infrastructure.Maintenance;

/// <summary>
/// Uses the existing revision-checked atomic text-document writer for maintenance files.
/// </summary>
public sealed class WorkspaceMaintenanceService
{
    /// <summary>Reads records without creating or modifying a maintenance document.</summary>
    /// <param name="path">Existing Markdown file path.</param>
    /// <param name="cancellationToken">Operation cancellation.</param>
    /// <returns>Validated records and diagnostics.</returns>
    public async Task<MaintenanceParseResult> ReadAsync(
        string path, CancellationToken cancellationToken = default)
    {
        TextDocumentSession session = await TextDocumentSession.OpenAsync(path, cancellationToken);
        return MaintenanceMarkdownCodec.Parse(session.Content);
    }

    /// <summary>
    /// Appends a new intervention using an atomic and revision-checked save.
    /// Any external notes changed before the call are read and retained.
    /// </summary>
    /// <param name="path">Existing maintenance document.</param>
    /// <param name="entry">New intervention.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The saved Markdown text.</returns>
    public async Task<string> AddAsync(
        string path, MaintenanceRecord entry, CancellationToken cancellationToken = default)
    {
        TextDocumentSession session = await TextDocumentSession.OpenAsync(path, cancellationToken);
        string updated = MaintenanceMarkdownCodec.Append(session.Content, entry);
        await session.SaveAsync(updated, cancellationToken);
        return session.Content;
    }

    /// <summary>
    /// Updates an intervention while checking its original values and the file revision.
    /// Concurrent modifications to the intervention are rejected; other notes survive.
    /// </summary>
    /// <param name="path">Existing maintenance document.</param>
    /// <param name="original">Record snapshot captured before opening the form.</param>
    /// <param name="replacement">Edited record with the same ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The safely saved Markdown.</returns>
    public async Task<string> EditAsync(
        string path, MaintenanceRecord original, MaintenanceRecord replacement,
        CancellationToken cancellationToken = default)
    {
        TextDocumentSession session = await TextDocumentSession.OpenAsync(path, cancellationToken);
        string updated = MaintenanceMarkdownEditor.Replace(session.Content, original, replacement);
        await session.SaveAsync(updated, cancellationToken);
        return session.Content;
    }
}
