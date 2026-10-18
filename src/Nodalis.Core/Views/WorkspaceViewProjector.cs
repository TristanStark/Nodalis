using System.Globalization;
using Nodalis.Core.Decisions;
using Nodalis.Core.Links;
using Nodalis.Core.Maintenance;
using Nodalis.Core.Markdown;
using Nodalis.Core.Milestones;
using Nodalis.Core.Tasks;

namespace Nodalis.Core.Views;

/// <summary>Canonical source types supported by cross-workspace dynamic views.</summary>
public enum WorkspaceViewItemKind
{
    Document, Task, Milestone, Decision, Incident, Intervention
}

/// <summary>Discriminant for a typed, read-only projected field.</summary>
public enum WorkspaceViewFieldKind
{
    Text, Date, Boolean, Number
}

/// <summary>
/// One immutable typed value; absent fields are represented by a missing key,
/// never by a fabricated date, owner, status or priority.
/// </summary>
public sealed record WorkspaceViewField
{
    /// <summary>Gets the stored type.</summary>
    public required WorkspaceViewFieldKind Kind { get; init; }

    /// <summary>Gets the human-readable text if this field contains text.</summary>
    public string? Text { get; init; }

    /// <summary>Gets a date without locale-dependent conversion.</summary>
    public DateOnly? Date { get; init; }

    /// <summary>Gets the Boolean value if one exists.</summary>
    public bool? Boolean { get; init; }

    /// <summary>Gets the numeric value if one exists.</summary>
    public decimal? Number { get; init; }

    /// <summary>Creates one text value from nonempty canonical text.</summary>
    /// <param name="value">Value to project.</param>
    /// <returns>A typed text field.</returns>
    public static WorkspaceViewField OfText(string value) =>
        new() { Kind = WorkspaceViewFieldKind.Text, Text = value };

    /// <summary>Creates one date field, preserving the exact calendar date.</summary>
    /// <param name="value">Original date.</param>
    /// <returns>A typed calendar-date field.</returns>
    public static WorkspaceViewField OfDate(DateOnly value) =>
        new() { Kind = WorkspaceViewFieldKind.Date, Date = value };

    /// <summary>Creates an explicit Boolean field.</summary>
    /// <param name="value">Original Boolean.</param>
    /// <returns>A typed Boolean field.</returns>
    public static WorkspaceViewField OfBoolean(bool value) =>
        new() { Kind = WorkspaceViewFieldKind.Boolean, Boolean = value };
}

/// <summary>Direct navigation back to a canonical Markdown source.</summary>
public sealed record WorkspaceViewSource(
    Guid DocumentId,
    string RelativePath,
    int? LineNumber,
    string? RecordId);

/// <summary>
/// Read-only typed item; no properties or result snapshot is ever written back to
/// a note, task, decision or maintenance record.
/// </summary>
public sealed record WorkspaceViewItem
{
    /// <summary>Gets the deterministic identity of this projection.</summary>
    public required string Key { get; init; }

    /// <summary>Gets the item's canonical family.</summary>
    public required WorkspaceViewItemKind Kind { get; init; }

    /// <summary>Gets its display name.</summary>
    public required string Title { get; init; }

    /// <summary>Gets the stable context identity inherited from the existing index.</summary>
    public required string ScopeIdentity { get; init; }

    /// <summary>Gets the reference used to navigate to the existing source.</summary>
    public required WorkspaceViewSource Source { get; init; }

    /// <summary>Gets typed fields; missing keys mean unknown/not supplied.</summary>
    public required IReadOnlyDictionary<string, WorkspaceViewField> Fields { get; init; }

    /// <summary>Gets normalized explicit tags.</summary>
    public IReadOnlyList<string> Tags { get; init; } = [];
}

/// <summary>Unprojectable sources are surfaced rather than silently converted to fake items.</summary>
public sealed record WorkspaceViewProjectionDiagnostic(string Source, string Reason);

/// <summary>A deterministically ordered collection of typed read-only projections.</summary>
public sealed record WorkspaceViewProjection(
    IReadOnlyList<WorkspaceViewItem> Items,
    IReadOnlyList<WorkspaceViewProjectionDiagnostic> Diagnostics);

/// <summary>
/// Converts existing canonical projections into a shared read model without
/// scanning the filesystem or creating another source of business truth.
/// </summary>
public static class WorkspaceViewProjector
{
    /// <summary>
    /// Builds a cross-project read model from in-memory projections already used
    /// by Nodalis 1.0–1.4. It ignores or reports items whose source GUID is no
    /// longer active, rather than redirecting to a document with a similar name.
    /// </summary>
    /// <param name="catalog">Stable identity registry and active documents.</param>
    /// <param name="tasks">Existing indexed Markdown tasks.</param>
    /// <param name="milestones">Existing indexed milestones.</param>
    /// <param name="decisions">Existing Decision Records.</param>
    /// <param name="maintenance">Already parsed maintenance incidents/interventions.</param>
    /// <param name="frontMatter">Optional previously parsed Markdown front matter by document GUID.</param>
    /// <param name="workspaceRoot">Workspace root for safely resolving absolute maintenance source paths.</param>
    /// <param name="cancellationToken">Cancellation request.</param>
    /// <returns>Deterministic read-only items with explicit diagnostics.</returns>
    public static WorkspaceViewProjection Build(
        LinkIndexCatalog catalog,
        IEnumerable<TaskItem>? tasks = null,
        IEnumerable<MilestoneItem>? milestones = null,
        IEnumerable<DecisionRecord>? decisions = null,
        IEnumerable<MaintenanceHistoryItem>? maintenance = null,
        IReadOnlyDictionary<Guid, MarkdownFrontMatterDocument>? frontMatter = null,
        string? workspaceRoot = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        Dictionary<Guid, LinkTargetEntry> documents = catalog.Targets
            .Where(item => item.Kind == LinkTargetKind.Document && item.Id != Guid.Empty)
            .GroupBy(item => item.Id)
            .ToDictionary(item => item.Key, item => item.First());
        Dictionary<string, LinkTargetEntry> byPath = documents.Values
            .GroupBy(item => NormalizePath(item.RelativePath), StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() == 1)
            .ToDictionary(group => group.Key, group => group.First(),
                StringComparer.OrdinalIgnoreCase);
        Dictionary<string, WorkspaceViewItem> rows = new(StringComparer.OrdinalIgnoreCase);
        List<WorkspaceViewProjectionDiagnostic> diagnostics = [];

        foreach (LinkTargetEntry document in documents.Values)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Dictionary<string, WorkspaceViewField> fields = [];
            IReadOnlyList<string> tags = [];
            if (frontMatter?.TryGetValue(document.Id, out MarkdownFrontMatterDocument? parsed) == true)
            {
                foreach (KeyValuePair<string, string> entry in parsed.Properties)
                {
                    AddText(fields, entry.Key, entry.Value);
                }

                if (parsed.Properties.TryGetValue("date", out string? dateText) &&
                    DateOnly.TryParseExact(dateText, "yyyy-MM-dd",
                        CultureInfo.InvariantCulture, DateTimeStyles.None,
                        out DateOnly date))
                {
                    fields["date"] = WorkspaceViewField.OfDate(date);
                }

                if (parsed.Properties.TryGetValue("tags", out string? rawTags))
                {
                    tags = ParseTags(rawTags);
                }
            }

            AddRow(rows, diagnostics, new WorkspaceViewItem
            {
                Key = "document:" + document.Id.ToString("N"),
                Kind = WorkspaceViewItemKind.Document,
                Title = document.DisplayName,
                ScopeIdentity = document.ScopeIdentity ?? "unknown",
                Source = Source(document, null, null),
                Fields = fields,
                Tags = tags
            });
        }

        foreach (TaskItem task in tasks ?? [])
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!documents.TryGetValue(task.SourceDocumentId, out LinkTargetEntry? document))
            {
                diagnostics.Add(new WorkspaceViewProjectionDiagnostic(
                    task.SourceRelativePath, "Tâche ignorée : GUID de document source introuvable."));
                continue;
            }

            Dictionary<string, WorkspaceViewField> fields = [];
            fields["completed"] = WorkspaceViewField.OfBoolean(task.IsCompleted);
            AddText(fields, "status", task.Status);
            AddText(fields, "priority", task.Priority);
            AddText(fields, "owner", task.Owner);
            AddText(fields, "application", task.ApplicationName);
            AddText(fields, "project", task.ProjectName);
            if (task.DueDate is DateOnly due)
            {
                fields["date"] = WorkspaceViewField.OfDate(due);
            }

            AddRow(rows, diagnostics, new WorkspaceViewItem
            {
                Key = "task:" + task.Id.ToString("N"),
                Kind = WorkspaceViewItemKind.Task,
                Title = task.Text,
                ScopeIdentity = document.ScopeIdentity ?? "unknown",
                Source = Source(document, task.LineNumber, task.Id.ToString("N")),
                Fields = fields,
                Tags = task.Tags.ToArray()
            });
        }

        foreach (MilestoneItem milestone in milestones ?? [])
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!byPath.TryGetValue(NormalizePath(milestone.SourceRelativePath),
                    out LinkTargetEntry? document))
            {
                diagnostics.Add(new WorkspaceViewProjectionDiagnostic(
                    milestone.SourceRelativePath, "Jalon ignoré : document source introuvable ou ambigu."));
                continue;
            }

            Dictionary<string, WorkspaceViewField> fields = [];
            AddText(fields, "status", milestone.Status);
            AddText(fields, "project", milestone.ProjectName);
            AddText(fields, "description", milestone.Description);
            if (milestone.TargetDate is DateOnly date)
            {
                fields["date"] = WorkspaceViewField.OfDate(date);
            }

            AddRow(rows, diagnostics, new WorkspaceViewItem
            {
                Key = "milestone:" + milestone.Id.ToString("N"),
                Kind = WorkspaceViewItemKind.Milestone,
                Title = milestone.Name,
                ScopeIdentity = document.ScopeIdentity ?? "unknown",
                Source = Source(document, milestone.LineNumber, milestone.Id.ToString("N")),
                Fields = fields
            });
        }

        foreach (DecisionRecord decision in decisions ?? [])
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!byPath.TryGetValue(NormalizePath(decision.SourceRelativePath),
                    out LinkTargetEntry? document))
            {
                diagnostics.Add(new WorkspaceViewProjectionDiagnostic(
                    decision.SourceRelativePath, "Décision ignorée : document source introuvable ou ambigu."));
                continue;
            }

            Dictionary<string, WorkspaceViewField> fields = [];
            AddText(fields, "status", decision.Status);
            AddText(fields, "scope", decision.ScopeName);
            AddText(fields, "lifecycle", decision.LifecycleState.ToString());
            if (decision.Date is DateOnly date)
            {
                fields["date"] = WorkspaceViewField.OfDate(date);
            }

            AddRow(rows, diagnostics, new WorkspaceViewItem
            {
                Key = "decision:" + document.Id.ToString("N"),
                Kind = WorkspaceViewItemKind.Decision,
                Title = decision.Title,
                ScopeIdentity = document.ScopeIdentity ?? "unknown",
                Source = Source(document, null, null),
                Fields = fields
            });
        }

        foreach (MaintenanceHistoryItem record in maintenance ?? [])
        {
            cancellationToken.ThrowIfCancellationRequested();
            string? maintenancePath = ResolveSourcePath(record.SourcePath, workspaceRoot);
            if (string.IsNullOrWhiteSpace(record.Id) ||
                maintenancePath is null ||
                !byPath.TryGetValue(maintenancePath, out LinkTargetEntry? document))
            {
                diagnostics.Add(new WorkspaceViewProjectionDiagnostic(
                    record.SourcePath, "Fiche ignorée : identifiant manquant ou document source introuvable."));
                continue;
            }

            Dictionary<string, WorkspaceViewField> fields = [];
            AddText(fields, "status", record.Status);
            AddText(fields, "category", record.Category);
            AddText(fields, "project", record.ProjectName);
            AddText(fields, "incident", record.IncidentId);
            fields["incomplete"] = WorkspaceViewField.OfBoolean(record.IsIncomplete);
            if (record.Date is DateOnly date)
            {
                fields["date"] = WorkspaceViewField.OfDate(date);
            }

            AddRow(rows, diagnostics, new WorkspaceViewItem
            {
                Key = (record.Kind == MaintenanceRecordKind.Incident ? "incident:" : "intervention:") +
                    document.Id.ToString("N") + ":" + record.Id,
                Kind = record.Kind == MaintenanceRecordKind.Incident
                    ? WorkspaceViewItemKind.Incident : WorkspaceViewItemKind.Intervention,
                Title = record.Summary,
                ScopeIdentity = document.ScopeIdentity ?? "unknown",
                Source = Source(document, record.LineNumber, record.Id),
                Fields = fields,
                Tags = ParseTags(record.Tags)
            });
        }

        return new WorkspaceViewProjection(
            rows.Values.OrderBy(item => item.Kind)
                .ThenBy(item => item.ScopeIdentity, StringComparer.OrdinalIgnoreCase)
                .ThenBy(item => item.Title, StringComparer.OrdinalIgnoreCase)
                .ThenBy(item => item.Key, StringComparer.Ordinal).ToArray(),
            diagnostics.ToArray());
    }

    /// <summary>Uses stable source GUID and original Markdown position.</summary>
    /// <param name="document">Canonical document.</param>
    /// <param name="line">Optional line number.</param>
    /// <param name="recordId">Optional native record identifier.</param>
    /// <returns>Auditable navigation destination.</returns>
    private static WorkspaceViewSource Source(
        LinkTargetEntry document, int? line, string? recordId) =>
        new(document.Id, document.RelativePath, line, recordId);

    /// <summary>Normalizes Windows/Unix relative path separators for source lookup.</summary>
    /// <param name="path">Canonical document path.</param>
    /// <returns>Comparable path key.</returns>
    private static string NormalizePath(string path) =>
        path.Replace('\\', '/').TrimStart('/');

    /// <summary>
    /// Converts a maintenance file path to a canonical workspace-relative path
    /// without following paths outside the workspace. No disk access occurs.
    /// </summary>
    /// <param name="sourcePath">Source absolute or relative path.</param>
    /// <param name="workspaceRoot">Root required for an absolute source path.</param>
    /// <returns>Normalized relative path, or null if outside/ambiguous.</returns>
    private static string? ResolveSourcePath(string sourcePath, string? workspaceRoot)
    {
        if (string.IsNullOrWhiteSpace(sourcePath))
        {
            return null;
        }

        string relative = sourcePath;
        if (Path.IsPathRooted(sourcePath))
        {
            if (string.IsNullOrWhiteSpace(workspaceRoot))
            {
                return null;
            }

            relative = Path.GetRelativePath(
                Path.GetFullPath(workspaceRoot), Path.GetFullPath(sourcePath));
        }

        string normalized = NormalizePath(relative);
        if (normalized.Length == 0 ||
            normalized.Split('/').Any(segment => segment == ".." || segment == ".") ||
            Path.IsPathRooted(normalized))
        {
            return null;
        }

        return normalized;
    }

    /// <summary>Stores one optional nonempty text field.</summary>
    /// <param name="fields">Mutable projection under construction.</param>
    /// <param name="name">Field name.</param>
    /// <param name="value">Existing business value.</param>
    private static void AddText(
        IDictionary<string, WorkspaceViewField> fields, string name, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            fields[name] = WorkspaceViewField.OfText(value.Trim());
        }
    }

    /// <summary>Splits existing labels without assuming tags for absent metadata.</summary>
    /// <param name="raw">Raw comma/semicolon-delimited tags.</param>
    /// <returns>Normalized, distinct tag names.</returns>
    private static IReadOnlyList<string> ParseTags(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return [];
        }

        return raw.Trim().Trim('[', ']').Split([',', ';'],
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(tag => tag.Trim(' ', '\'', '"', '#'))
            .Where(tag => tag.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    /// <summary>Rejects duplicate projection identities rather than hiding ambiguity.</summary>
    /// <param name="rows">Projected rows by identity.</param>
    /// <param name="diagnostics">Rejection diagnostics.</param>
    /// <param name="item">New projection.</param>
    private static void AddRow(
        IDictionary<string, WorkspaceViewItem> rows,
        ICollection<WorkspaceViewProjectionDiagnostic> diagnostics,
        WorkspaceViewItem item)
    {
        if (!rows.TryAdd(item.Key, item))
        {
            diagnostics.Add(new WorkspaceViewProjectionDiagnostic(
                item.Source.RelativePath, "Identifiant de projection répété : " + item.Key));
        }
    }
}
