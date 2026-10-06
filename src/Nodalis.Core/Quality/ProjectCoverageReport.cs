namespace Nodalis.Core.Quality;

/// <summary>
/// Identifies the severity of a deterministic coverage or coherence finding.
/// </summary>
public enum ProjectCoverageSeverity
{
    Information,
    Warning,
    Error
}

/// <summary>
/// Identifies one source used to derive a coverage finding.
/// </summary>
public sealed record ProjectCoverageSource
{
    /// <summary>Gets the workspace-relative source path.</summary>
    public required string RelativePath { get; init; }

    /// <summary>Gets the optional one-based source line.</summary>
    public int? LineNumber { get; init; }

    /// <summary>Gets a readable location label.</summary>
    public string LocationLabel =>
        LineNumber is int lineNumber
            ? RelativePath + " · ligne " + lineNumber
            : RelativePath;
}

/// <summary>
/// Describes one explainable deterministic project coverage finding.
/// </summary>
public sealed record ProjectCoverageFinding
{
    /// <summary>Gets the stable machine-readable rule code.</summary>
    public required string Code { get; init; }

    /// <summary>Gets the finding severity.</summary>
    public required ProjectCoverageSeverity Severity { get; init; }

    /// <summary>Gets the human-readable finding.</summary>
    public required string Message { get; init; }

    /// <summary>Gets the explicit rule explanation.</summary>
    public required string RuleExplanation { get; init; }

    /// <summary>Gets every source used by the rule.</summary>
    public IReadOnlyList<ProjectCoverageSource> Sources { get; init; } = [];

    /// <summary>Gets the localized severity label.</summary>
    public string SeverityLabel =>
        Severity switch
        {
            ProjectCoverageSeverity.Error => "ERREUR",
            ProjectCoverageSeverity.Warning => "AVERTISSEMENT",
            _ => "INFORMATION"
        };

    /// <summary>Gets a compact source list for the desktop UI.</summary>
    public string SourcesLabel =>
        Sources.Count == 0
            ? "Projet"
            : string.Join(
                " · ",
                Sources.Select(source =>
                    source.LocationLabel));

    /// <summary>Gets whether the first source can be opened in the Markdown editor.</summary>
    public bool IsNavigable =>
        Sources.Count > 0 &&
        Sources[0].RelativePath.EndsWith(
            ".md",
            StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// Contains the read-only result of a deterministic coverage analysis.
/// </summary>
public sealed record ProjectCoverageReport
{
    /// <summary>Gets the analyzed project identifier.</summary>
    public required Guid ProjectId { get; init; }

    /// <summary>Gets the analyzed project name.</summary>
    public required string ProjectName { get; init; }

    /// <summary>Gets the ordered findings.</summary>
    public IReadOnlyList<ProjectCoverageFinding> Findings { get; init; } = [];

    /// <summary>Gets the number of error findings.</summary>
    public int ErrorCount =>
        Findings.Count(finding =>
            finding.Severity == ProjectCoverageSeverity.Error);

    /// <summary>Gets the number of warning findings.</summary>
    public int WarningCount =>
        Findings.Count(finding =>
            finding.Severity == ProjectCoverageSeverity.Warning);
}
