namespace Nodalis.Core.Reliability;

/// <summary>
/// Identifies whether an integrity finding blocks a coherent workspace or merely deserves attention.
/// </summary>
public enum WorkspaceIntegritySeverity
{
    Warning,
    Error
}

/// <summary>
/// Describes one deterministic workspace integrity finding.
/// </summary>
public sealed record WorkspaceIntegrityIssue
{
    /// <summary>Gets the severity of the finding.</summary>
    public required WorkspaceIntegritySeverity Severity { get; init; }

    /// <summary>Gets the stable machine-readable diagnostic code.</summary>
    public required string Code { get; init; }

    /// <summary>Gets the human-readable diagnostic message.</summary>
    public required string Message { get; init; }

    /// <summary>Gets the workspace-relative path related to the finding, when applicable.</summary>
    public string? RelativePath { get; init; }

    /// <summary>Gets the stable entity identifier related to the finding, when applicable.</summary>
    public Guid? EntityId { get; init; }

    /// <summary>Gets a localized severity label suitable for the UI.</summary>
    public string SeverityLabel =>
        Severity == WorkspaceIntegritySeverity.Error
            ? "ERREUR"
            : "AVERTISSEMENT";

    /// <summary>Gets a readable location label suitable for the UI.</summary>
    public string LocationLabel =>
        string.IsNullOrWhiteSpace(RelativePath)
            ? "Workspace"
            : RelativePath;
}

/// <summary>
/// Contains the immutable result of one workspace integrity scan.
/// </summary>
public sealed record WorkspaceIntegrityReport
{
    /// <summary>Gets when the report was generated.</summary>
    public DateTimeOffset GeneratedUtc { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>Gets the findings ordered for presentation.</summary>
    public List<WorkspaceIntegrityIssue> Issues { get; init; } = [];

    /// <summary>Gets the number of blocking integrity errors.</summary>
    public int ErrorCount =>
        Issues.Count(issue => issue.Severity == WorkspaceIntegritySeverity.Error);

    /// <summary>Gets the number of non-blocking warnings.</summary>
    public int WarningCount =>
        Issues.Count(issue => issue.Severity == WorkspaceIntegritySeverity.Warning);

    /// <summary>Gets whether the scan found no blocking error.</summary>
    public bool IsStructurallyHealthy => ErrorCount == 0;
}
