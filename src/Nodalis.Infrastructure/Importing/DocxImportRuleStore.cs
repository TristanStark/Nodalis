using Nodalis.Core.Importing;
using Nodalis.Infrastructure.Persistence;

namespace Nodalis.Infrastructure.Importing;

public sealed class DocxImportRuleStore
{
    public const string FileName = "docx-import-rules.json";

    private readonly string _path;

    /// <summary>
    /// Initializes a new instance of <see cref="DocxImportRuleStore"/>.
    /// </summary>
    /// <param name="workspaceRoot">The <c>workspaceRoot</c> value.</param>
    public DocxImportRuleStore(string workspaceRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRoot);

        _path = Path.Combine(
            Path.GetFullPath(workspaceRoot),
            WorkspaceLayout.TemplatesDirectoryName,
            FileName);
    }

    /// <summary>
    /// Performs the <c>LoadAsync</c> operation.
    /// </summary>
    /// <param name="cancellationToken">The <c>cancellationToken</c> value.</param>
    /// <returns>The result of the operation.</returns>
    public async Task<DocxImportRuleCatalog> LoadAsync(
            CancellationToken cancellationToken = default)
    {
        await EnsureDefaultAsync(
            cancellationToken);

        global::Nodalis.Core.Importing.DocxImportRuleCatalog catalog = await AtomicJsonFile.ReadAsync<DocxImportRuleCatalog>(
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

    /// <summary>
    /// Performs the <c>EnsureDefaultAsync</c> operation.
    /// </summary>
    /// <param name="cancellationToken">The <c>cancellationToken</c> value.</param>
    /// <returns>The result of the operation.</returns>
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

    /// <summary>
    /// Performs the <c>CreateDefault</c> operation.
    /// </summary>
    /// <returns>The result of the operation.</returns>
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

    /// <summary>
    /// Performs the <c>Rule</c> operation.
    /// </summary>
    /// <param name="target">The <c>target</c> value.</param>
    /// <param name="aliases">The <c>aliases</c> value.</param>
    /// <returns>The result of the operation.</returns>
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

    /// <summary>
    /// Performs the <c>Validate</c> operation.
    /// </summary>
    /// <param name="catalog">The <c>catalog</c> value.</param>
    /// <returns>The result of the operation.</returns>
    private static void Validate(
            DocxImportRuleCatalog catalog)
    {
        if (catalog.Detection.MaxContentParagraphs is < 1 or > 10_000)
        {
            throw new InvalidDataException(
                "MaxContentParagraphs doit être compris entre 1 et 10000.");
        }

        global::System.Linq.IGrouping<string, global::Nodalis.Core.Importing.DocxSectionMappingRule>? duplicateTarget = catalog.SectionMappings
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

        foreach (global::Nodalis.Core.Importing.DocxSectionMappingRule rule in catalog.SectionMappings)
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
