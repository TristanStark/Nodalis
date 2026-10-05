using Nodalis.Core.Abstractions;
using Nodalis.Core.Domain;
using Nodalis.Core.Templates;
using Nodalis.Infrastructure.Persistence;
using Nodalis.Infrastructure.Reliability;

namespace Nodalis.Infrastructure.Templates;

public sealed class FileSystemTemplateStore : ITemplateStore
{
    public const string TemplateCatalogFileName = "templates.json";
    public const string ProjectProfilesFileName = "project-profiles.json";

    private readonly string _templatesRoot;

    /// <summary>
    /// Initializes a new instance of <see cref="FileSystemTemplateStore"/>.
    /// </summary>
    /// <param name="workspaceRoot">The <c>workspaceRoot</c> value.</param>
    public FileSystemTemplateStore(string workspaceRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRoot);

        _templatesRoot = Path.Combine(
            Path.GetFullPath(workspaceRoot),
            WorkspaceLayout.TemplatesDirectoryName);
    }

    /// <summary>
    /// Performs the <c>InitializeDefaultsAsync</c> operation.
    /// </summary>
    /// <param name="cancellationToken">The <c>cancellationToken</c> value.</param>
    /// <returns>The result of the operation.</returns>
    public async Task InitializeDefaultsAsync(
            CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(_templatesRoot);

        await WriteJsonIfMissingAsync(
            Path.Combine(_templatesRoot, TemplateCatalogFileName),
            CreateDefaultTemplateCatalog(),
            cancellationToken);

        await WriteJsonIfMissingAsync(
            Path.Combine(_templatesRoot, ProjectProfilesFileName),
            CreateDefaultProjectProfiles(),
            cancellationToken);

        foreach (global::System.Collections.Generic.KeyValuePair<string, string> template in CreateDefaultTemplateFiles())
        {
            string path = ResolveTemplatePath(template.Key);

            if (File.Exists(path))
            {
                continue;
            }

            await AtomicFileWriter.WriteAllTextAsync(
                path,
                template.Value,
                cancellationToken);
        }
    }

    /// <summary>
    /// Performs the <c>LoadTemplateCatalogAsync</c> operation.
    /// </summary>
    /// <param name="cancellationToken">The <c>cancellationToken</c> value.</param>
    /// <returns>The result of the operation.</returns>
    public async Task<TemplateCatalog> LoadTemplateCatalogAsync(
            CancellationToken cancellationToken = default)
    {
        string path = Path.Combine(
            _templatesRoot,
            TemplateCatalogFileName);

        global::Nodalis.Core.Templates.TemplateCatalog catalog = await AtomicJsonFile.ReadAsync<TemplateCatalog>(
            path,
            cancellationToken);

        if (catalog.SchemaVersion != TemplateCatalog.CurrentSchemaVersion)
        {
            throw new InvalidDataException(
                $"Unsupported template catalog schema {catalog.SchemaVersion}.");
        }

        global::System.Linq.IGrouping<string, global::Nodalis.Core.Templates.MarkdownTemplateDefinition>? duplicateKey = catalog.Templates
            .GroupBy(
                template => template.Key,
                StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(group => group.Count() > 1);

        if (duplicateKey is not null)
        {
            throw new InvalidDataException(
                $"Duplicate template key '{duplicateKey.Key}'.");
        }

        foreach (global::Nodalis.Core.Templates.MarkdownTemplateDefinition template in catalog.Templates)
        {
            ValidateDefinition(template);
            ResolveTemplatePath(template.FileName);
        }

        return catalog;
    }

    /// <summary>
    /// Performs the <c>LoadProjectProfilesAsync</c> operation.
    /// </summary>
    /// <param name="cancellationToken">The <c>cancellationToken</c> value.</param>
    /// <returns>The result of the operation.</returns>
    public async Task<ProjectProfileCatalog> LoadProjectProfilesAsync(
            CancellationToken cancellationToken = default)
    {
        string path = Path.Combine(
            _templatesRoot,
            ProjectProfilesFileName);

        global::Nodalis.Core.Templates.ProjectProfileCatalog catalog = await AtomicJsonFile.ReadAsync<ProjectProfileCatalog>(
            path,
            cancellationToken);

        if (catalog.SchemaVersion != ProjectProfileCatalog.CurrentSchemaVersion)
        {
            throw new InvalidDataException(
                $"Unsupported project profile schema {catalog.SchemaVersion}.");
        }

        return catalog;
    }

    /// <summary>
    /// Loads the Markdown source for one configured template.
    /// </summary>
    /// <param name="templateKey">The stable template key.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The Markdown template source.</returns>
    public async Task<string> LoadTemplateContentAsync(
            string templateKey,
            CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(templateKey);

        TemplateCatalog catalog = await LoadTemplateCatalogAsync(
            cancellationToken);
        MarkdownTemplateDefinition definition = FindTemplateDefinition(
            catalog,
            templateKey);

        return await File.ReadAllTextAsync(
            ResolveTemplatePath(definition.FileName),
            cancellationToken);
    }

    /// <summary>
    /// Saves editable template metadata and Markdown content.
    /// </summary>
    /// <param name="definition">The updated template definition.</param>
    /// <param name="content">The Markdown template content.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task representing the save.</returns>
    public async Task SaveTemplateAsync(
            MarkdownTemplateDefinition definition,
            string content,
            CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(content);

        TemplateCatalog catalog = await LoadTemplateCatalogAsync(
            cancellationToken);
        MarkdownTemplateDefinition current = FindTemplateDefinition(
            catalog,
            definition.Key);

        if (!string.Equals(
                current.FileName,
                definition.FileName,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                "The template storage file cannot be renamed from the editor.");
        }

        ValidateDefinition(
            definition);

        List<MarkdownTemplateDefinition> templates =
            catalog.Templates.ToList();
        int index = templates.FindIndex(template =>
            string.Equals(
                template.Key,
                definition.Key,
                StringComparison.OrdinalIgnoreCase));

        templates[index] =
            definition;

        TemplateCatalog updatedCatalog = catalog with
        {
            Templates =
                templates
        };

        ValidateTemplateCatalog(
            updatedCatalog);

        await AtomicFileWriter.WriteAllTextAsync(
            ResolveTemplatePath(
                definition.FileName),
            content,
            cancellationToken);

        await AtomicJsonFile.WriteAsync(
            Path.Combine(
                _templatesRoot,
                TemplateCatalogFileName),
            updatedCatalog,
            cancellationToken);
    }

    /// <summary>
    /// Duplicates one configured template under a new display name.
    /// </summary>
    /// <param name="templateKey">The source template key.</param>
    /// <param name="displayName">The display name for the duplicate.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The newly created template definition.</returns>
    public async Task<MarkdownTemplateDefinition> DuplicateTemplateAsync(
            string templateKey,
            string displayName,
            CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(
            templateKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(
            displayName);

        TemplateCatalog catalog = await LoadTemplateCatalogAsync(
            cancellationToken);
        MarkdownTemplateDefinition source = FindTemplateDefinition(
            catalog,
            templateKey);

        string keyBase = CreateTemplateKeyBase(
            displayName);
        string candidateKey =
            keyBase;
        string candidateFileName =
            candidateKey + ".md";
        int suffix =
            2;

        while (catalog.Templates.Any(template =>
                   string.Equals(
                       template.Key,
                       candidateKey,
                       StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(
                       template.FileName,
                       candidateFileName,
                       StringComparison.OrdinalIgnoreCase)) ||
               File.Exists(
                   ResolveTemplatePath(
                       candidateFileName)))
        {
            candidateKey =
                $"{keyBase}-{suffix}";
            candidateFileName =
                candidateKey + ".md";
            suffix++;
        }

        MarkdownTemplateDefinition duplicate =
            new MarkdownTemplateDefinition
            {
                Key =
                    candidateKey,
                DisplayName =
                    displayName.Trim(),
                FileName =
                    candidateFileName,
                Category =
                    source.Category,
                DefaultFileName =
                    source.DefaultFileName
            };

        List<MarkdownTemplateDefinition> templates =
            catalog.Templates.ToList();
        templates.Add(
            duplicate);

        TemplateCatalog updatedCatalog = catalog with
        {
            Templates =
                templates
        };

        ValidateTemplateCatalog(
            updatedCatalog);

        string content = await File.ReadAllTextAsync(
            ResolveTemplatePath(
                source.FileName),
            cancellationToken);

        await AtomicFileWriter.WriteAllTextAsync(
            ResolveTemplatePath(
                duplicate.FileName),
            content,
            cancellationToken);

        await AtomicJsonFile.WriteAsync(
            Path.Combine(
                _templatesRoot,
                TemplateCatalogFileName),
            updatedCatalog,
            cancellationToken);

        return duplicate;
    }

    /// <summary>
    /// Restores one built-in template to its shipped metadata and Markdown.
    /// </summary>
    /// <param name="templateKey">The built-in template key.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task representing the restore.</returns>
    public async Task RestoreTemplateDefaultAsync(
            string templateKey,
            CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(
            templateKey);

        TemplateCatalog defaults =
            CreateDefaultTemplateCatalog();
        MarkdownTemplateDefinition defaultDefinition =
            defaults.Templates.SingleOrDefault(template =>
                string.Equals(
                    template.Key,
                    templateKey,
                    StringComparison.OrdinalIgnoreCase))
            ?? throw new KeyNotFoundException(
                $"Template '{templateKey}' has no built-in default.");

        IReadOnlyDictionary<string, string> defaultFiles =
            CreateDefaultTemplateFiles();

        if (!defaultFiles.TryGetValue(
                defaultDefinition.FileName,
                out string? defaultContent))
        {
            throw new InvalidDataException(
                $"Built-in template '{templateKey}' has no Markdown source.");
        }

        TemplateCatalog catalog = await LoadTemplateCatalogAsync(
            cancellationToken);
        List<MarkdownTemplateDefinition> templates =
            catalog.Templates.ToList();
        int index = templates.FindIndex(template =>
            string.Equals(
                template.Key,
                templateKey,
                StringComparison.OrdinalIgnoreCase));

        if (index >= 0)
        {
            templates[index] =
                defaultDefinition;
        }
        else
        {
            templates.Add(
                defaultDefinition);
        }

        TemplateCatalog updatedCatalog = catalog with
        {
            Templates =
                templates
        };

        ValidateTemplateCatalog(
            updatedCatalog);

        await AtomicFileWriter.WriteAllTextAsync(
            ResolveTemplatePath(
                defaultDefinition.FileName),
            defaultContent,
            cancellationToken);

        await AtomicJsonFile.WriteAsync(
            Path.Combine(
                _templatesRoot,
                TemplateCatalogFileName),
            updatedCatalog,
            cancellationToken);
    }

    /// <summary>
    /// Persists the complete project-profile catalog after validation.
    /// </summary>
    /// <param name="catalog">The project-profile catalog.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task representing the save.</returns>
    public async Task SaveProjectProfilesAsync(
            ProjectProfileCatalog catalog,
            CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(
            catalog);

        TemplateCatalog templates = await LoadTemplateCatalogAsync(
            cancellationToken);

        ValidateProjectProfileCatalog(
            catalog,
            templates);

        await AtomicJsonFile.WriteAsync(
            Path.Combine(
                _templatesRoot,
                ProjectProfilesFileName),
            catalog,
            cancellationToken);
    }

    /// <summary>
    /// Restores one built-in project profile.
    /// </summary>
    /// <param name="complexity">The profile complexity to restore.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task representing the restore.</returns>
    public async Task RestoreProjectProfileDefaultAsync(
            ProjectComplexity complexity,
            CancellationToken cancellationToken = default)
    {
        ProjectProfileCatalog catalog =
            await LoadProjectProfilesAsync(
                cancellationToken);
        ProjectProfileCatalog defaults =
            CreateDefaultProjectProfiles();
        ProjectProfileDefinition defaultProfile =
            defaults.Profiles.Single(profile =>
                profile.Complexity ==
                complexity);

        List<ProjectProfileDefinition> profiles =
            catalog.Profiles.ToList();
        int index = profiles.FindIndex(profile =>
            profile.Complexity ==
            complexity);

        if (index >= 0)
        {
            profiles[index] =
                defaultProfile;
        }
        else
        {
            profiles.Add(
                defaultProfile);
        }

        ProjectProfileCatalog updatedCatalog = catalog with
        {
            Profiles =
                profiles
        };

        await SaveProjectProfilesAsync(
            updatedCatalog,
            cancellationToken);
    }

    /// <summary>
    /// Performs the <c>RenderAsync</c> operation.
    /// </summary>
    /// <param name="templateKey">The <c>templateKey</c> value.</param>
    /// <param name="variables">The <c>variables</c> value.</param>
    /// <param name="cancellationToken">The <c>cancellationToken</c> value.</param>
    /// <returns>The result of the operation.</returns>
    public async Task<string> RenderAsync(
            string templateKey,
            IReadOnlyDictionary<string, string> variables,
            CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(templateKey);
        ArgumentNullException.ThrowIfNull(variables);

        global::Nodalis.Core.Templates.TemplateCatalog catalog = await LoadTemplateCatalogAsync(cancellationToken);

        global::Nodalis.Core.Templates.MarkdownTemplateDefinition definition = catalog.Templates.SingleOrDefault(
            template => string.Equals(
                template.Key,
                templateKey,
                StringComparison.OrdinalIgnoreCase))
            ?? throw new KeyNotFoundException(
                $"Template '{templateKey}' was not found.");

        string path = ResolveTemplatePath(definition.FileName);

        string template = await File.ReadAllTextAsync(
            path,
            cancellationToken);

        return MarkdownTemplateRenderer.Render(
            template,
            variables);
    }

    /// <summary>
    /// Finds one template definition by its stable key.
    /// </summary>
    /// <param name="catalog">The template catalog.</param>
    /// <param name="templateKey">The stable template key.</param>
    /// <returns>The matching template definition.</returns>
    private static MarkdownTemplateDefinition FindTemplateDefinition(
            TemplateCatalog catalog,
            string templateKey) =>
            catalog.Templates.SingleOrDefault(template =>
                string.Equals(
                    template.Key,
                    templateKey,
                    StringComparison.OrdinalIgnoreCase))
            ?? throw new KeyNotFoundException(
                $"Template '{templateKey}' was not found.");

    /// <summary>
    /// Validates template metadata before it is persisted.
    /// </summary>
    /// <param name="catalog">The template catalog.</param>
    private static void ValidateTemplateCatalog(
            TemplateCatalog catalog)
    {
        if (catalog.SchemaVersion !=
            TemplateCatalog.CurrentSchemaVersion)
        {
            throw new InvalidDataException(
                $"Unsupported template catalog schema {catalog.SchemaVersion}.");
        }

        global::System.Linq.IGrouping<string, MarkdownTemplateDefinition>? duplicateKey =
            catalog.Templates
                .GroupBy(
                    template => template.Key,
                    StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault(group =>
                    group.Count() > 1);

        if (duplicateKey is not null)
        {
            throw new InvalidDataException(
                $"Duplicate template key '{duplicateKey.Key}'.");
        }

        global::System.Linq.IGrouping<string, MarkdownTemplateDefinition>? duplicateFile =
            catalog.Templates
                .GroupBy(
                    template => template.FileName,
                    StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault(group =>
                    group.Count() > 1);

        if (duplicateFile is not null)
        {
            throw new InvalidDataException(
                $"Duplicate template file '{duplicateFile.Key}'.");
        }

        foreach (MarkdownTemplateDefinition template in
                 catalog.Templates)
        {
            ValidateDefinition(
                template);
        }
    }

    /// <summary>
    /// Validates the editable project-profile catalog and template references.
    /// </summary>
    /// <param name="catalog">The project-profile catalog.</param>
    /// <param name="templates">The available templates.</param>
    private static void ValidateProjectProfileCatalog(
            ProjectProfileCatalog catalog,
            TemplateCatalog templates)
    {
        if (catalog.SchemaVersion !=
            ProjectProfileCatalog.CurrentSchemaVersion)
        {
            throw new InvalidDataException(
                $"Unsupported project profile schema {catalog.SchemaVersion}.");
        }

        global::System.Linq.IGrouping<ProjectComplexity, ProjectProfileDefinition>? duplicateProfile =
            catalog.Profiles
                .GroupBy(profile =>
                    profile.Complexity)
                .FirstOrDefault(group =>
                    group.Count() > 1);

        if (duplicateProfile is not null)
        {
            throw new InvalidDataException(
                $"Duplicate project profile '{duplicateProfile.Key}'.");
        }

        foreach (ProjectComplexity complexity in
                 Enum.GetValues<ProjectComplexity>())
        {
            if (!catalog.Profiles.Any(profile =>
                    profile.Complexity ==
                    complexity))
            {
                throw new InvalidDataException(
                    $"Project profile '{complexity}' is missing.");
            }
        }

        global::System.Collections.Generic.HashSet<string> templateKeys =
            templates.Templates
                .Select(template =>
                    template.Key)
                .ToHashSet(
                    StringComparer.OrdinalIgnoreCase);

        foreach (ProjectProfileDefinition profile in
                 catalog.Profiles)
        {
            if (string.IsNullOrWhiteSpace(
                    profile.DisplayName))
            {
                throw new InvalidDataException(
                    $"Project profile '{profile.Complexity}' requires a display name.");
            }

            global::System.Linq.IGrouping<string, ProjectSectionTemplateDefinition>? duplicateSection =
                profile.Sections
                    .Where(section =>
                        !string.IsNullOrWhiteSpace(
                            section.Name))
                    .GroupBy(
                        section => section.Name.Trim(),
                        StringComparer.OrdinalIgnoreCase)
                    .FirstOrDefault(group =>
                        group.Count() > 1);

            if (duplicateSection is not null)
            {
                throw new InvalidDataException(
                    $"Project profile '{profile.DisplayName}' contains duplicate section '{duplicateSection.Key}'.");
            }

            foreach (ProjectSectionTemplateDefinition section in
                     profile.Sections)
            {
                if (string.IsNullOrWhiteSpace(
                        section.Name))
                {
                    throw new InvalidDataException(
                        $"Project profile '{profile.DisplayName}' contains an unnamed section.");
                }

                if (!string.IsNullOrWhiteSpace(
                        section.TemplateKey) &&
                    !templateKeys.Contains(
                        section.TemplateKey))
                {
                    throw new InvalidDataException(
                        $"Section '{section.Name}' references unknown template '{section.TemplateKey}'.");
                }
            }
        }
    }

    /// <summary>
    /// Builds a portable template key from a display name.
    /// </summary>
    /// <param name="displayName">The display name.</param>
    /// <returns>A lowercase key suitable for file names.</returns>
    private static string CreateTemplateKeyBase(
            string displayName)
    {
        global::System.Text.StringBuilder builder =
            new global::System.Text.StringBuilder();
        bool previousSeparator =
            false;

        foreach (char character in
                 displayName.Trim())
        {
            if (char.IsLetterOrDigit(
                    character))
            {
                builder.Append(
                    char.ToLowerInvariant(
                        character));
                previousSeparator =
                    false;
                continue;
            }

            if (previousSeparator ||
                builder.Length ==
                0)
            {
                continue;
            }

            builder.Append('-');
            previousSeparator =
                true;
        }

        string key = builder
            .ToString()
            .Trim('-');

        return key.Length == 0
            ? "template"
            : key;
    }

    /// <summary>
    /// Performs the <c>ResolveTemplatePath</c> operation.
    /// </summary>
    /// <param name="fileName">The <c>fileName</c> value.</param>
    /// <returns>The result of the operation.</returns>
    private string ResolveTemplatePath(string fileName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);

        string root = Path.GetFullPath(_templatesRoot)
            .TrimEnd(
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar);

        string fullPath = Path.GetFullPath(
            Path.Combine(root, fileName));

        string prefix = root + Path.DirectorySeparatorChar;

        if (!fullPath.StartsWith(
                prefix,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                $"Template path '{fileName}' escapes the Templates directory.");
        }

        return fullPath;
    }

    /// <summary>
    /// Performs the <c>ValidateDefinition</c> operation.
    /// </summary>
    /// <param name="template">The <c>template</c> value.</param>
    private static void ValidateDefinition(
            MarkdownTemplateDefinition template)
    {
        if (string.IsNullOrWhiteSpace(template.Key) ||
            string.IsNullOrWhiteSpace(template.DisplayName) ||
            string.IsNullOrWhiteSpace(template.FileName) ||
            string.IsNullOrWhiteSpace(template.Category) ||
            string.IsNullOrWhiteSpace(template.DefaultFileName))
        {
            throw new InvalidDataException(
                "Template definitions require key, displayName, fileName, category and defaultFileName.");
        }
    }

    /// <summary>
    /// Performs the <c>WriteJsonIfMissingAsync</c> operation.
    /// </summary>
    /// <typeparam name="T">The <c>T</c> type.</typeparam>
    /// <param name="path">The <c>path</c> value.</param>
    /// <param name="value">The <c>value</c> value.</param>
    /// <param name="cancellationToken">The <c>cancellationToken</c> value.</param>
    /// <returns>The result of the operation.</returns>
    private static Task WriteJsonIfMissingAsync<T>(
            string path,
            T value,
            CancellationToken cancellationToken)
    {
        if (File.Exists(path))
        {
            return Task.CompletedTask;
        }

        return AtomicJsonFile.WriteAsync(
            path,
            value,
            cancellationToken);
    }

    /// <summary>
    /// Performs the <c>CreateDefaultTemplateCatalog</c> operation.
    /// </summary>
    /// <returns>The result of the operation.</returns>
    private static TemplateCatalog CreateDefaultTemplateCatalog() =>
            new()
            {
                Templates =
                [
                    new MarkdownTemplateDefinition
                {
                    Key = "note",
                    DisplayName = "Note",
                    FileName = "note.md",
                    Category = "Général"
                },
                new MarkdownTemplateDefinition
                {
                    Key = "meeting",
                    DisplayName = "Compte-rendu de réunion",
                    FileName = "meeting.md",
                    Category = "Projet",
                    DefaultFileName = "{{date}} - {{title}}.md"
                },
                new MarkdownTemplateDefinition
                {
                    Key = "decision",
                    DisplayName = "Decision Record",
                    FileName = "decision.md",
                    Category = "Projet",
                    DefaultFileName = "{{date}} - {{title}}.md"
                },
                new MarkdownTemplateDefinition
                {
                    Key = "milestones",
                    DisplayName = "Jalons",
                    FileName = "milestones.md",
                    Category = "Projet"
                },
                new MarkdownTemplateDefinition
                {
                    Key = "glossary",
                    DisplayName = "Glossaire",
                    FileName = "glossary.md",
                    Category = "Connaissance"
                },
                new MarkdownTemplateDefinition
                {
                    Key = "documentation",
                    DisplayName = "Documentation projet",
                    FileName = "documentation.md",
                    Category = "Documentation"
                },
                new MarkdownTemplateDefinition
                {
                    Key = "functional",
                    DisplayName = "Documentation fonctionnelle",
                    FileName = "functional.md",
                    Category = "Documentation"
                },
                new MarkdownTemplateDefinition
                {
                    Key = "technical",
                    DisplayName = "Documentation technique",
                    FileName = "technical.md",
                    Category = "Documentation"
                },
                new MarkdownTemplateDefinition
                {
                    Key = "technical-simple",
                    DisplayName = "Technique — Simple",
                    FileName = "technical-simple.md",
                    Category = "Profils projet"
                },
                new MarkdownTemplateDefinition
                {
                    Key = "technical-medium",
                    DisplayName = "Technique — Moyen",
                    FileName = "technical-medium.md",
                    Category = "Profils projet"
                },
                new MarkdownTemplateDefinition
                {
                    Key = "technical-complex",
                    DisplayName = "Technique — Complexe",
                    FileName = "technical-complex.md",
                    Category = "Profils projet"
                },
                new MarkdownTemplateDefinition
                {
                    Key = "tests",
                    DisplayName = "Tests",
                    FileName = "tests.md",
                    Category = "Projet"
                },
                new MarkdownTemplateDefinition
                {
                    Key = "tests-simple",
                    DisplayName = "Tests — Simple",
                    FileName = "tests-simple.md",
                    Category = "Profils projet"
                },
                new MarkdownTemplateDefinition
                {
                    Key = "tests-medium",
                    DisplayName = "Tests — Moyen",
                    FileName = "tests-medium.md",
                    Category = "Profils projet"
                },
                new MarkdownTemplateDefinition
                {
                    Key = "tests-complex",
                    DisplayName = "Tests — Complexe",
                    FileName = "tests-complex.md",
                    Category = "Profils projet"
                },
                new MarkdownTemplateDefinition
                {
                    Key = "risks",
                    DisplayName = "Risques",
                    FileName = "risks.md",
                    Category = "Projet"
                },
                new MarkdownTemplateDefinition
                {
                    Key = "operations",
                    DisplayName = "Exploitation",
                    FileName = "operations.md",
                    Category = "Documentation"
                },
                new MarkdownTemplateDefinition
                {
                    Key = "deployment",
                    DisplayName = "Déploiement",
                    FileName = "deployment.md",
                    Category = "Documentation"
                },
                new MarkdownTemplateDefinition
                {
                    Key = "dependencies",
                    DisplayName = "Dépendances",
                    FileName = "dependencies.md",
                    Category = "Projet"
                }
                ]
            };

    /// <summary>
    /// Performs the <c>CreateDefaultProjectProfiles</c> operation.
    /// </summary>
    /// <returns>The result of the operation.</returns>
    private static ProjectProfileCatalog CreateDefaultProjectProfiles()
    {
        static ProjectSectionTemplateDefinition Section(
            string name,
            int order,
            string? templateKey = null,
            bool singleton = true) =>
            new()
            {
                Name = name,
                Order = order,
                IsSingleton = singleton,
                TemplateKey = templateKey
            };

        return new ProjectProfileCatalog
        {
            Profiles =
            [
                new ProjectProfileDefinition
                {
                    Complexity = ProjectComplexity.Simple,
                    DisplayName = "Simple",
                    Sections =
                    [
                        Section("Jalons", 10, "milestones"),
                        Section("Technique", 20, "technical-simple"),
                        Section("Glossaire", 30, "glossary"),
                        Section("Tests", 40, "tests-simple")
                    ]
                },
                new ProjectProfileDefinition
                {
                    Complexity = ProjectComplexity.Medium,
                    DisplayName = "Moyen",
                    Sections =
                    [
                        Section("Jalons", 10, "milestones"),
                        Section("Documentation", 20, "documentation"),
                        Section("Technique", 30, "technical-medium"),
                        Section("Glossaire", 40, "glossary"),
                        Section("Tests", 50, "tests-medium"),
                        Section("Réunions", 60, singleton: false),
                        Section("Décisions", 70, singleton: false),
                        Section("Risques", 80, "risks")
                    ]
                },
                new ProjectProfileDefinition
                {
                    Complexity = ProjectComplexity.Complex,
                    DisplayName = "Complexe",
                    Sections =
                    [
                        Section("Jalons", 10, "milestones"),
                        Section("Documentation", 20, "documentation"),
                        Section("Fonctionnel", 30, "functional"),
                        Section("Technique", 40, "technical-complex"),
                        Section("Exploitation", 50, "operations"),
                        Section("Déploiement", 60, "deployment"),
                        Section("Glossaire", 70, "glossary"),
                        Section("Tests", 80, "tests-complex"),
                        Section("Réunions", 90, singleton: false),
                        Section("Décisions", 100, singleton: false),
                        Section("Risques", 110, "risks"),
                        Section("Dépendances", 120, "dependencies")
                    ]
                }
            ]
        };
    }

    /// <summary>
    /// Performs the <c>CreateDefaultTemplateFiles</c> operation.
    /// </summary>
    /// <returns>The result of the operation.</returns>
    private static IReadOnlyDictionary<string, string>
            CreateDefaultTemplateFiles() =>
            new Dictionary<string, string>(
                StringComparer.OrdinalIgnoreCase)
            {
                ["note.md"] =
                    "# {{title}}\n\n" +
                    "_Créé le {{date}}_\n\n",
                ["meeting.md"] =
                    "# Réunion — {{title}}\n\n" +
                    "**Date :** {{date}}\n\n" +
                    "## Participants\n\n" +
                    "## Contexte\n\n" +
                    "## Ordre du jour\n\n" +
                    "## Notes\n\n" +
                    "## Décisions\n\n" +
                    "## Actions\n\n" +
                    "## Transcription IA\n\n" +
                    "## Résumé IA\n\n" +
                    "## Contenu Outlook\n\n",
                ["decision.md"] =
                    "# Décision — {{title}}\n\n" +
                    "**Date :** {{date}}\n\n" +
                    "## Contexte\n\n" +
                    "## Décision\n\n" +
                    "## Justification\n\n" +
                    "## Impacts\n\n" +
                    "## Sources et liens\n\n",
                ["milestones.md"] =
                    "# Jalons\n\n" +
                    "| Jalon | Date cible | Statut | Description | Lien |\n" +
                    "| --- | --- | --- | --- | --- |\n",
                ["glossary.md"] =
                    "# Glossaire\n\n" +
                    "## Exemple\n\n" +
                    "**Définition :** Définition du terme.\n\n" +
                    "**Synonymes :** synonyme 1; synonyme 2\n\n" +
                    "**Acronymes :** EX\n\n" +
                    "**Liens :** [[Document lié]]\n\n",
                ["documentation.md"] =
                    "# Documentation\n\n" +
                    "## Objectif\n\n" +
                    "## Périmètre\n\n" +
                    "## Références\n\n" +
                    "## Points ouverts\n\n",
                ["functional.md"] =
                    "# Fonctionnel\n\n" +
                    "## Besoin\n\n" +
                    "## Règles métier\n\n" +
                    "## Parcours / traitements\n\n" +
                    "## Données fonctionnelles\n\n" +
                    "## Interfaces\n\n" +
                    "## Cas particuliers\n\n",
                ["technical.md"] =
                    "# Technique\n\n" +
                    "## Contexte\n\n" +
                    "## Architecture / réalisation\n\n" +
                    "## Points d'attention\n\n",
                ["technical-simple.md"] =
                    "# Technique\n\n" +
                    "## Contexte\n\n" +
                    "## Réalisation\n\n" +
                    "## Configuration\n\n" +
                    "## Points d'attention\n\n",
                ["technical-medium.md"] =
                    "# Technique\n\n" +
                    "## Contexte\n\n" +
                    "## Architecture\n\n" +
                    "## Composants\n\n" +
                    "## Flux et données\n\n" +
                    "## Interfaces\n\n" +
                    "## Configuration\n\n" +
                    "## Exploitation\n\n" +
                    "## Points d'attention\n\n",
                ["technical-complex.md"] =
                    "# Technique\n\n" +
                    "## Contexte\n\n" +
                    "## Architecture\n\n" +
                    "## Composants et responsabilités\n\n" +
                    "## Flux et modèle de données\n\n" +
                    "## Interfaces / API\n\n" +
                    "## Configuration et environnements\n\n" +
                    "## Sécurité\n\n" +
                    "## Performance et capacité\n\n" +
                    "## Observabilité\n\n" +
                    "## Déploiement / rollback\n\n" +
                    "## Exploitation et reprise\n\n" +
                    "## Décisions d'architecture liées\n\n" +
                    "## Points d'attention\n\n",
                ["tests.md"] =
                    "# Tests\n\n" +
                    "## Périmètre\n\n" +
                    "## Cas de test\n\n" +
                    "## Résultats\n\n",
                ["tests-simple.md"] =
                    "# Tests\n\n" +
                    "## Périmètre\n\n" +
                    "## Cas de test\n\n" +
                    "## Résultats\n\n",
                ["tests-medium.md"] =
                    "# Tests\n\n" +
                    "## Stratégie\n\n" +
                    "## Environnements et données\n\n" +
                    "## Tests unitaires\n\n" +
                    "## Tests d'intégration\n\n" +
                    "## Tests fonctionnels / recette\n\n" +
                    "## Non-régression\n\n" +
                    "## Résultats et anomalies\n\n",
                ["tests-complex.md"] =
                    "# Tests\n\n" +
                    "## Stratégie et traçabilité\n\n" +
                    "## Environnements\n\n" +
                    "## Jeux de données\n\n" +
                    "## Tests unitaires\n\n" +
                    "## Tests de composants\n\n" +
                    "## Tests d'intégration\n\n" +
                    "## Tests système\n\n" +
                    "## Recette fonctionnelle\n\n" +
                    "## Performance / volumétrie\n\n" +
                    "## Sécurité\n\n" +
                    "## Résilience / reprise\n\n" +
                    "## Non-régression\n\n" +
                    "## Résultats et anomalies\n\n",
                ["risks.md"] =
                    "# Risques\n\n" +
                    "| Risque | Probabilité | Impact | Mitigation | Statut |\n" +
                    "| --- | --- | --- | --- | --- |\n",
                ["operations.md"] =
                    "# Exploitation\n\n" +
                    "## Supervision\n\n" +
                    "## Procédures récurrentes\n\n" +
                    "## Incidents connus\n\n" +
                    "## Sauvegarde / restauration\n\n" +
                    "## Contacts et escalade\n\n",
                ["deployment.md"] =
                    "# Déploiement\n\n" +
                    "## Prérequis\n\n" +
                    "## Procédure\n\n" +
                    "## Contrôles post-déploiement\n\n" +
                    "## Rollback\n\n" +
                    "## Validation\n\n",
                ["dependencies.md"] =
                    "# Dépendances\n\n" +
                    "| Dépendance | Type | Responsable | Version / contrainte | Impact |\n" +
                    "| --- | --- | --- | --- | --- |\n"
            };
}
