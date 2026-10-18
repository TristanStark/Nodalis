using System.Text;
using Nodalis.Core.Composites;
using Nodalis.Core.Links;
using Nodalis.Infrastructure.Links;

namespace Nodalis.Infrastructure.Composites;

/// <summary>Builds an offline virtual composite without editing Markdown or business relations.</summary>
public sealed class CompositeWorkspaceRenderer
{
    private const int MaximumDepth = 8;
    private const int MaximumFragments = 256;
    private const int MaximumCharacters = 2_000_000;
    private readonly string _root;
    private readonly LinkIndexCatalog _catalog;

    /// <summary>Reads only local document identities in the existing link index.</summary>
    /// <param name="workspaceRoot">Workspace folder.</param>
    /// <param name="catalog">Latest canonical link index.</param>
    public CompositeWorkspaceRenderer(string workspaceRoot, LinkIndexCatalog catalog)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRoot);
        ArgumentNullException.ThrowIfNull(catalog);
        _root = Path.GetFullPath(workspaceRoot);
        _catalog = catalog;
    }

    private sealed class State
    {
        internal int Fragments;
        internal int Characters;
        internal List<CompositeEdge> Edges = [];
        internal List<CompositeDiagnostic> Diagnostics = [];
    }

    /// <summary>Expands source text with a bounded recursive in-memory projection.</summary>
    /// <param name="parentPath">Local parent document path.</param>
    /// <param name="markdown">Parent editor text, which may contain unsaved edits.</param>
    /// <param name="token">Cooperative cancellation of obsolete previews.</param>
    /// <returns>Read-only Markdown snapshot plus source links and diagnostics.</returns>
    public async Task<CompositeRenderResult> RenderAsync(string parentPath,
        string markdown, CancellationToken token = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(parentPath);
        ArgumentNullException.ThrowIfNull(markdown);
        string path = Path.GetFullPath(parentPath);
        if (!IsSafePath(path))
        {
            throw new InvalidDataException("Document parent hors du workspace.");
        }

        LinkTargetEntry? parent = _catalog.Targets.FirstOrDefault(item =>
            item.Kind == LinkTargetKind.Document &&
            string.Equals(GetFullPath(item.RelativePath), path,
                StringComparison.OrdinalIgnoreCase));
        State state = new();
        HashSet<Guid> ancestors = [];
        if (parent is not null) ancestors.Add(parent.Id);
        string expanded = await ExpandAsync(markdown, parent?.Id ?? Guid.Empty,
            Path.GetRelativePath(_root, path), ancestors, state, 0, token);
        return new CompositeRenderResult(expanded,
            state.Edges.ToArray(), state.Diagnostics.ToArray());
    }

    /// <summary>Resolves a source line only when outside fenced code and within limits.</summary>
    /// <param name="markdown">Current Markdown.</param>
    /// <param name="parentId">Identity of the containing document.</param>
    /// <param name="origin">Source-relative diagnostics path.</param>
    /// <param name="stack">Active recursive document GUID stack.</param>
    /// <param name="state">Shared safety limits and audit.</param>
    /// <param name="depth">Current nesting depth.</param>
    /// <param name="token">Cancellation request.</param>
    /// <returns>Expanded display-only Markdown.</returns>
    private async Task<string> ExpandAsync(string markdown, Guid parentId,
        string origin, HashSet<Guid> stack, State state, int depth,
        CancellationToken token)
    {
        StringBuilder result = new();
        string[] lines = CompositeMarkdownSections.Normalize(markdown).Split('\n');
        bool fenced = false;
        char fenceMark = '\0';
        int fenceLength = 0;
        for (int i = 0; i < lines.Length; i++)
        {
            token.ThrowIfCancellationRequested();
            string line = lines[i];
            if (CompositeMarkdownSections.ToggleFence(line, ref fenced,
                ref fenceMark, ref fenceLength))
            {
                result.AppendLine(line);
                continue;
            }

            CompositeInclude? include = fenced ? null :
                CompositeMarkdownSyntax.Parse(line, i + 1);
            if (include is null)
            {
                result.AppendLine(line);
                continue;
            }

            if (depth >= MaximumDepth || state.Fragments >= MaximumFragments)
            {
                result.AppendLine(Problem(state, origin, i + 1,
                    "Limite de profondeur ou nombre de fragments dépassé."));
                continue;
            }

            state.Fragments++;
            if (!CompositeMarkdownSyntax.IsSafeTarget(include.Target) ||
                include.Section is { Length: > 200 } ||
                include.Section is not null && include.Section.Length == 0)
            {
                result.AppendLine(Problem(state, origin, i + 1,
                    "Cible ou section d'inclusion invalide."));
                continue;
            }

            LinkResolution resolved = WorkspaceLinkIndexService.Resolve(
                _catalog, include.Target);
            if (resolved.Status != LinkResolutionStatus.Resolved ||
                resolved.Target is not LinkTargetEntry child ||
                child.Kind != LinkTargetKind.Document)
            {
                result.AppendLine(Problem(state, origin, i + 1,
                    resolved.Status == LinkResolutionStatus.Ambiguous
                        ? "Cible ambiguë : " + include.Target
                        : "Document introuvable : " + include.Target));
                continue;
            }

            if (parentId != Guid.Empty)
            {
                state.Edges.Add(new CompositeEdge(parentId, child.Id));
            }

            if (stack.Contains(child.Id))
            {
                result.AppendLine(Problem(state, origin, i + 1,
                    "Cycle d'inclusion vers " + child.DisplayName));
                continue;
            }

            string path = GetFullPath(child.RelativePath);
            if (!IsSafePath(path) ||
                !path.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
            {
                result.AppendLine(Problem(state, origin, i + 1,
                    "Chemin source non autorisé."));
                continue;
            }

            try
            {
                FileInfo info = new(path);
                if (!info.Exists || info.Length > MaximumCharacters * 4L)
                {
                    result.AppendLine(Problem(state, origin, i + 1,
                        "Source absente ou trop volumineuse : " + child.DisplayName));
                    continue;
                }

                string source = await File.ReadAllTextAsync(path, token);
                CompositeExcerpt excerpt = include.Section is null
                    ? new CompositeExcerpt(source, 1, null)
                    : CompositeMarkdownSections.Extract(source, include.Section);
                if (excerpt.Diagnostic is string reason)
                {
                    result.AppendLine(Problem(state, origin, i + 1, reason));
                    continue;
                }

                if (state.Characters + excerpt.Markdown.Length > MaximumCharacters)
                {
                    result.AppendLine(Problem(state, origin, i + 1,
                        "Budget de texte inclus dépassé."));
                    continue;
                }

                state.Characters += excerpt.Markdown.Length;
                stack.Add(child.Id);
                string body;
                try
                {
                    body = await ExpandAsync(excerpt.Markdown, child.Id,
                        child.RelativePath, stack, state, depth + 1, token);
                }
                finally
                {
                    stack.Remove(child.Id);
                }

                string heading = child.DisplayName.Replace("[", "(",
                    StringComparison.Ordinal).Replace("]", ")", StringComparison.Ordinal);
                result.AppendLine();
                result.AppendLine("> **Contenu inclus (lecture seule) : " +
                    heading + "** — [[" + child.Id.ToString("D") +
                    (include.Section is null ? "" : "#" + include.Section) + "]]");
                result.AppendLine();
                result.AppendLine(body.TrimEnd());
                result.AppendLine();
                result.AppendLine("> ↳ Fin du contenu inclus");
                result.AppendLine();
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception error) when (error is IOException or
                UnauthorizedAccessException or DecoderFallbackException)
            {
                result.AppendLine(Problem(state, origin, i + 1,
                    "Document inaccessible : " + error.Message));
            }
        }

        return result.ToString();
    }

    /// <summary>Emits a visible placeholder and associates it with the original source line.</summary>
    /// <param name="state">Diagnostic collector.</param>
    /// <param name="origin">Relative source path.</param>
    /// <param name="line">One-based line.</param>
    /// <param name="message">Diagnostic text.</param>
    /// <returns>Safe Markdown placeholder.</returns>
    private static string Problem(State state, string origin, int line, string message)
    {
        state.Diagnostics.Add(new CompositeDiagnostic(origin, line, message));
        return "> ⚠ Inclusion indisponible : " + message.Replace('\n', ' ');
    }

    /// <summary>Converts a registry path, then validates it separately.</summary>
    /// <param name="relative">Source-relative document path.</param>
    /// <returns>Absolute source path.</returns>
    private string GetFullPath(string relative) =>
        Path.GetFullPath(Path.Combine(_root,
            relative.Replace('/', Path.DirectorySeparatorChar)));

    /// <summary>Prevents filesystem traversal outside this workspace.</summary>
    /// <param name="path">Fully resolved path.</param>
    /// <returns>True when path remains inside the workspace.</returns>
    private bool IsSafePath(string path)
    {
        string prefix = Path.TrimEndingDirectorySeparator(_root) +
            Path.DirectorySeparatorChar;
        if (!path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        try
        {
            DirectoryInfo? directory = Directory.GetParent(path);
            while (directory is not null &&
                (directory.FullName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(directory.FullName, _root, StringComparison.OrdinalIgnoreCase)))
            {
                if (directory.Exists &&
                    (directory.Attributes & FileAttributes.ReparsePoint) != 0)
                {
                    return false;
                }

                directory = directory.Parent;
            }

            return !File.Exists(path) ||
                (File.GetAttributes(path) & FileAttributes.ReparsePoint) == 0;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
