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

    /// <summary>
    /// Applies a human link or ignore decision to the existing Markdown with
    /// block fingerprints and atomic revision checking. Neither incident is merged.
    /// </summary>
    /// <param name="first">Original source incident snapshot.</param>
    /// <param name="second">Compared incident snapshot in the same file.</param>
    /// <param name="decision">Explicit user choice.</param>
    /// <param name="cancellationToken">Operation cancellation.</param>
    /// <returns>Contents written atomically to the source sheet.</returns>
    public async Task<string> ReviewIncidentsAsync(
        MaintenanceHistoryItem first, MaintenanceHistoryItem second,
        MaintenanceRecurrenceDecision decision,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(first);
        ArgumentNullException.ThrowIfNull(second);
        TextDocumentSession session = await TextDocumentSession.OpenAsync(
            first.SourcePath, cancellationToken);
        string updated = MaintenanceIncidentReviewEditor.Apply(
            session.Content, first, second, decision);
        if (!string.Equals(updated, session.Content, StringComparison.Ordinal))
        {
            await session.SaveAsync(updated, cancellationToken);
        }

        return session.Content;
    }


    /// <summary>
    /// Inserts a declared typed relation using stable document identity, a checked
    /// maintenance-block fingerprint, and atomic revision-controlled persistence.
    /// </summary>
    /// <param name="record">Snapshot of the selected source record.</param>
    /// <param name="type">User-chosen relation type.</param>
    /// <param name="targetId">Current document GUID.</param>
    /// <param name="label">Human-readable target name.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    /// <returns>Saved Markdown text, or unchanged text if this exact link already exists.</returns>
    public async Task<string> AddRelationAsync(MaintenanceHistoryItem record,
        string type, Guid targetId, string label,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(record);
        TextDocumentSession session = await TextDocumentSession.OpenAsync(
            record.SourcePath, cancellationToken);
        string updated = MaintenanceRelationEditor.Add(session.Content,
            record, type, targetId, label);
        if (!string.Equals(updated, session.Content, StringComparison.Ordinal))
        {
            await session.SaveAsync(updated, cancellationToken);
        }

        return session.Content;
    }

}
