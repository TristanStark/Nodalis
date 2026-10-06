using Nodalis.Core.Domain;

namespace Nodalis.Core.Compatibility;

public enum WorkspaceAccessMode
{
    ReadWrite,
    MigrationRequired,
    ReadOnlyFallback,
    Unsupported
}

public sealed record WorkspaceCompatibilityDecision
{
    public required int WorkspaceSchemaVersion { get; init; }

    public required int ApplicationSchemaVersion { get; init; }

    public required WorkspaceAccessMode AccessMode { get; init; }

    public required bool CanRead { get; init; }

    public required bool CanWrite { get; init; }

    public required bool CanMigrate { get; init; }

    public required string Message { get; init; }

    public required string RecommendedAction { get; init; }
}

public static class WorkspaceCompatibilityPolicy
{
    public const int LegacyUnversionedSchemaVersion = 0;

    /// <summary>
    /// Evaluates one workspace schema against the application schema.
    /// </summary>
    /// <param name="workspaceSchemaVersion">The detected workspace schema.</param>
    /// <returns>The deterministic compatibility decision.</returns>
    public static WorkspaceCompatibilityDecision Evaluate(
            int workspaceSchemaVersion)
    {
        if (workspaceSchemaVersion < LegacyUnversionedSchemaVersion)
        {
            return Unsupported(
                workspaceSchemaVersion,
                "La version de schéma du workspace est invalide.",
                "Vérifiez ou restaurez le manifest .workspace.json.");
        }

        if (workspaceSchemaVersion == WorkspaceManifest.CurrentSchemaVersion)
        {
            return new WorkspaceCompatibilityDecision
            {
                WorkspaceSchemaVersion = workspaceSchemaVersion,
                ApplicationSchemaVersion = WorkspaceManifest.CurrentSchemaVersion,
                AccessMode = WorkspaceAccessMode.ReadWrite,
                CanRead = true,
                CanWrite = true,
                CanMigrate = false,
                Message = "Le workspace utilise le schéma courant.",
                RecommendedAction = "Ouvrir normalement."
            };
        }

        if (workspaceSchemaVersion == LegacyUnversionedSchemaVersion &&
            WorkspaceManifest.CurrentSchemaVersion > LegacyUnversionedSchemaVersion)
        {
            return new WorkspaceCompatibilityDecision
            {
                WorkspaceSchemaVersion = workspaceSchemaVersion,
                ApplicationSchemaVersion = WorkspaceManifest.CurrentSchemaVersion,
                AccessMode = WorkspaceAccessMode.MigrationRequired,
                CanRead = true,
                CanWrite = false,
                CanMigrate = true,
                Message = "Le workspace utilise un schéma legacy connu qui doit être migré avant toute écriture.",
                RecommendedAction = "Migrer après sauvegarde, ou ouvrir le contenu Markdown en lecture seule."
            };
        }

        if (workspaceSchemaVersion > WorkspaceManifest.CurrentSchemaVersion)
        {
            return new WorkspaceCompatibilityDecision
            {
                WorkspaceSchemaVersion = workspaceSchemaVersion,
                ApplicationSchemaVersion = WorkspaceManifest.CurrentSchemaVersion,
                AccessMode = WorkspaceAccessMode.ReadOnlyFallback,
                CanRead = true,
                CanWrite = false,
                CanMigrate = false,
                Message =
                    "Le workspace provient d'une version plus récente de Nodalis. " +
                    "Ses métadonnées ne seront pas interprétées par cette version.",
                RecommendedAction =
                    "Utiliser la lecture seule de secours pour consulter les fichiers Markdown, " +
                    "ou installer une version de Nodalis compatible avec ce schéma."
            };
        }

        return Unsupported(
            workspaceSchemaVersion,
            "Aucune chaîne de compatibilité n'est déclarée pour ce schéma ancien.",
            "Ouvrez le workspace avec une version intermédiaire de Nodalis ou restaurez une sauvegarde compatible.");
    }

    /// <summary>
    /// Creates a deterministic unsupported compatibility decision.
    /// </summary>
    /// <param name="workspaceSchemaVersion">The detected schema.</param>
    /// <param name="message">The problem description.</param>
    /// <param name="recommendedAction">The actionable recovery guidance.</param>
    /// <returns>The unsupported decision.</returns>
    private static WorkspaceCompatibilityDecision Unsupported(
            int workspaceSchemaVersion,
            string message,
            string recommendedAction) =>
        new WorkspaceCompatibilityDecision
        {
            WorkspaceSchemaVersion = workspaceSchemaVersion,
            ApplicationSchemaVersion = WorkspaceManifest.CurrentSchemaVersion,
            AccessMode = WorkspaceAccessMode.Unsupported,
            CanRead = false,
            CanWrite = false,
            CanMigrate = false,
            Message = message,
            RecommendedAction = recommendedAction
        };
}
