using System.Text;
using Nodalis.Core.Domain;
using Nodalis.Core.Glossary;
using Nodalis.Infrastructure.Persistence;
using Nodalis.Infrastructure.Reliability;

namespace Nodalis.Infrastructure.Glossary;

public sealed class GlossaryService
{
    /// <summary>
    /// Performs the <c>ResolveScopesAsync</c> operation.
    /// </summary>
    /// <param name="workspaceRoot">The <c>workspaceRoot</c> value.</param>
    /// <param name="contextPath">The <c>contextPath</c> value.</param>
    /// <param name="cancellationToken">The <c>cancellationToken</c> value.</param>
    /// <returns>The result of the operation.</returns>
public async Task<IReadOnlyList<GlossaryScope>> ResolveScopesAsync(
        string workspaceRoot,
        string? contextPath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRoot);

        string root = Path.GetFullPath(workspaceRoot);
        string contextDirectory = ResolveContextDirectory(
            root,
            contextPath);

        string? projectDirectory = null;
        ProjectManifest? project = null;
        string? applicationDirectory = null;
        ApplicationManifest? application = null;

        for (string current = contextDirectory;
             current is not null && IsInsideOrEqual(current, root);
             current = Directory.GetParent(current)?.FullName)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (project is null)
            {
                string projectManifestPath = Path.Combine(
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
                string applicationManifestPath = Path.Combine(
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

        global::System.Collections.Generic.List<global::Nodalis.Core.Glossary.GlossaryScope> scopes = new List<GlossaryScope>();

        if (project is not null &&
            projectDirectory is not null)
        {
            global::Nodalis.Core.Domain.SectionManifest? glossarySection = project.Sections
                .FirstOrDefault(section =>
                    string.Equals(
                        section.TemplateKey,
                        "glossary",
                        StringComparison.OrdinalIgnoreCase));

            string glossaryDirectory = glossarySection is null
                ? Path.Combine(
                    projectDirectory,
                    "Glossaire")
                : Path.Combine(
                    projectDirectory,
                    WindowsPathRules.SanitizeSegment(
                        glossarySection.Name));

            string glossaryFile = glossarySection is null
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

        foreach (global::Nodalis.Core.Glossary.GlossaryScope scope in scopes)
        {
            await EnsureGlossaryFileAsync(
                scope,
                cancellationToken);
        }

        return scopes;
    }

    /// <summary>
    /// Performs the <c>LoadEntriesAsync</c> operation.
    /// </summary>
    /// <param name="scope">The <c>scope</c> value.</param>
    /// <param name="cancellationToken">The <c>cancellationToken</c> value.</param>
    /// <returns>The result of the operation.</returns>
public async Task<IReadOnlyList<GlossaryEntry>> LoadEntriesAsync(
        GlossaryScope scope,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scope);

        await EnsureGlossaryFileAsync(
            scope,
            cancellationToken);

        string markdown = await File.ReadAllTextAsync(
            scope.FilePath,
            cancellationToken);

        return Parse(
            markdown,
            scope);
    }

    /// <summary>
    /// Performs the <c>LoadEffectiveEntriesAsync</c> operation.
    /// </summary>
    /// <param name="workspaceRoot">The <c>workspaceRoot</c> value.</param>
    /// <param name="contextPath">The <c>contextPath</c> value.</param>
    /// <param name="cancellationToken">The <c>cancellationToken</c> value.</param>
    /// <returns>The result of the operation.</returns>
public async Task<IReadOnlyList<GlossaryEntry>> LoadEffectiveEntriesAsync(
        string workspaceRoot,
        string? contextPath,
        CancellationToken cancellationToken = default)
    {
        global::System.Collections.Generic.IReadOnlyList<global::Nodalis.Core.Glossary.GlossaryScope> scopes = await ResolveScopesAsync(
            workspaceRoot,
            contextPath,
            cancellationToken);

        global::System.Collections.Generic.List<global::Nodalis.Core.Glossary.GlossaryEntry> entries = new List<GlossaryEntry>();

        foreach (global::Nodalis.Core.Glossary.GlossaryScope scope in scopes)
        {
            entries.AddRange(
                await LoadEntriesAsync(
                    scope,
                    cancellationToken));
        }

        return entries;
    }

    /// <summary>
    /// Performs the <c>ResolveAsync</c> operation.
    /// </summary>
    /// <param name="workspaceRoot">The <c>workspaceRoot</c> value.</param>
    /// <param name="contextPath">The <c>contextPath</c> value.</param>
    /// <param name="query">The <c>query</c> value.</param>
    /// <param name="cancellationToken">The <c>cancellationToken</c> value.</param>
    /// <returns>The result of the operation.</returns>
public async Task<GlossaryResolution> ResolveAsync(
        string workspaceRoot,
        string? contextPath,
        string query,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query);

        string normalizedQuery = query.Trim();
        global::System.Collections.Generic.IReadOnlyList<global::Nodalis.Core.Glossary.GlossaryScope> scopes = await ResolveScopesAsync(
            workspaceRoot,
            contextPath,
            cancellationToken);

        global::System.Collections.Generic.List<global::Nodalis.Core.Glossary.GlossaryEntry> matches = new List<GlossaryEntry>();

        foreach (global::Nodalis.Core.Glossary.GlossaryScope scope in scopes)
        {
            global::System.Collections.Generic.IReadOnlyList<global::Nodalis.Core.Glossary.GlossaryEntry> entries = await LoadEntriesAsync(
                scope,
                cancellationToken);

            global::Nodalis.Core.Glossary.GlossaryEntry[] exactTerms = entries
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

    /// <summary>
    /// Performs the <c>AppendAsync</c> operation.
    /// </summary>
    /// <param name="scope">The <c>scope</c> value.</param>
    /// <param name="draft">The <c>draft</c> value.</param>
    /// <param name="cancellationToken">The <c>cancellationToken</c> value.</param>
    /// <returns>The result of the operation.</returns>
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

        global::System.Collections.Generic.IReadOnlyList<global::Nodalis.Core.Glossary.GlossaryEntry> existingEntries = await LoadEntriesAsync(
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

        global::Nodalis.Infrastructure.Reliability.TextDocumentSession session = await TextDocumentSession.OpenAsync(
            scope.FilePath,
            cancellationToken);

        string existing = session.Content.TrimEnd();
        string block = FormatEntry(draft);

        string updated = string.IsNullOrWhiteSpace(existing)
            ? $"# Glossaire\n\n{block}"
            : $"{existing}\n\n{block}";

        await session.SaveAsync(
            updated,
            cancellationToken);
    }

    /// <summary>
    /// Performs the <c>Parse</c> operation.
    /// </summary>
    /// <param name="markdown">The <c>markdown</c> value.</param>
    /// <param name="scope">The <c>scope</c> value.</param>
    /// <returns>The result of the operation.</returns>
public static IReadOnlyList<GlossaryEntry> Parse(
        string markdown,
        GlossaryScope scope)
    {
        ArgumentNullException.ThrowIfNull(markdown);
        ArgumentNullException.ThrowIfNull(scope);

        string[] lines = markdown
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split('\n');

        global::System.Collections.Generic.List<global::Nodalis.Core.Glossary.GlossaryEntry> entries = new List<GlossaryEntry>();
        EntryBuilder? current = null;
        FieldKind activeField = FieldKind.None;

        for (int index = 0;
             index < lines.Length;
             index++)
        {
            string line = lines[index];

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
                    out string? definition))
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
                    out string? synonyms))
            {
                activeField = FieldKind.None;
                current.Synonyms.AddRange(
                    SplitValues(synonyms));
                continue;
            }

            if (TryReadField(
                    line,
                    "Acronymes",
                    out string? acronyms))
            {
                activeField = FieldKind.None;
                current.Acronyms.AddRange(
                    SplitValues(acronyms));
                continue;
            }

            if (TryReadField(
                    line,
                    "Synonymes / acronymes",
                    out string? legacyAliases))
            {
                activeField = FieldKind.None;
                current.Synonyms.AddRange(
                    SplitValues(legacyAliases));
                continue;
            }

            if (TryReadField(
                    line,
                    "Liens",
                    out string? links))
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

    /// <summary>
    /// Performs the <c>FormatEntry</c> operation.
    /// </summary>
    /// <param name="draft">The <c>draft</c> value.</param>
    /// <returns>The result of the operation.</returns>
public static string FormatEntry(
        GlossaryEntryDraft draft)
    {
        ArgumentNullException.ThrowIfNull(draft);

        global::System.Text.StringBuilder builder = new StringBuilder();

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

    /// <summary>
    /// Performs the <c>EnsureGlossaryFileAsync</c> operation.
    /// </summary>
    /// <param name="scope">The <c>scope</c> value.</param>
    /// <param name="cancellationToken">The <c>cancellationToken</c> value.</param>
    /// <returns>The result of the operation.</returns>
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

    /// <summary>
    /// Performs the <c>TryReadField</c> operation.
    /// </summary>
    /// <param name="line">The <c>line</c> value.</param>
    /// <param name="fieldName">The <c>fieldName</c> value.</param>
    /// <param name="value">The <c>value</c> value.</param>
    /// <returns>The result of the operation.</returns>
private static bool TryReadField(
        string line,
        string fieldName,
        out string value)
    {
        string prefix = $"**{fieldName} :**";

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

    /// <summary>
    /// Performs the <c>AddCurrentIfMeaningful</c> operation.
    /// </summary>
    /// <param name="entries">The <c>entries</c> value.</param>
    /// <param name="current">The <c>current</c> value.</param>
    /// <param name="scope">The <c>scope</c> value.</param>
    /// <returns>The result of the operation.</returns>
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

        string definition = current.Definition
            .ToString()
            .Trim();

        global::System.Collections.Generic.List<string> synonyms = NormalizeValues(
            current.Synonyms);

        global::System.Collections.Generic.List<string> acronyms = NormalizeValues(
            current.Acronyms);

        global::System.Collections.Generic.List<string> links = NormalizeValues(
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

    /// <summary>
    /// Performs the <c>AppendDefinition</c> operation.
    /// </summary>
    /// <param name="builder">The <c>builder</c> value.</param>
    /// <param name="value">The <c>value</c> value.</param>
    /// <returns>The result of the operation.</returns>
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

    /// <summary>
    /// Performs the <c>SplitValues</c> operation.
    /// </summary>
    /// <param name="value">The <c>value</c> value.</param>
    /// <returns>The result of the operation.</returns>
private static IEnumerable<string> SplitValues(
        string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return [];
        }

        char separator = value.Contains(';')
            ? ';'
            : ',';

        return value
            .Split(
                separator,
                StringSplitOptions.TrimEntries |
                StringSplitOptions.RemoveEmptyEntries);
    }

    /// <summary>
    /// Performs the <c>NormalizeValues</c> operation.
    /// </summary>
    /// <param name="values">The <c>values</c> value.</param>
    /// <returns>The result of the operation.</returns>
private static List<string> NormalizeValues(
        IEnumerable<string> values) =>
        values
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .Distinct(StringComparer.CurrentCultureIgnoreCase)
            .OrderBy(value => value, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

    /// <summary>
    /// Performs the <c>JoinValues</c> operation.
    /// </summary>
    /// <param name="values">The <c>values</c> value.</param>
    /// <returns>The result of the operation.</returns>
private static string JoinValues(
        IEnumerable<string> values) =>
        string.Join(
            "; ",
            NormalizeValues(values));

    /// <summary>
    /// Performs the <c>ResolveContextDirectory</c> operation.
    /// </summary>
    /// <param name="workspaceRoot">The <c>workspaceRoot</c> value.</param>
    /// <param name="contextPath">The <c>contextPath</c> value.</param>
    /// <returns>The result of the operation.</returns>
private static string ResolveContextDirectory(
        string workspaceRoot,
        string? contextPath)
    {
        if (string.IsNullOrWhiteSpace(contextPath))
        {
            return workspaceRoot;
        }

        string fullPath = Path.GetFullPath(
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

    /// <summary>
    /// Performs the <c>IsInsideOrEqual</c> operation.
    /// </summary>
    /// <param name="candidate">The <c>candidate</c> value.</param>
    /// <param name="root">The <c>root</c> value.</param>
    /// <returns>The result of the operation.</returns>
private static bool IsInsideOrEqual(
        string candidate,
        string root)
    {
        string fullCandidate = Path.GetFullPath(candidate)
            .TrimEnd(
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar);

        string fullRoot = Path.GetFullPath(root)
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
