using Nodalis.Core.Abstractions;
using Nodalis.Core.Notes;
using Nodalis.Core.Templates;
using Nodalis.Infrastructure.Persistence;
using Nodalis.Infrastructure.Reliability;

namespace Nodalis.Infrastructure.Notes;

public sealed class WorkspaceDailyNoteService
{
    private const string DailyNoteTemplateKey = "daily-note";
    private const string OpenedItemsStartMarker = "<!-- nodalis-opened-today:start -->";
    private const string OpenedItemsEndMarker = "<!-- nodalis-opened-today:end -->";

    private readonly string _workspaceRoot;
    private readonly string _journalDirectory;
    private readonly ITemplateStore _templateStore;

    /// <summary>
    /// Initializes a new instance of <see cref="WorkspaceDailyNoteService"/>.
    /// </summary>
    /// <param name="workspaceRoot">The workspace root directory.</param>
    /// <param name="templateStore">The canonical workspace template store.</param>
    public WorkspaceDailyNoteService(
            string workspaceRoot,
            ITemplateStore templateStore)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(
            workspaceRoot);
        ArgumentNullException.ThrowIfNull(
            templateStore);

        _workspaceRoot =
            Path.GetFullPath(
                workspaceRoot);
        _journalDirectory =
            Path.Combine(
                _workspaceRoot,
                WorkspaceLayout.JournalDirectoryName);
        _templateStore =
            templateStore;
    }

    /// <summary>
    /// Creates or returns the unique daily note for the local date represented by <paramref name="localNow"/>.
    /// </summary>
    /// <param name="localNow">The current local date and time, including its UTC offset.</param>
    /// <param name="openedItems">Items opened during that local day to include on first creation.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The daily-note descriptor.</returns>
    public async Task<DailyNoteItem> GetOrCreateAsync(
            DateTimeOffset localNow,
            IReadOnlyList<DailyNoteReference>? openedItems = null,
            CancellationToken cancellationToken = default)
    {
        DateOnly date =
            DateOnly.FromDateTime(
                localNow.DateTime);
        string path =
            GetPath(
                date);

        if (!File.Exists(
                path))
        {
            Directory.CreateDirectory(
                _journalDirectory);

            IReadOnlyDictionary<string, string> variables =
                MarkdownTemplateRenderer.CreateStandardVariables(
                    $"Journal — {date:yyyy-MM-dd}",
                    CreateDailyDocumentId(
                        date),
                    localNow,
                    new Dictionary<string, string>(
                        StringComparer.OrdinalIgnoreCase)
                    {
                        ["opened_today"] =
                            BuildOpenedItemsSection(
                                openedItems ??
                                Array.Empty<DailyNoteReference>())
                    });

            string content =
                await _templateStore.RenderAsync(
                    DailyNoteTemplateKey,
                    variables,
                    cancellationToken);

            try
            {
                await using FileStream stream =
                    new FileStream(
                        path,
                        FileMode.CreateNew,
                        FileAccess.Write,
                        FileShare.Read,
                        4096,
                        useAsync: true);
                await using StreamWriter writer =
                    new StreamWriter(
                        stream);
                await writer.WriteAsync(
                    content.AsMemory(),
                    cancellationToken);
            }
            catch (IOException) when (
                File.Exists(
                    path))
            {
                // Another create request won the race. The existing daily note is canonical.
            }
        }

        return CreateItem(
            date,
            localNow);
    }

    /// <summary>
    /// Returns the most recent daily notes ordered from newest to oldest.
    /// </summary>
    /// <param name="localNow">The current local date and time used to identify today.</param>
    /// <param name="maximumCount">Maximum number of notes to return.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The recent daily notes.</returns>
    public Task<IReadOnlyList<DailyNoteItem>> GetRecentAsync(
            DateTimeOffset localNow,
            int maximumCount = 14,
            CancellationToken cancellationToken = default)
    {
        if (maximumCount <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maximumCount));
        }

        cancellationToken.ThrowIfCancellationRequested();

        if (!Directory.Exists(
                _journalDirectory))
        {
            return Task.FromResult<IReadOnlyList<DailyNoteItem>>(
                Array.Empty<DailyNoteItem>());
        }

        global::System.Collections.Generic.List<global::Nodalis.Core.Notes.DailyNoteItem> result =
            Directory
                .EnumerateFiles(
                    _journalDirectory,
                    "*.md",
                    SearchOption.TopDirectoryOnly)
                .Select(path =>
                    TryParseDate(
                        path,
                        out DateOnly date)
                            ? CreateItem(
                                date,
                                localNow)
                            : null)
                .Where(item =>
                    item is not null)
                .Cast<DailyNoteItem>()
                .OrderByDescending(item =>
                    item.Date)
                .Take(
                    maximumCount)
                .ToList();

        return Task.FromResult<IReadOnlyList<DailyNoteItem>>(
            result);
    }

    /// <summary>
    /// Replaces the generated "opened today" block in one daily note without modifying any referenced source file.
    /// </summary>
    /// <param name="date">The local date of the journal file.</param>
    /// <param name="openedItems">The current set of opened items for that date.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public async Task SyncOpenedItemsAsync(
            DateOnly date,
            IReadOnlyList<DailyNoteReference> openedItems,
            CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(
            openedItems);

        string path =
            GetPath(
                date);

        if (!File.Exists(
                path))
        {
            throw new FileNotFoundException(
                "La note quotidienne est introuvable.",
                path);
        }

        global::Nodalis.Infrastructure.Reliability.TextDocumentSession session =
            await TextDocumentSession.OpenAsync(
                path,
                cancellationToken);

        string generatedBlock =
            BuildOpenedItemsSection(
                openedItems);
        string content =
            ReplaceGeneratedBlock(
                session.Content,
                generatedBlock);

        if (string.Equals(
                content,
                session.Content,
                StringComparison.Ordinal))
        {
            return;
        }

        await session.SaveAsync(
            content,
            cancellationToken);
    }

    /// <summary>
    /// Builds the generated Markdown block containing links to items opened during the day.
    /// </summary>
    /// <param name="openedItems">The items to render.</param>
    /// <returns>The generated Markdown section.</returns>
    public static string BuildOpenedItemsSection(
            IReadOnlyList<DailyNoteReference> openedItems)
    {
        ArgumentNullException.ThrowIfNull(
            openedItems);

        global::System.Collections.Generic.List<global::Nodalis.Core.Notes.DailyNoteReference> normalized =
            openedItems
                .Where(item =>
                    !string.IsNullOrWhiteSpace(
                        item.QualifiedName))
                .DistinctBy(
                    item => item.QualifiedName,
                    StringComparer.OrdinalIgnoreCase)
                .OrderBy(
                    item => item.DisplayName,
                    StringComparer.CurrentCultureIgnoreCase)
                .ToList();

        global::System.Text.StringBuilder builder =
            new System.Text.StringBuilder();

        builder.AppendLine(
            OpenedItemsStartMarker);
        builder.AppendLine(
            "## Éléments ouverts aujourd'hui");
        builder.AppendLine();

        if (normalized.Count == 0)
        {
            builder.AppendLine(
                "_Aucun élément enregistré pour cette journée._");
        }
        else
        {
            foreach (DailyNoteReference item in
                     normalized)
            {
                builder.Append(
                    "- [[");
                builder.Append(
                    item.QualifiedName);
                builder.Append(
                    "|");
                builder.Append(
                    EscapeLinkAlias(
                        item.DisplayName));
                builder.Append(
                    "]] · ");
                builder.AppendLine(
                    item.Kind);
            }
        }

        builder.AppendLine();
        builder.Append(
            OpenedItemsEndMarker);

        return builder.ToString();
    }

    /// <summary>
    /// Returns the deterministic journal file path for one local date.
    /// </summary>
    /// <param name="date">The local journal date.</param>
    /// <returns>The absolute Markdown path.</returns>
    private string GetPath(
            DateOnly date) =>
        Path.Combine(
            _journalDirectory,
            $"{date:yyyy-MM-dd}.md");

    /// <summary>
    /// Creates a daily-note descriptor.
    /// </summary>
    /// <param name="date">The note date.</param>
    /// <param name="localNow">The current local time.</param>
    /// <returns>The descriptor.</returns>
    private DailyNoteItem CreateItem(
            DateOnly date,
            DateTimeOffset localNow)
    {
        string path =
            GetPath(
                date);

        return new DailyNoteItem
        {
            Date =
                date,
            FullPath =
                path,
            RelativePath =
                Path.GetRelativePath(
                        _workspaceRoot,
                        path)
                    .Replace(
                        Path.DirectorySeparatorChar,
                        '/'),
            IsToday =
                date ==
                DateOnly.FromDateTime(
                    localNow.DateTime)
        };
    }

    /// <summary>
    /// Parses a journal date from a canonical daily-note filename.
    /// </summary>
    /// <param name="path">The daily-note path.</param>
    /// <param name="date">The parsed date.</param>
    /// <returns><see langword="true"/> when the filename is canonical.</returns>
    private static bool TryParseDate(
            string path,
            out DateOnly date) =>
        DateOnly.TryParseExact(
            Path.GetFileNameWithoutExtension(
                path),
            "yyyy-MM-dd",
            System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.None,
            out date);

    /// <summary>
    /// Replaces or appends the generated opened-items block.
    /// </summary>
    /// <param name="content">The current Markdown content.</param>
    /// <param name="generatedBlock">The newly generated block.</param>
    /// <returns>The updated Markdown.</returns>
    private static string ReplaceGeneratedBlock(
            string content,
            string generatedBlock)
    {
        int start =
            content.IndexOf(
                OpenedItemsStartMarker,
                StringComparison.Ordinal);
        int end =
            content.IndexOf(
                OpenedItemsEndMarker,
                StringComparison.Ordinal);

        if (start >= 0 &&
            end >= start)
        {
            int endExclusive =
                end +
                OpenedItemsEndMarker.Length;

            return content[..start] +
                generatedBlock +
                content[endExclusive..];
        }

        string trimmed =
            content.TrimEnd();

        return trimmed.Length == 0
            ? generatedBlock + Environment.NewLine
            : trimmed +
                Environment.NewLine +
                Environment.NewLine +
                generatedBlock +
                Environment.NewLine;
    }

    /// <summary>
    /// Creates a deterministic identity for template variables belonging to one daily note.
    /// </summary>
    /// <param name="date">The local journal date.</param>
    /// <returns>The deterministic identifier.</returns>
    private static Guid CreateDailyDocumentId(
            DateOnly date)
    {
        byte[] hash =
            System.Security.Cryptography.SHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes(
                    $"daily-note|{date:yyyy-MM-dd}"));

        return new Guid(
            hash.AsSpan(
                0,
                16));
    }

    /// <summary>
    /// Escapes the alias portion of an internal Nodalis link.
    /// </summary>
    /// <param name="value">The alias text.</param>
    /// <returns>The safe alias.</returns>
    private static string EscapeLinkAlias(
            string value) =>
        value
            .Replace(
                "|",
                "—",
                StringComparison.Ordinal)
            .Replace(
                "]]",
                "] ]",
                StringComparison.Ordinal);
}
