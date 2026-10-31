using System.Text.RegularExpressions;
using Nodalis.Infrastructure.Persistence;

namespace Nodalis.Infrastructure.Projects;

/// <summary>
/// Read-only impact analysis for references around a Complex project migration.
/// Nothing in the workspace is rewritten: affected and ambiguous references
/// remain blockers until a multi-file, recoverable rewrite is available.
/// </summary>
public sealed class ComplexProjectMigrationLinkImpactService
{
    private static readonly Regex MarkdownLinkPattern = new(
        @"!?\[[^\]\r\n]*\]\(\s*(?<target><[^>\r\n]*>|[^\s\)\r\n]+)(?:\s+(?:""[^""]*""|\x27[^\x27]*\x27))?\s*\)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex HtmlLinkPattern = new(
        @"\b(?:src|href)\s*=\s*(?:""(?<target>[^""]*)""|'(?<target>[^']*)')",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex ReferenceDefinitionPattern = new(
        @"^\s{0,3}\[[^\]]+\]:\s*\S+",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex ReferenceUsagePattern = new(
        @"\]\[[^\]\r\n]+\]",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex HtmlAttributePattern = new(
        @"\b(?:src|href)\s*=",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private readonly string _workspaceRoot;
    private readonly string _projectRoot;
    private readonly IReadOnlyList<ComplexMigrationMove> _moves;

    /// <summary>Creates a deterministic, non-mutating migration link auditor.</summary>
    /// <param name="workspaceRoot">Workspace containing active Markdown.</param>
    /// <param name="projectRoot">Source Complex project directory.</param>
    /// <param name="moves">Read-only candidate moves from the approved blueprint.</param>
    public ComplexProjectMigrationLinkImpactService(
        string workspaceRoot,
        string projectRoot,
        IReadOnlyList<ComplexMigrationMove> moves)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(projectRoot);
        ArgumentNullException.ThrowIfNull(moves);
        _workspaceRoot = Path.GetFullPath(workspaceRoot);
        _projectRoot = Path.GetFullPath(projectRoot);
        _moves = moves.Where(move =>
            move.State == ComplexMigrationMoveState.Candidate).ToArray();
    }

    /// <summary>
    /// Finds precisely affected supported path references, and conservatively
    /// reports link syntaxes whose target cannot be proven stable.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Human-readable source/line diagnostics; no workspace writes.</returns>
    public async Task<IReadOnlyList<string>> FindBlockingReferencesAsync(
        CancellationToken cancellationToken = default)
    {
        List<string> blockers = [];
        foreach (string file in ComplexProjectMigrationPlanService.EnumerateTree(
                     _workspaceRoot, cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();
            FileAttributes attributes = File.GetAttributes(file);
            if ((attributes & FileAttributes.ReparsePoint) != 0)
            {
                throw new InvalidDataException(
                    "Une jonction du workspace empêche de qualifier tous les backlinks.");
            }

            if ((attributes & FileAttributes.Directory) != 0 ||
                !string.Equals(Path.GetExtension(file), ".md",
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string relative = Path.GetRelativePath(_workspaceRoot, file)
                .Replace('\\', '/');
            string firstPart = relative.Split('/')[0];
            if (string.Equals(firstPart, WorkspaceLayout.TemplatesDirectoryName,
                    StringComparison.OrdinalIgnoreCase) ||
                string.Equals(firstPart, WorkspaceLayout.ImportsDirectoryName,
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            bool inFence = false;
            char fenceChar = '\0';
            int fenceLength = 0;
            int lineNumber = 0;
            await using FileStream input = new(file, FileMode.Open,
                FileAccess.Read, FileShare.Read, 65536, useAsync: true);
            using StreamReader reader = new(input);
            while (await reader.ReadLineAsync(cancellationToken) is string line)
            {
                cancellationToken.ThrowIfCancellationRequested();
                lineNumber++;
                string trimmed = line.TrimStart();
                if (trimmed.Length >= 3 && (trimmed[0] == '`' ||
                    trimmed[0] == '~'))
                {
                    int runLength = 0;
                    while (runLength < trimmed.Length &&
                        trimmed[runLength] == trimmed[0])
                    {
                        runLength++;
                    }

                    if (runLength >= 3)
                    {
                        if (!inFence)
                        {
                            inFence = true;
                            fenceChar = trimmed[0];
                            fenceLength = runLength;
                            continue;
                        }

                        if (trimmed[0] == fenceChar && runLength >= fenceLength)
                        {
                            inFence = false;
                            continue;
                        }
                    }
                }

                if (inFence)
                {
                    continue;
                }

                string context = relative + ":" + lineNumber;
                if (line.Contains("[[", StringComparison.Ordinal) ||
                    ReferenceDefinitionPattern.IsMatch(line) ||
                    ReferenceUsagePattern.IsMatch(line))
                {
                    blockers.Add(context + " : référence wiki/indirecte non qualifiée.");
                    continue;
                }

                MatchCollection markdown = MarkdownLinkPattern.Matches(line);
                MatchCollection html = HtmlLinkPattern.Matches(line);
                int directTokens = Regex.Matches(line, @"\]\(").Count;
                int htmlTokens = HtmlAttributePattern.Matches(line).Count;
                if (markdown.Count != directTokens || html.Count != htmlTokens)
                {
                    blockers.Add(context + " : syntaxe de lien ambiguë.");
                    continue;
                }

                foreach (Match match in markdown.Cast<Match>().Concat(html.Cast<Match>()))
                {
                    string? reason = CheckTarget(file, match.Groups["target"].Value);
                    if (reason is not null)
                    {
                        blockers.Add(context + " : " + reason);
                    }
                }
            }
        }

        return blockers.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    /// <summary>
    /// A literal relative reference is safe only when it still resolves to
    /// the mapped target at the mapped document position after all moves.
    /// </summary>
    /// <param name="source">Absolute source document path.</param>
    /// <param name="rawTarget">As-authored Markdown or HTML target.</param>
    /// <returns>Reason for a blocked link, or null for an unchanged target.</returns>
    private string? CheckTarget(string source, string rawTarget)
    {
        string target = rawTarget.Trim();
        if (target.StartsWith('<') && target.EndsWith('>'))
        {
            target = target[1..^1];
        }

        if (target.Length == 0 || target.StartsWith('#'))
        {
            return null;
        }

        if (target.StartsWith("//", StringComparison.Ordinal))
        {
            return null;
        }

        if (Uri.TryCreate(target, UriKind.Absolute, out Uri? uri))
        {
            return uri.Scheme is "http" or "https" or "mailto" or "data"
                ? null
                : "schéma de lien non qualifié : " + target;
        }

        if (target.StartsWith('/') || target.StartsWith('\\') ||
            target.Contains('%') || target.Contains('?') ||
            target.Contains('\\') || target.Contains(':'))
        {
            return "cible relative ou encodée non qualifiée : " + target;
        }

        string logicalTarget = target.Split('#', 2)[0];
        if (logicalTarget.Length == 0)
        {
            return null;
        }

        string resolved;
        try
        {
            resolved = Path.GetFullPath(Path.Combine(
                Path.GetDirectoryName(source)!,
                logicalTarget.Replace('/', Path.DirectorySeparatorChar)));
        }
        catch (Exception exception) when (exception is ArgumentException or
            NotSupportedException or PathTooLongException)
        {
            return "chemin de lien non résoluble : " + target;
        }

        string mappedSource = MapPath(source);
        string mappedTarget = MapPath(resolved);
        string postMoveResolution = Path.GetFullPath(Path.Combine(
            Path.GetDirectoryName(mappedSource)!,
            logicalTarget.Replace('/', Path.DirectorySeparatorChar)));

        return string.Equals(postMoveResolution, mappedTarget,
            StringComparison.OrdinalIgnoreCase)
            ? null
            : "lien relatif affecté : " + target;
    }

    /// <summary>Maps a path through exactly the approved candidate moves.</summary>
    /// <param name="sourcePath">Path before migration.</param>
    /// <returns>Corresponding absolute path after migration.</returns>
    private string MapPath(string sourcePath)
    {
        foreach (ComplexMigrationMove move in _moves)
        {
            string from = Path.GetFullPath(Path.Combine(_projectRoot,
                move.From.Replace('/', Path.DirectorySeparatorChar)));
            string to = Path.GetFullPath(Path.Combine(_projectRoot,
                move.To.Replace('/', Path.DirectorySeparatorChar)));
            if (string.Equals(sourcePath, from,
                StringComparison.OrdinalIgnoreCase))
            {
                return to;
            }

            if (sourcePath.StartsWith(from + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase))
            {
                return to + sourcePath[from.Length..];
            }
        }

        return sourcePath;
    }
}
