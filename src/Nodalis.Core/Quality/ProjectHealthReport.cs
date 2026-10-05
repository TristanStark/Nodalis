namespace Nodalis.Core.Quality;

/// <summary>
/// Identifies the actionability level of a deterministic project health finding.
/// </summary>
public enum ProjectHealthSeverity
{
    Warning,
    Error
}

/// <summary>
/// Configures deterministic project health rules without requiring network access.
/// </summary>
public sealed record ProjectHealthOptions
{
    /// <summary>
    /// Gets the maximum age, in days, for a meeting to be considered recent.
    /// </summary>
    public int MeetingRecencyDays { get; init; } = 30;

    /// <summary>
    /// Gets the minimum amount of meaningful test content expected before the Tests section is considered populated.
    /// </summary>
    public int MinimumTestContentCharacters { get; init; } = 80;
}

/// <summary>
/// Describes one deterministic project health finding.
/// </summary>
public sealed record ProjectHealthIssue
{
    /// <summary>
    /// Gets the severity of the finding.
    /// </summary>
    public required ProjectHealthSeverity Severity { get; init; }

    /// <summary>
    /// Gets the stable machine-readable rule code.
    /// </summary>
    public required string Code { get; init; }

    /// <summary>
    /// Gets the human-readable explanation of the finding.
    /// </summary>
    public required string Message { get; init; }

    /// <summary>
    /// Gets the workspace-relative path related to the finding, when available.
    /// </summary>
    public string? RelativePath { get; init; }

    /// <summary>
    /// Gets the one-based source line related to the finding, when available.
    /// </summary>
    public int? LineNumber { get; init; }

    /// <summary>
    /// Gets the localized severity label used by the desktop UI.
    /// </summary>
    public string SeverityLabel =>
        Severity == ProjectHealthSeverity.Error
            ? "ERREUR"
            : "AVERTISSEMENT";

    /// <summary>
    /// Gets a readable source location for the finding.
    /// </summary>
    public string LocationLabel =>
        string.IsNullOrWhiteSpace(RelativePath)
            ? "Projet"
            : LineNumber is int lineNumber
                ? RelativePath + " · ligne " + lineNumber
                : RelativePath;

    /// <summary>
    /// Gets whether the finding can be opened directly in the Markdown editor.
    /// </summary>
    public bool IsNavigable =>
        !string.IsNullOrWhiteSpace(RelativePath) &&
        RelativePath.EndsWith(
            ".md",
            StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// Contains the immutable result of one deterministic project health scan.
/// </summary>
public sealed record ProjectHealthReport
{
    /// <summary>
    /// Gets the analyzed project identifier.
    /// </summary>
    public required Guid ProjectId { get; init; }

    /// <summary>
    /// Gets the analyzed project name.
    /// </summary>
    public required string ProjectName { get; init; }

    /// <summary>
    /// Gets when the report was generated.
    /// </summary>
    public DateTimeOffset GeneratedUtc { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Gets the findings ordered for presentation.
    /// </summary>
    public List<ProjectHealthIssue> Issues { get; init; } = [];

    /// <summary>
    /// Gets the number of error-level findings.
    /// </summary>
    public int ErrorCount =>
        Issues.Count(issue => issue.Severity == ProjectHealthSeverity.Error);

    /// <summary>
    /// Gets the number of warning-level findings.
    /// </summary>
    public int WarningCount =>
        Issues.Count(issue => issue.Severity == ProjectHealthSeverity.Warning);

    /// <summary>
    /// Gets whether no error-level finding was detected.
    /// </summary>
    public bool IsHealthy => ErrorCount == 0;
}
