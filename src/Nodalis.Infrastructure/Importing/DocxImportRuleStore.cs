using Nodalis.Core.Importing;
using Nodalis.Infrastructure.Persistence;

namespace Nodalis.Infrastructure.Importing;

public sealed class DocxImportRuleStore
{
    public const string FileName = "docx-import-rules.json";

    private readonly string _path;

    public DocxImportRuleStore(string workspaceRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRoot);

        _path = Path.Combine(
            Path.GetFullPath(workspaceRoot),
            WorkspaceLayout.TemplatesDirectoryName,
            FileName);
    }

    public async Task<DocxImportRuleCatalog> LoadAsync(
        CancellationToken cancellationToken = default)
    {
        await EnsureDefaultAsync(
            cancellationToken);

        var catalog = await AtomicJsonFile.ReadAsync<DocxImportRuleCatalog>(
            _path,
            cancellationToken);

        if (catalog.SchemaVersion !=
            DocxImportRuleCatalog.CurrentSchemaVersion)
        {
            throw new InvalidDataException(
                $"Version de règles DOCX non supportée : {catalog.SchemaVersion}.");
        }

        Validate(
            catalog);

        return catalog;
    }

    private async Task EnsureDefaultAsync(
        CancellationToken cancellationToken)
    {
        if (File.Exists(_path))
        {
            return;
        }

        Directory.CreateDirectory(
            Path.GetDirectoryName(_path)!);

        await AtomicJsonFile.WriteAsync(
            _path,
            CreateDefault(),
            cancellationToken);
    }

    private static DocxImportRuleCatalog CreateDefault() =>
        new()
        {
            Detection = new DocxEntityDetectionRules
            {
                ApplicationLabels =
                [
                    "Application",
                    "Application cible",
                    "Appli"
                ],
                ProjectLabels =
                [
                    "Projet",
                    "Projet cible",
                    "Project"
                ],
                MaxContentParagraphs = 80
            },
            SectionMappings =
            [
                Rule(
                    "Technique",
                    "Documentation technique",
                    "Technique",
                    "Architecture technique",
                    "Architecture"),
                Rule(
                    "Fonctionnel",
                    "Documentation fonctionnelle",
                    "Fonctionnel",
                    "Spécifications fonctionnelles",
                    "Spécification fonctionnelle"),
                Rule(
                    "Tests",
                    "Tests",
                    "Recette",
                    "Tests / Recette",
                    "Plan de tests",
                    "Validation"),
                Rule(
                    "Jalons",
                    "Jalons",
                    "Planning",
                    "Échéances"),
                Rule(
                    "Risques",
                    "Risques",
                    "Risques et points d'attention"),
                Rule(
                    "Exploitation",
                    "Exploitation",
                    "Run",
                    "Opérations"),
                Rule(
                    "Déploiement",
                    "Déploiement",
                    "Mise en production",
                    "Installation"),
                Rule(
                    "Dépendances",
                    "Dépendances",
                    "Pré-requis",
                    "Prérequis"),
                Rule(
                    "Documentation",
                    "Documentation",
                    "Présentation",
                    "Description générale")
            ]
        };

    private static DocxSectionMappingRule Rule(
        string target,
        params string[] aliases) =>
        new()
        {
            TargetSection = target,
            HeadingAliases = aliases.ToList(),
            MaximumHeadingLevel = 3,
            AllowPrefixMatch = true
        };

    private static void Validate(
        DocxImportRuleCatalog catalog)
    {
        if (catalog.Detection.MaxContentParagraphs is < 1 or > 10_000)
        {
            throw new InvalidDataException(
                "MaxContentParagraphs doit être compris entre 1 et 10000.");
        }

        var duplicateTarget = catalog.SectionMappings
            .GroupBy(
                rule => rule.TargetSection.Trim(),
                StringComparer.CurrentCultureIgnoreCase)
            .FirstOrDefault(group =>
                group.Count() > 1);

        if (duplicateTarget is not null)
        {
            throw new InvalidDataException(
                $"Plusieurs règles DOCX ciblent la section '{duplicateTarget.Key}'.");
        }

        foreach (var rule in catalog.SectionMappings)
        {
            if (string.IsNullOrWhiteSpace(rule.TargetSection) ||
                rule.HeadingAliases.Count == 0 ||
                rule.HeadingAliases.Any(string.IsNullOrWhiteSpace))
            {
                throw new InvalidDataException(
                    "Chaque mapping DOCX doit définir une section cible et au moins un alias de titre.");
            }

            if (rule.MaximumHeadingLevel is < 1 or > 9)
            {
                throw new InvalidDataException(
                    $"Niveau de titre invalide pour '{rule.TargetSection}'.");
            }
        }
    }
}
