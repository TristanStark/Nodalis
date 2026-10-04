using System.Text;
using Nodalis.Core.Domain;
using Nodalis.Core.Glossary;
using Nodalis.Infrastructure.Persistence;
using Nodalis.Infrastructure.Reliability;

namespace Nodalis.Infrastructure.Glossary;

public sealed class GlossaryService
{
    public async Task<IReadOnlyList<GlossaryScope>> ResolveScopesAsync(
        string workspaceRoot,
        string? contextPath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRoot);

        var root = Path.GetFullPath(workspaceRoot);
        var contextDirectory = ResolveContextDirectory(
            root,
            contextPath);

        string? projectDirectory = null;
        ProjectManifest? project = null;
        string? applicationDirectory = null;
        ApplicationManifest? application = null;

        for (var current = contextDirectory;
             current is not null && IsInsideOrEqual(current, root);
             current = Directory.GetParent(current)?.FullName)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (project is null)
            {
                var projectManifestPath = Path.Combine(
                    current,
                    WorkspaceLayout.ProjectManifestFileName);

                if (File.Exists(projectManifestPath))
                {
                    project = await AtomicJsonFile.ReadAsync<ProjectManifest>(
                        projectManifestPath,
                        cancellationToken);
                    projectDirectory = current;
                }
            }

            if (application is null)
            {
                var applicationManifestPath = Path.Combine(
                    current,
                    WorkspaceLayout.ApplicationManifestFileName);

                if (File.Exists(applicationManifestPath))
                {
                    application = await AtomicJsonFile.ReadAsync<ApplicationManifest>(
                        applicationManifestPath,
                        cancellationToken);
                    applicationDirectory = current;
                }
            }

            if (project is not null &&
                application is not null)
            {
                break;
            }

            if (string.Equals(
                    current,
                    root,
                    StringComparison.OrdinalIgnoreCase))
            {
                break;
            }
        }

        var scopes = new List<GlossaryScope>();

        if (project is not null &&
            projectDirectory is not null)
        {
            var glossarySection = project.Sections
                .FirstOrDefault(section =>
                    string.Equals(
                        section.TemplateKey,
                        "glossary",
                        StringComparison.OrdinalIgnoreCase));

            var glossaryDirectory = glossarySection is null
                ? Path.Combine(
                    projectDirectory,
                    "Glossaire")
                : Path.Combine(
                    projectDirectory,
                    WindowsPathRules.SanitizeSegment(
                        glossarySection.Name));

            var glossaryFile = glossarySection is null
                ? Path.Combine(
                    glossaryDirectory,
                    WorkspaceLayout.GlobalGlossaryFileName)
                : Path.Combine(
                    glossaryDirectory,
                    WindowsPathRules.SanitizeSegment(
                        glossarySection.Name) + ".md");

            scopes.Add(new GlossaryScope
            {
                Kind = GlossaryScopeKind.Project,
                DisplayName = $"Projet · {project.Name}",
                DirectoryPath = glossaryDirectory,
                FilePath = glossaryFile
            });
        }

        if (application is not null &&
            applicationDirectory is not null)
        {
            scopes.Add(new GlossaryScope
            {
                Kind = GlossaryScopeKind.Application,
                DisplayName = $"Application · {application.Name}",
                DirectoryPath = applicationDirectory,
                FilePath = Path.Combine(
                    applicationDirectory,
                    WorkspaceLayout.GlobalGlossaryFileName)
            });
        }

        scopes.Add(new GlossaryScope
        {
            Kind = GlossaryScopeKind.Global,
            DisplayName = "Global",
            DirectoryPath = root,
            FilePath = Path.Combine(
                root,
                WorkspaceLayout.GlobalGlossaryFileName)
        });

        foreach (var scope in scopes)
        {
            await EnsureGlossaryFileAsync(
                scope,
                cancellationToken);
        }

        return scopes;
    }

    public async Task<IReadOnlyList<GlossaryEntry>> LoadEntriesAsync(
        GlossaryScope scope,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scope);

        await EnsureGlossaryFileAsync(
            scope,
            cancellationToken);

        var markdown = await File.ReadAllTextAsync(
            scope.FilePath,
            cancellationToken);

        return Parse(
            markdown,
            scope);
    }

    public async Task<IReadOnlyList<GlossaryEntry>> LoadEffectiveEntriesAsync(
        string workspaceRoot,
        string? contextPath,
        CancellationToken cancellationToken = default)
    {
        var scopes = await ResolveScopesAsync(
            workspaceRoot,
            contextPath,
            cancellationToken);

        var entries = new List<GlossaryEntry>();

        foreach (var scope in scopes)
        {
            entries.AddRange(
                await LoadEntriesAsync(
                    scope,
                    cancellationToken));
        }

        return entries;
    }

    public async Task<GlossaryResolution> ResolveAsync(
        string workspaceRoot,
        string? contextPath,
        string query,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query);

        var normalizedQuery = query.Trim();
        var scopes = await ResolveScopesAsync(
            workspaceRoot,
            contextPath,
            cancellationToken);

        var matches = new List<GlossaryEntry>();

        foreach (var scope in scopes)
        {
            var entries = await LoadEntriesAsync(
                scope,
                cancellationToken);

            var exactTerms = entries
                .Where(entry => string.Equals(
                    entry.Term,
                    normalizedQuery,
                    StringComparison.CurrentCultureIgnoreCase))
                .ToArray();

            if (exactTerms.Length > 0)
            {
                matches.AddRange(exactTerms);
                continue;
            }

            matches.AddRange(
                entries.Where(entry =>
                    entry.Synonyms.Any(value =>
                        string.Equals(
                            value,
                            normalizedQuery,
                            StringComparison.CurrentCultureIgnoreCase)) ||
                    entry.Acronyms.Any(value =>
                        string.Equals(
                            value,
                            normalizedQuery,
                            StringComparison.CurrentCultureIgnoreCase))));
        }

        return new GlossaryResolution
        {
            Query = normalizedQuery,
            Primary = matches.FirstOrDefault(),
            Alternatives = matches.Skip(1).ToList()
        };
    }

    public async Task AppendAsync(
        GlossaryScope scope,
        GlossaryEntryDraft draft,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(draft);
        ArgumentException.ThrowIfNullOrWhiteSpace(draft.Term);
        ArgumentException.ThrowIfNullOrWhiteSpace(draft.Definition);

        await EnsureGlossaryFileAsync(
            scope,
            cancellationToken);

        var existingEntries = await LoadEntriesAsync(
            scope,
            cancellationToken);

        if (existingEntries.Any(entry =>
                string.Equals(
                    entry.Term,
                    draft.Term.Trim(),
                    StringComparison.CurrentCultureIgnoreCase)))
        {
            throw new InvalidOperationException(
                $"Le terme '{draft.Term.Trim()}' existe déjà dans {scope.DisplayName}.");
        }

        var session = await TextDocumentSession.OpenAsync(
            scope.FilePath,
            cancellationToken);

        var existing = session.Content.TrimEnd();
        var block = FormatEntry(draft);

        var updated = string.IsNullOrWhiteSpace(existing)
            ? $"# Glossaire\n\n{block}"
            : $"{existing}\n\n{block}";

        await session.SaveAsync(
            updated,
            cancellationToken);
    }

    public static IReadOnlyList<GlossaryEntry> Parse(
        string markdown,
        GlossaryScope scope)
    {
        ArgumentNullException.ThrowIfNull(markdown);
        ArgumentNullException.ThrowIfNull(scope);

        var lines = markdown
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split('\n');

        var entries = new List<GlossaryEntry>();
        EntryBuilder? current = null;
        FieldKind activeField = FieldKind.None;

        for (var index = 0;
             index < lines.Length;
             index++)
        {
            var line = lines[index];

            if (line.StartsWith(
                    "## ",
                    StringComparison.Ordinal))
            {
                AddCurrentIfMeaningful(
                    entries,
                    current,
                    scope);

                current = new EntryBuilder
                {
                    Term = line[3..].Trim(),
                    LineNumber = index + 1
                };

                activeField = FieldKind.None;
                continue;
            }

            if (current is null)
            {
                continue;
            }

            if (TryReadField(
                    line,
                    "Définition",
                    out var definition))
            {
                activeField = FieldKind.Definition;
                AppendDefinition(
                    current,
                    definition);
                continue;
            }

            if (TryReadField(
                    line,
                    "Synonymes",
                    out var synonyms))
            {
                activeField = FieldKind.None;
                current.Synonyms.AddRange(
                    SplitValues(synonyms));
                continue;
            }

            if (TryReadField(
                    line,
                    "Acronymes",
                    out var acronyms))
            {
                activeField = FieldKind.None;
                current.Acronyms.AddRange(
                    SplitValues(acronyms));
                continue;
            }

            if (TryReadField(
                    line,
                    "Synonymes / acronymes",
                    out var legacyAliases))
            {
                activeField = FieldKind.None;
                current.Synonyms.AddRange(
                    SplitValues(legacyAliases));
                continue;
            }

            if (TryReadField(
                    line,
                    "Liens",
                    out var links))
            {
                activeField = FieldKind.None;
                current.Links.AddRange(
                    SplitValues(links));
                continue;
            }

            if (activeField == FieldKind.Definition)
            {
                if (string.IsNullOrWhiteSpace(line))
                {
                    if (current.Definition.Length > 0 &&
                        current.Definition[current.Definition.Length - 1] != '\n')
                    {
                        current.Definition.AppendLine();
                    }
                }
                else
                {
                    AppendDefinition(
                        current,
                        line.Trim());
                }
            }
        }

        AddCurrentIfMeaningful(
            entries,
            current,
            scope);

        return entries;
    }

    public static string FormatEntry(
        GlossaryEntryDraft draft)
    {
        ArgumentNullException.ThrowIfNull(draft);

        var builder = new StringBuilder();

        builder.AppendLine(
            $"## {draft.Term.Trim()}");
        builder.AppendLine();

        builder.AppendLine(
            $"**Définition :** {draft.Definition.Trim()}");
        builder.AppendLine();

        builder.AppendLine(
            $"**Synonymes :** {JoinValues(draft.Synonyms)}");
        builder.AppendLine();

        builder.AppendLine(
            $"**Acronymes :** {JoinValues(draft.Acronyms)}");
        builder.AppendLine();

        builder.AppendLine(
            $"**Liens :** {JoinValues(draft.Links)}");

        return builder
            .ToString()
            .TrimEnd() + "\n";
    }

    private static async Task EnsureGlossaryFileAsync(
        GlossaryScope scope,
        CancellationToken cancellationToken)
    {
        if (File.Exists(scope.FilePath))
        {
            return;
        }

        Directory.CreateDirectory(
            scope.DirectoryPath);

        await AtomicFileWriter.WriteAllTextAsync(
            scope.FilePath,
            "# Glossaire\n\n",
            cancellationToken);
    }

    private static bool TryReadField(
        string line,
        string fieldName,
        out string value)
    {
        var prefix = $"**{fieldName} :**";

        if (line.StartsWith(
                prefix,
                StringComparison.CurrentCultureIgnoreCase))
        {
            value = line[prefix.Length..].Trim();
            return true;
        }

        value = string.Empty;
        return false;
    }

    private static void AddCurrentIfMeaningful(
        ICollection<GlossaryEntry> entries,
        EntryBuilder? current,
        GlossaryScope scope)
    {
        if (current is null ||
            string.IsNullOrWhiteSpace(current.Term))
        {
            return;
        }

        var definition = current.Definition
            .ToString()
            .Trim();

        var synonyms = NormalizeValues(
            current.Synonyms);

        var acronyms = NormalizeValues(
            current.Acronyms);

        var links = NormalizeValues(
            current.Links);

        if (string.IsNullOrWhiteSpace(definition) &&
            synonyms.Count == 0 &&
            acronyms.Count == 0 &&
            links.Count == 0)
        {
            return;
        }

        entries.Add(new GlossaryEntry
        {
            Term = current.Term.Trim(),
            Definition = definition,
            Synonyms = synonyms,
            Acronyms = acronyms,
            Links = links,
            Scope = scope,
            LineNumber = current.LineNumber
        });
    }

    private static void AppendDefinition(
        EntryBuilder builder,
        string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        if (builder.Definition.Length > 0 &&
            !builder.Definition
                .ToString()
                .EndsWith(
                    "\n",
                    StringComparison.Ordinal))
        {
            builder.Definition.AppendLine();
        }

        builder.Definition.Append(
            value.Trim());
    }

    private static IEnumerable<string> SplitValues(
        string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return [];
        }

        var separator = value.Contains(';')
            ? ';'
            : ',';

        return value
            .Split(
                separator,
                StringSplitOptions.TrimEntries |
                StringSplitOptions.RemoveEmptyEntries);
    }

    private static List<string> NormalizeValues(
        IEnumerable<string> values) =>
        values
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .Distinct(StringComparer.CurrentCultureIgnoreCase)
            .OrderBy(value => value, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

    private static string JoinValues(
        IEnumerable<string> values) =>
        string.Join(
            "; ",
            NormalizeValues(values));

    private static string ResolveContextDirectory(
        string workspaceRoot,
        string? contextPath)
    {
        if (string.IsNullOrWhiteSpace(contextPath))
        {
            return workspaceRoot;
        }

        var fullPath = Path.GetFullPath(
            contextPath);

        if (File.Exists(fullPath) ||
            Path.HasExtension(fullPath))
        {
            return Path.GetDirectoryName(fullPath)
                ?? workspaceRoot;
        }

        return Directory.Exists(fullPath)
            ? fullPath
            : workspaceRoot;
    }

    private static bool IsInsideOrEqual(
        string candidate,
        string root)
    {
        var fullCandidate = Path.GetFullPath(candidate)
            .TrimEnd(
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar);

        var fullRoot = Path.GetFullPath(root)
            .TrimEnd(
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar);

        return string.Equals(
                   fullCandidate,
                   fullRoot,
                   StringComparison.OrdinalIgnoreCase) ||
               fullCandidate.StartsWith(
                   fullRoot + Path.DirectorySeparatorChar,
                   StringComparison.OrdinalIgnoreCase);
    }

    private sealed class EntryBuilder
    {
        public string Term { get; init; } = string.Empty;

        public int LineNumber { get; init; }

        public StringBuilder Definition { get; } = new();

        public List<string> Synonyms { get; } = [];

        public List<string> Acronyms { get; } = [];

        public List<string> Links { get; } = [];
    }

    private enum FieldKind
    {
        None,
        Definition
    }
}
