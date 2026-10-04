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

    public FileSystemTemplateStore(string workspaceRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRoot);

        _templatesRoot = Path.Combine(
            Path.GetFullPath(workspaceRoot),
            WorkspaceLayout.TemplatesDirectoryName);
    }

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

        foreach (var template in CreateDefaultTemplateFiles())
        {
            var path = ResolveTemplatePath(template.Key);

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

    public async Task<TemplateCatalog> LoadTemplateCatalogAsync(
        CancellationToken cancellationToken = default)
    {
        var path = Path.Combine(
            _templatesRoot,
            TemplateCatalogFileName);

        var catalog = await AtomicJsonFile.ReadAsync<TemplateCatalog>(
            path,
            cancellationToken);

        if (catalog.SchemaVersion != TemplateCatalog.CurrentSchemaVersion)
        {
            throw new InvalidDataException(
                $"Unsupported template catalog schema {catalog.SchemaVersion}.");
        }

        var duplicateKey = catalog.Templates
            .GroupBy(
                template => template.Key,
                StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(group => group.Count() > 1);

        if (duplicateKey is not null)
        {
            throw new InvalidDataException(
                $"Duplicate template key '{duplicateKey.Key}'.");
        }

        foreach (var template in catalog.Templates)
        {
            ValidateDefinition(template);
            ResolveTemplatePath(template.FileName);
        }

        return catalog;
    }

    public async Task<ProjectProfileCatalog> LoadProjectProfilesAsync(
        CancellationToken cancellationToken = default)
    {
        var path = Path.Combine(
            _templatesRoot,
            ProjectProfilesFileName);

        var catalog = await AtomicJsonFile.ReadAsync<ProjectProfileCatalog>(
            path,
            cancellationToken);

        if (catalog.SchemaVersion != ProjectProfileCatalog.CurrentSchemaVersion)
        {
            throw new InvalidDataException(
                $"Unsupported project profile schema {catalog.SchemaVersion}.");
        }

        return catalog;
    }

    public async Task<string> RenderAsync(
        string templateKey,
        IReadOnlyDictionary<string, string> variables,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(templateKey);
        ArgumentNullException.ThrowIfNull(variables);

        var catalog = await LoadTemplateCatalogAsync(cancellationToken);

        var definition = catalog.Templates.SingleOrDefault(
            template => string.Equals(
                template.Key,
                templateKey,
                StringComparison.OrdinalIgnoreCase))
            ?? throw new KeyNotFoundException(
                $"Template '{templateKey}' was not found.");

        var path = ResolveTemplatePath(definition.FileName);

        var template = await File.ReadAllTextAsync(
            path,
            cancellationToken);

        return MarkdownTemplateRenderer.Render(
            template,
            variables);
    }

    private string ResolveTemplatePath(string fileName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);

        var root = Path.GetFullPath(_templatesRoot)
            .TrimEnd(
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar);

        var fullPath = Path.GetFullPath(
            Path.Combine(root, fileName));

        var prefix = root + Path.DirectorySeparatorChar;

        if (!fullPath.StartsWith(
                prefix,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                $"Template path '{fileName}' escapes the Templates directory.");
        }

        return fullPath;
    }

    private static void ValidateDefinition(
        MarkdownTemplateDefinition template)
    {
        if (string.IsNullOrWhiteSpace(template.Key) ||
            string.IsNullOrWhiteSpace(template.DisplayName) ||
            string.IsNullOrWhiteSpace(template.FileName))
        {
            throw new InvalidDataException(
                "Template definitions require key, displayName and fileName.");
        }
    }

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
                    Category = "Projet"
                },
                new MarkdownTemplateDefinition
                {
                    Key = "decision",
                    DisplayName = "Decision Record",
                    FileName = "decision.md",
                    Category = "Projet"
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
                    Key = "technical",
                    DisplayName = "Documentation technique",
                    FileName = "technical.md",
                    Category = "Documentation"
                },
                new MarkdownTemplateDefinition
                {
                    Key = "tests",
                    DisplayName = "Tests",
                    FileName = "tests.md",
                    Category = "Projet"
                }
            ]
        };

    private static ProjectProfileCatalog CreateDefaultProjectProfiles()
    {
        static List<ProjectSectionTemplateDefinition> MinimumSections() =>
        [
            new ProjectSectionTemplateDefinition
            {
                Name = "Jalons",
                Order = 10,
                IsSingleton = true,
                TemplateKey = "milestones"
            },
            new ProjectSectionTemplateDefinition
            {
                Name = "Technique",
                Order = 20,
                IsSingleton = true,
                TemplateKey = "technical"
            },
            new ProjectSectionTemplateDefinition
            {
                Name = "Glossaire",
                Order = 30,
                IsSingleton = true,
                TemplateKey = "glossary"
            },
            new ProjectSectionTemplateDefinition
            {
                Name = "Tests",
                Order = 40,
                IsSingleton = true,
                TemplateKey = "tests"
            }
        ];

        return new ProjectProfileCatalog
        {
            Profiles =
            [
                new ProjectProfileDefinition
                {
                    Complexity = ProjectComplexity.Simple,
                    DisplayName = "Simple",
                    Sections = MinimumSections()
                },
                new ProjectProfileDefinition
                {
                    Complexity = ProjectComplexity.Medium,
                    DisplayName = "Moyen",
                    Sections = MinimumSections()
                },
                new ProjectProfileDefinition
                {
                    Complexity = ProjectComplexity.Complex,
                    DisplayName = "Complexe",
                    Sections = MinimumSections()
                }
            ]
        };
    }

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
                "## Résumé IA\n\n",
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
                "| Jalon | Date cible | Statut | Lien |\n" +
                "| --- | --- | --- | --- |\n",
            ["glossary.md"] =
                "# Glossaire\n\n" +
                "## Exemple\n\n" +
                "**Définition :**\n\n" +
                "**Synonymes / acronymes :**\n\n",
            ["technical.md"] =
                "# Technique\n\n" +
                "## Contexte\n\n" +
                "## Architecture / réalisation\n\n" +
                "## Points d'attention\n\n",
            ["tests.md"] =
                "# Tests\n\n" +
                "## Périmètre\n\n" +
                "## Cas de test\n\n" +
                "## Résultats\n\n"
        };
}
