using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Nodalis.Infrastructure.Persistence;

namespace Nodalis.Infrastructure.Projects;

/// <summary>One byte-protected literal link edit within an existing project document.</summary>
/// <param name="ProjectRelativePath">Original path relative to the project root.</param>
/// <param name="WorkspaceRelativePath">Display path relative to the workspace.</param>
/// <param name="Line">One-based diagnostic line.</param>
/// <param name="Offset">UTF-16 character offset in the original document.</param>
/// <param name="OriginalTarget">Exact captured target including angle brackets if present.</param>
/// <param name="ReplacementTarget">Target after the proposed folder moves.</param>
/// <param name="SourceHash">SHA-256 of original Markdown bytes.</param>
public sealed record ComplexMigrationLinkRewrite(
    string ProjectRelativePath,
    string WorkspaceRelativePath,
    int Line,
    int Offset,
    string OriginalTarget,
    string ReplacementTarget,
    string SourceHash);

/// <summary>Read-only plan for provably reversible local edits and other blockers.</summary>
/// <param name="Rewrites">Local edits that can be applied to staging only.</param>
/// <param name="Blockers">Unsupported syntax and affected references outside the project.</param>
public sealed record ComplexMigrationLinkAudit(
    IReadOnlyList<ComplexMigrationLinkRewrite> Rewrites,
    IReadOnlyList<string> Blockers)
{
    /// <summary>Exact path rewrites in documents outside the migrated project.</summary>
    public IReadOnlyList<ComplexMigrationLinkRewrite> ExternalRewrites { get; init; } = [];

    /// <summary>Number of target-project Markdown documents audited strictly.</summary>
    public int ProjectDocumentsInspected { get; init; }

    /// <summary>External Markdown documents parsed for potential backlinks.</summary>
    public int ExternalDocumentsInspected { get; init; }

    /// <summary>External Markdown documents omitted after safe scope filtering.</summary>
    public int ExternalDocumentsSkipped { get; init; }
}

/// <summary>
/// Simulates project moves on both link sources and destinations. Affected
/// internal and external plain relative targets are proposed for exact
/// staging edits; unsupported syntax fails closed. Never writes a file.
/// </summary>
public sealed class ComplexProjectMigrationLinkImpactService
{
    // A pathological Markdown line cannot monopolize a worker indefinitely.
    // A timeout becomes a conservative, project-scoped blocking diagnostic.
    private static readonly TimeSpan LinkRegexTimeout =
        TimeSpan.FromMilliseconds(500);

    private static readonly Regex MarkdownLinkPattern = new(
        @"!?\[[^\]\r\n]*\]\(\s*(?<target><[^>\r\n]*>|[^\s\)\r\n]+)(?:\s+(?:""[^""]*""|\x27[^\x27]*\x27))?\s*\)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant,
        LinkRegexTimeout);

    private static readonly Regex HtmlLinkPattern = new(
        @"\b(?:src|href)\s*=\s*(?:""(?<target>[^""]*)""|'(?<target>[^']*)')",
        RegexOptions.Compiled | RegexOptions.IgnoreCase |
        RegexOptions.CultureInvariant, LinkRegexTimeout);

    private static readonly Regex ReferenceDefinitionPattern = new(
        @"^\uFEFF?\s{0,3}\[[^\]]+\]:\s*\S+",
        RegexOptions.Compiled | RegexOptions.CultureInvariant,
        LinkRegexTimeout);

    // Only simple, explicit reference labels and literal destinations are safe
    // to map without a full Markdown parser. All other forms still fail closed.
    private static readonly Regex SimpleReferenceDefinitionPattern = new(
        @"^\uFEFF?\s{0,3}\[(?<label>[A-Za-z0-9_-]+)\]:[ \t]+(?<target><[^>\r\n]*>|[^\s\r\n]+)(?:[ \t]+(?:""[^""\r\n]*""|'[^'\r\n]*'))?[ \t]*$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant,
        LinkRegexTimeout);

    private static readonly Regex ReferenceUsagePattern = new(
        @"\]\[[^\]\r\n]+\]",
        RegexOptions.Compiled | RegexOptions.CultureInvariant,
        LinkRegexTimeout);

    private static readonly Regex SimpleReferenceUsagePattern = new(
        @"!?\[[^\]\r\n]+\]\[(?<label>[A-Za-z0-9_-]+)\]",
        RegexOptions.Compiled | RegexOptions.CultureInvariant,
        LinkRegexTimeout);

    private static readonly Regex HtmlAttributePattern = new(
        @"\b(?:src|href)\s*=",
        RegexOptions.Compiled | RegexOptions.IgnoreCase |
        RegexOptions.CultureInvariant, LinkRegexTimeout);

    private const int ProgressReportInterval = 128;
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private readonly string _workspaceRoot;
    private readonly string _projectRoot;
    private readonly IReadOnlyList<ComplexMigrationMove> _moves;
    private readonly string _projectName;
    private readonly string _encodedProjectName;
    private readonly IReadOnlyList<string> _possibleMovedPaths;

    /// <summary>Creates a deterministic, non-mutating link auditor.</summary>
    /// <param name="workspaceRoot">Workspace containing active Markdown.</param>
    /// <param name="projectRoot">Source Complex project root.</param>
    /// <param name="moves">Frozen candidate moves.</param>
    public ComplexProjectMigrationLinkImpactService(
        string workspaceRoot,
        string projectRoot,
        IReadOnlyList<ComplexMigrationMove> moves)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(projectRoot);
        ArgumentNullException.ThrowIfNull(moves);
        _workspaceRoot = Path.TrimEndingDirectorySeparator(
            Path.GetFullPath(workspaceRoot));
        _projectRoot = Path.TrimEndingDirectorySeparator(
            Path.GetFullPath(projectRoot));
        _moves = moves.Where(move =>
            move.State == ComplexMigrationMoveState.Candidate).ToArray();
        _projectName = Path.GetFileName(_projectRoot);
        _encodedProjectName = Uri.EscapeDataString(_projectName);
        _possibleMovedPaths = _moves.SelectMany(move =>
        {
            string from = move.From.Replace('\\', '/');
            return new[]
            {
                _projectName + "/" + from,
                _projectName + "/" + Uri.EscapeDataString(from),
                _encodedProjectName + "/" + from,
                _encodedProjectName + "/" + Uri.EscapeDataString(from)
            };
        }).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    /// <summary>
    /// Returns proposed exact replacements as well as unqualified references.
    /// Only UTF-8 Markdown can be rewritten, preserving its original BOM and
    /// newline bytes. An unsupported file is never silently modified.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <param name="progress">Optional aggregate progress; no source names.</param>
    /// <returns>Immutable, read-only link analysis.</returns>
    public async Task<ComplexMigrationLinkAudit> AnalyzeAsync(
        CancellationToken cancellationToken = default,
        IProgress<ComplexMigrationPreflightProgress>? progress = null)
    {
        List<ComplexMigrationLinkRewrite> rewrites = [];
        List<ComplexMigrationLinkRewrite> externalRewrites = [];
        List<string> blockers = [];
        int projectFiles = 0;
        int externalParsed = 0;
        int externalSkipped = 0;
        int processed = 0;
        void ReportProgress()
        {
            progress?.Report(new(ComplexMigrationPreflightPhase.AnalyzeLinks,
                projectFiles, externalParsed, externalSkipped));
        }

        void ReportAfterDocument()
        {
            processed++;
            if (processed % ProgressReportInterval == 0)
            {
                ReportProgress();
            }
        }

        ReportProgress();
        foreach (string file in ComplexProjectMigrationPlanService.EnumerateTree(
                     _workspaceRoot, cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();
            FileAttributes attributes = File.GetAttributes(file);
            // Only the migrated project is copied. Other applications may
            // legitimately contain junctions; never follow or rewrite them.
            if ((attributes & FileAttributes.ReparsePoint) != 0)
            {
                if (IsWithin(_projectRoot, file))
                {
                    throw new InvalidDataException(
                        "Une jonction du projet empêche la migration.");
                }

                continue;
            }

            if ((attributes & FileAttributes.Directory) != 0 ||
                !string.Equals(Path.GetExtension(file), ".md",
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string workspaceRelative = Path.GetRelativePath(_workspaceRoot, file)
                .Replace('\\', '/');
            string firstPart = workspaceRelative.Split('/')[0];
            if (string.Equals(firstPart, WorkspaceLayout.TemplatesDirectoryName,
                    StringComparison.OrdinalIgnoreCase) ||
                string.Equals(firstPart, WorkspaceLayout.ImportsDirectoryName,
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            bool internalDocument = IsWithin(_projectRoot, file);
            // Stream the cheap external scope check with bounded memory.
            // A candidate is reopened fully only when its contents could
            // refer to this project's name. Broken UTF-8 takes the safe
            // fallback path so its raw bytes can still be inspected.
            if (!internalDocument &&
                !await MayNameMovedProjectInFileAsync(file, cancellationToken))
            {
                externalSkipped++;
                ReportAfterDocument();
                continue;
            }

            byte[] bytes = await File.ReadAllBytesAsync(file, cancellationToken);
            string content;
            try
            {
                content = StrictUtf8.GetString(bytes);
            }
            catch (DecoderFallbackException)
            {
                // An unrelated Markdown owned by another application has no
                // bearing on this migration. Only suspected backlinks to a
                // moved destination must stop the operation for review.
                if (internalDocument || MayContainMovedDestination(bytes))
                {
                    blockers.Add(workspaceRelative +
                        " : encodage non UTF-8 d'une référence concernée.");
                }

                ReportAfterDocument();
                continue;
            }

            if (internalDocument)
            {
                projectFiles++;
            }
            else
            {
                externalParsed++;
            }

            string? sourceHash = null;
            string projectRelative = internalDocument
                ? Path.GetRelativePath(_projectRoot, file).Replace('\\', '/')
                : string.Empty;
            Dictionary<string, bool> referenceDefinitions = new(
                StringComparer.OrdinalIgnoreCase);
            List<(string Label, int Line)> referenceUsages = [];
            bool inFence = false;
            char fenceChar = '\0';
            int fenceLength = 0;
            int offset = 0;
            int lineNumber = 0;
            while (offset < content.Length)
            {
                cancellationToken.ThrowIfCancellationRequested();
                int end = content.IndexOf('\n', offset);
                end = end < 0 ? content.Length : end;
                string line = content[offset..end];
                // Preserve offsets against the original content, but exclude
                // the CR belonging to a Windows CRLF line terminator from
                // anchored Markdown definition parsing.
                if (line.EndsWith('\r'))
                {
                    line = line[..^1];
                }

                lineNumber++;
                string trimmed = line.TrimStart();
                if (trimmed.Length >= 3 && (trimmed[0] == (char)96 ||
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
                            offset = end + 1;
                            continue;
                        }

                        if (trimmed[0] == fenceChar && runLength >= fenceLength)
                        {
                            inFence = false;
                            offset = end + 1;
                            continue;
                        }
                    }
                }

                try
                {
                    if (!inFence)
                    {
                        string context = workspaceRelative + ":" + lineNumber;
                    // External Markdown is relevant only when its literal
                    // target may lead to one of this project's moved paths.
                    // The project itself remains subject to strict validation.
                    bool relevant = internalDocument ||
                        MayReferenceMovedDestination(line);
                    Match definition = SimpleReferenceDefinitionPattern.Match(line);
                    MatchCollection usages = ReferenceUsagePattern.Matches(line);
                    MatchCollection simpleUsages =
                        SimpleReferenceUsagePattern.Matches(line);
                    if (line.Contains("[[", StringComparison.Ordinal) ||
                        (ReferenceDefinitionPattern.IsMatch(line) && !definition.Success) ||
                        usages.Count != simpleUsages.Count ||
                        (definition.Success && line.Contains((char)96)))
                    {
                        if (relevant)
                        {
                            blockers.Add(context +
                                " : référence wiki/indirecte non qualifiée.");
                        }
                    }
                    else
                    {
                        if (definition.Success)
                        {
                            string label = definition.Groups["label"].Value;
                            if (referenceDefinitions.TryGetValue(label,
                                out bool previousRelevant))
                            {
                                if (relevant || previousRelevant)
                                {
                                    blockers.Add(context +
                                        " : définition Markdown dupliquée : " + label);
                                }
                            }
                            else
                            {
                                referenceDefinitions.Add(label, relevant);
                            }
                        }
                        else
                        {
                            foreach (Match usage in simpleUsages)
                            {
                                referenceUsages.Add((
                                    usage.Groups["label"].Value, lineNumber));
                            }
                        }

                        MatchCollection markdown = MarkdownLinkPattern.Matches(line);
                        MatchCollection html = HtmlLinkPattern.Matches(line);
                        bool ambiguous = markdown.Count !=
                            CountInlineLinkOpenings(line) ||
                            html.Count != HtmlAttributePattern.Matches(line).Count ||
                            (definition.Success && (markdown.Count != 0 || html.Count != 0));

                        // Inline code can contain syntactically valid text that
                        // must never be silently rewritten as a live link.
                        if ((markdown.Count > 0 || html.Count > 0) &&
                            line.Contains((char)96))
                        {
                            ambiguous = true;
                        }

                        if (ambiguous)
                        {
                            if (relevant)
                            {
                                blockers.Add(context + " : syntaxe de lien ambiguë.");
                            }
                        }
                        else
                        {
                            IEnumerable<Match> targets = definition.Success
                                ? [definition]
                                : markdown.Cast<Match>().Concat(html.Cast<Match>());
                            foreach (Match match in targets)
                            {
                                Group capture = match.Groups["target"];
                                LinkChange change = EvaluateTarget(file, capture.Value);
                                if (change.Blocker is not null)
                                {
                                    if (relevant)
                                    {
                                        blockers.Add(context + " : " + change.Blocker);
                                    }
                                }
                                else if (change.Replacement is not null)
                                {
                                    ComplexMigrationLinkRewrite rewrite = new(
                                        projectRelative, workspaceRelative,
                                        lineNumber, offset + capture.Index,
                                        capture.Value, change.Replacement,
                                        sourceHash ??= Convert.ToHexString(
                                            SHA256.HashData(bytes)));
                                    if (internalDocument)
                                    {
                                        rewrites.Add(rewrite);
                                    }
                                    else
                                    {
                                        externalRewrites.Add(rewrite);
                                    }
                                }
                            }
                        }
                    }
                    }
                }
                catch (RegexMatchTimeoutException)
                {
                    // Only source documents of this project, or lines that
                    // may refer to its moved paths, can block migration.
                    // Ignore unrelated malformed external Markdown.
                    if (internalDocument || MayReferenceMovedDestination(line))
                    {
                        blockers.Add(workspaceRelative + ":" + lineNumber +
                            " : analyse de lien trop complexe (délai dépassé).");
                    }
                }

                offset = end + 1;
            }

            foreach ((string label, int usageLine) in referenceUsages)
            {
                // Unknown labels in other applications do not identify a
                // backlink to the project being moved. Never block on them.
                if (internalDocument && !referenceDefinitions.ContainsKey(label))
                {
                    blockers.Add(workspaceRelative + ":" + usageLine +
                        " : référence Markdown sans définition simple : " + label);
                }
            }

            ReportAfterDocument();
        }

        ReportProgress();
        return new ComplexMigrationLinkAudit(
            rewrites.OrderBy(item => item.WorkspaceRelativePath,
                StringComparer.OrdinalIgnoreCase).ThenBy(item => item.Offset).ToArray(),
            blockers.Distinct(StringComparer.OrdinalIgnoreCase).ToArray())
        {
            ExternalRewrites = externalRewrites
                .OrderBy(item => item.WorkspaceRelativePath,
                    StringComparer.OrdinalIgnoreCase)
                .ThenBy(item => item.Offset).ToArray(),
            ProjectDocumentsInspected = projectFiles,
            ExternalDocumentsInspected = externalParsed,
            ExternalDocumentsSkipped = externalSkipped
        };
    }

    /// <summary>
    /// Counts raw inline link starts without compiling or allocating a new
    /// regex for each Markdown line. This is intentionally a conservative
    /// syntax count, not a complete Markdown grammar.
    /// </summary>
    /// <param name="line">One original Markdown line.</param>
    /// <returns>Number of literal closing-bracket/open-parenthesis pairs.</returns>
    private static int CountInlineLinkOpenings(string line)
    {
        int count = 0;
        for (int index = 0; index < line.Length - 1; index++)
        {
            if (line[index] == ']' && line[index + 1] == '(')
            {
                count++;
            }
        }

        return count;
    }

    /// <summary>
    /// Scans an external Markdown file incrementally, never retaining the
    /// full content of unrelated documents. Overlapping char fragments
    /// preserve links whose project name crosses a stream buffer boundary.
    /// An undecodable source is conservatively rechecked using raw bytes.
    /// </summary>
    /// <param name="file">Workspace Markdown source, not a reparse point.</param>
    /// <param name="cancellationToken">Cancellation during I/O and parsing.</param>
    /// <returns>Whether a moved-project path may be referenced.</returns>
    private async Task<bool> MayNameMovedProjectInFileAsync(
        string file, CancellationToken cancellationToken)
    {
        await using FileStream input = new(file, FileMode.Open,
            FileAccess.Read, FileShare.Read, 65536, useAsync: true);
        using StreamReader reader = new(input, StrictUtf8,
            detectEncodingFromByteOrderMarks: false, bufferSize: 8192,
            leaveOpen: true);
        char[] buffer = new char[8192];
        int overlap = Math.Max(_projectName.Length,
            _encodedProjectName.Length) - 1;
        string tail = string.Empty;
        try
        {
            int count;
            while ((count = await reader.ReadAsync(buffer.AsMemory(),
                cancellationToken)) != 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                string text = tail + new string(buffer, 0, count);
                if (MayNameMovedProject(text))
                {
                    return true;
                }

                tail = overlap > 0 && text.Length > overlap
                    ? text[^overlap..] : text;
            }
        }
        catch (DecoderFallbackException)
        {
            // The normal strict decoder below decides whether malformed
            // bytes are actually relevant before creating a blocker.
            return true;
        }

        return false;
    }

    /// <summary>
    /// A malformed external link is a possible blocker only when its text
    /// names a destination being moved in this particular project. Valid
    /// external backlinks are still resolved through EvaluateTarget, even
    /// when they use relative dot segments.
    /// </summary>
    /// <param name="line">Untrusted Markdown source line.</param>
    /// <returns>Whether the line may reference a candidate move.</returns>
    private bool MayReferenceMovedDestination(string line)
    {
        string normalized = line.Replace('\\', '/');
        return _possibleMovedPaths.Any(candidate =>
            normalized.Contains(candidate, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// A backlink from outside the moved project must name the project
    /// directory. This only short-circuits unrelated external documents;
    /// it never excludes the target project's own Markdown.
    /// </summary>
    /// <param name="content">Strictly decoded external UTF-8 Markdown.</param>
    /// <returns>Whether the source deserves per-line link analysis.</returns>
    private bool MayNameMovedProject(string content) =>
        content.Contains(_projectName, StringComparison.OrdinalIgnoreCase) ||
        content.Contains(_encodedProjectName,
            StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Detects possible affected external links in undecodable Markdown
    /// without disqualifying every unrelated non-UTF8 source in the workspace.
    /// </summary>
    /// <param name="bytes">Untrusted source bytes.</param>
    /// <returns>Whether the source may reference a moved path.</returns>
    private bool MayContainMovedDestination(byte[] bytes)
    {
        foreach (string candidate in _possibleMovedPaths)
        {
            byte[] marker = Encoding.UTF8.GetBytes(candidate);
            if (bytes.AsSpan().IndexOf(marker) >= 0)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Legacy convenience API for consumers needing blockers only.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Unqualified affected references.</returns>
    public async Task<IReadOnlyList<string>> FindBlockingReferencesAsync(
        CancellationToken cancellationToken = default) =>
        (await AnalyzeAsync(cancellationToken)).Blockers;

    /// <summary>
    /// Compute new relative destination without resolving unsupported formats
    /// or guessing identities from missing original targets.
    /// </summary>
    /// <param name="source">Source Markdown file.</param>
    /// <param name="raw">Captured exact target.</param>
    /// <returns>Proposed target, blocking diagnosis, or unchanged.</returns>
    private LinkChange EvaluateTarget(string source, string raw)
    {
        bool angle = raw.StartsWith('<') && raw.EndsWith('>');
        string target = angle ? raw[1..^1] : raw;
        if (target.Length == 0 || target.StartsWith('#') ||
            target.StartsWith("//", StringComparison.Ordinal))
        {
            return new LinkChange(null, null);
        }

        if (Uri.TryCreate(target, UriKind.Absolute, out Uri? uri))
        {
            return uri.Scheme is "http" or "https" or "mailto" or "data"
                ? new LinkChange(null, null)
                : new LinkChange(null, "schéma non qualifié : " + raw);
        }

        if (target.StartsWith('/') || target.StartsWith('\\') ||
            target.Contains('%') || target.Contains('?') ||
            target.Contains('\\') || target.Contains(':'))
        {
            return new LinkChange(null, "cible encodée ou absolue : " + raw);
        }

        int anchor = target.IndexOf('#');
        string path = anchor >= 0 ? target[..anchor] : target;
        string suffix = anchor >= 0 ? target[anchor..] : string.Empty;
        if (path.Length == 0)
        {
            return new LinkChange(null, null);
        }

        string resolved;
        try
        {
            resolved = Path.GetFullPath(Path.Combine(
                Path.GetDirectoryName(source)!, path.Replace('/',
                    Path.DirectorySeparatorChar)));
        }
        catch (Exception exception) when (exception is ArgumentException or
            NotSupportedException or PathTooLongException)
        {
            return new LinkChange(null, "chemin de lien non résoluble : " + raw);
        }

        string mappedSource = MapPath(source);
        string mappedTarget = MapPath(resolved);
        string after = Path.GetFullPath(Path.Combine(
            Path.GetDirectoryName(mappedSource)!, path.Replace('/',
                Path.DirectorySeparatorChar)));
        if (string.Equals(after, mappedTarget,
            StringComparison.OrdinalIgnoreCase))
        {
            return new LinkChange(null, null);
        }

        if (!IsWithin(_workspaceRoot, resolved) ||
            (!File.Exists(resolved) && !Directory.Exists(resolved)))
        {
            return new LinkChange(null, "cible absente ou hors workspace : " + raw);
        }

        string newPath = Path.GetRelativePath(
            Path.GetDirectoryName(mappedSource)!, mappedTarget)
            .Replace('\\', '/');
        if (newPath == "." || Path.IsPathRooted(newPath))
        {
            return new LinkChange(null, "chemin recalculé non portable : " + raw);
        }

        string replacement = (angle ? "<" : string.Empty) +
            newPath + suffix + (angle ? ">" : string.Empty);
        if (!angle && replacement.Any(char.IsWhiteSpace))
        {
            return new LinkChange(null, "chemin recalculé avec espaces : " + raw);
        }

        return new LinkChange(replacement, null);
    }

    /// <summary>Maps source paths through only the explicit candidate moves.</summary>
    /// <param name="sourcePath">Existing absolute path.</param>
    /// <returns>Hypothetical location after migration.</returns>
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

    /// <summary>Checks path containment with a separator boundary.</summary>
    /// <param name="root">Canonical root.</param>
    /// <param name="path">Candidate path.</param>
    /// <returns>Whether path is strictly within root.</returns>
    private static bool IsWithin(string root, string path) =>
        path.StartsWith(root + Path.DirectorySeparatorChar,
            StringComparison.OrdinalIgnoreCase);

    private sealed record LinkChange(string? Replacement, string? Blocker);
}
