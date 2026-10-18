namespace Nodalis.Core.Views;

/// <summary>Where a saved view definition is stored, never where its results live.</summary>
public enum WorkspaceViewVisibility
{
    Workspace, Private
}

/// <summary>One supported visual arrangement of the same read-only query results.</summary>
public enum WorkspaceViewLayout
{
    List, Table, Cards
}

/// <summary>
/// A portable, user-authored query definition. It deliberately contains no
/// projected documents, task snapshots, vectors or cached query results.
/// </summary>
public sealed record WorkspaceViewDefinition
{
    public required Guid Id { get; init; }
    public required string Name { get; init; }
    public required WorkspaceViewQuery Query { get; init; }
    public WorkspaceViewLayout Layout { get; init; } = WorkspaceViewLayout.List;
    public IReadOnlyList<string> Columns { get; init; } = ["title", "kind", "date", "status"];
    public bool Pinned { get; init; }
    public int Order { get; init; }
}

/// <summary>Versioned persisted document containing only view definitions.</summary>
public sealed record WorkspaceViewDefinitionsDocument
{
    public const int CurrentSchemaVersion = 1;
    public int SchemaVersion { get; init; } = CurrentSchemaVersion;
    public IReadOnlyList<WorkspaceViewDefinition> Views { get; init; } = [];
}

/// <summary>Optimistic concurrency snapshot returned by the JSON definition store.</summary>
public sealed record WorkspaceViewDefinitionSnapshot(
    WorkspaceViewVisibility Visibility,
    IReadOnlyList<WorkspaceViewDefinition> Views,
    string Revision,
    bool IsReadOnly);

/// <summary>Concurrent external edit detected before overwriting saved definitions.</summary>
public sealed class WorkspaceViewDefinitionConflictException : IOException
{
    /// <summary>Creates an actionable conflict without altering source files.</summary>
    /// <param name="message">Reason the write must be retried after reloading.</param>
    public WorkspaceViewDefinitionConflictException(string message) : base(message)
    {
    }
}
