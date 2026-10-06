using System.Text.Json;
using Nodalis.Core.Domain;
using Nodalis.Core.Migrations;
using Nodalis.Infrastructure.Migrations;
using Nodalis.Infrastructure.Persistence;

internal static class WorkspaceMigrationSmokeTests
{
    /// <summary>
    /// Runs migration smoke tests for every workspace schema accepted by the migration framework.
    /// </summary>
    /// <param name="root">The main smoke-test workspace root.</param>
    public static async Task RunAsync(
            string root)
    {
        string legacyRoot =
            root + "-MigrationLegacy";
        string newerRoot =
            root + "-MigrationNewer";

        try
        {
            await VerifyLegacyZeroToOneAsync(
                legacyRoot);
            await VerifyNewerSchemaIsRejectedAsync(
                newerRoot);
        }
        finally
        {
            DeleteDirectoryIfPresent(
                legacyRoot);
            DeleteDirectoryIfPresent(
                newerRoot);
            DeleteDirectoryIfPresent(
                GetMigrationBackupDirectory(
                    legacyRoot));
            DeleteDirectoryIfPresent(
                GetMigrationBackupDirectory(
                    newerRoot));
        }
    }

    /// <summary>
    /// Verifies preflight, explicit approval, backup, migration, reporting and idempotence for schema 0.
    /// </summary>
    /// <param name="legacyRoot">The legacy workspace root.</param>
    private static async Task VerifyLegacyZeroToOneAsync(
            string legacyRoot)
    {
        Directory.CreateDirectory(
            legacyRoot);

        Guid workspaceId = Guid.NewGuid();
        string manifestPath = Path.Combine(
            legacyRoot,
            WorkspaceLayout.WorkspaceManifestFileName);
        string sentinelPath = Path.Combine(
            legacyRoot,
            "legacy-note.md");

        string legacyManifest =
            "{\n" +
            $"  \"id\": \"{workspaceId:D}\",\n" +
            "  \"name\": \"Legacy Workspace\",\n" +
            "  \"customLegacyProperty\": \"preserve-me\",\n" +
            $"  \"createdUtc\": \"{DateTimeOffset.UtcNow:O}\"\n" +
            "}\n";

        await File.WriteAllTextAsync(
            manifestPath,
            legacyManifest);
        await File.WriteAllTextAsync(
            sentinelPath,
            "# Legacy\n\nNe pas perdre ce contenu.\n");

        global::Nodalis.Infrastructure.Migrations.WorkspaceMigrationService service =
            new WorkspaceMigrationService(
                legacyRoot);

        global::Nodalis.Core.Migrations.WorkspaceMigrationPreflight preflight =
            await service.PreflightAsync();

        Assert(
            preflight.CanMigrate &&
            preflight.MigrationRequired &&
            preflight.BackupRequired &&
            preflight.DetectedSchemaVersion == 0 &&
            preflight.TargetSchemaVersion == WorkspaceManifest.CurrentSchemaVersion &&
            preflight.Steps.Count == 1 &&
            preflight.Steps[0].FromVersion == 0 &&
            preflight.Steps[0].ToVersion == 1,
            "Legacy schema 0 must produce one explicit, backup-protected migration step.");

        await AssertThrowsAsync<InvalidOperationException>(
            async () =>
            {
                await service.MigrateAsync(
                    migrationApproved: false);
            },
            "A required migration must never run without explicit approval.");

        global::Nodalis.Core.Migrations.WorkspaceMigrationReport report =
            await service.MigrateAsync(
                migrationApproved: true);

        Assert(
            report.Succeeded &&
            report.MigrationPerformed &&
            report.InitialSchemaVersion == 0 &&
            report.FinalSchemaVersion == WorkspaceManifest.CurrentSchemaVersion &&
            report.AppliedSteps.Count == 1 &&
            report.Changes.Any(change =>
                change.RelativePath == WorkspaceLayout.WorkspaceManifestFileName),
            "The schema 0 to 1 migration must expose the applied step and deterministic changes.");

        Assert(
            report.BackupArchivePath is not null &&
            File.Exists(report.BackupArchivePath),
            "A verified backup must exist before the migrated workspace replaces the original.");

        Assert(
            report.ReportPath is not null &&
            File.Exists(report.ReportPath),
            "A successful migration must persist a machine-readable report.");

        string migratedJson = await File.ReadAllTextAsync(
            manifestPath);

        using JsonDocument migratedDocument =
            JsonDocument.Parse(
                migratedJson);

        Assert(
            migratedDocument.RootElement.GetProperty(
                "schemaVersion").GetInt32() == 1 &&
            migratedDocument.RootElement.GetProperty(
                "customLegacyProperty").GetString() == "preserve-me",
            "Migration must add schemaVersion without dropping unknown legacy properties.");

        Assert(
            (await File.ReadAllTextAsync(
                sentinelPath)).Contains(
                "Ne pas perdre ce contenu.",
                StringComparison.Ordinal),
            "Migration must preserve ordinary workspace content.");

        global::Nodalis.Infrastructure.Persistence.FileSystemWorkspaceStore store =
            new FileSystemWorkspaceStore(
                legacyRoot);
        global::Nodalis.Core.Domain.WorkspaceManifest migrated =
            await store.LoadAsync();

        Assert(
            migrated.Id == workspaceId &&
            migrated.SchemaVersion == WorkspaceManifest.CurrentSchemaVersion,
            "The migrated workspace must load normally with its original identity.");

        global::Nodalis.Core.Migrations.WorkspaceMigrationReport secondPass =
            await service.MigrateAsync(
                migrationApproved: false);

        Assert(
            secondPass.Succeeded &&
            !secondPass.MigrationPerformed &&
            secondPass.AppliedSteps.Count == 0 &&
            secondPass.Changes.Count == 0,
            "Running the migration service again must be idempotent.");
    }

    /// <summary>
    /// Verifies that a workspace from a future schema is rejected during preflight.
    /// </summary>
    /// <param name="newerRoot">The future-schema workspace root.</param>
    private static async Task VerifyNewerSchemaIsRejectedAsync(
            string newerRoot)
    {
        Directory.CreateDirectory(
            newerRoot);

        string manifestPath = Path.Combine(
            newerRoot,
            WorkspaceLayout.WorkspaceManifestFileName);

        string newerManifest =
            "{\n" +
            $"  \"id\": \"{Guid.NewGuid():D}\",\n" +
            "  \"name\": \"Future Workspace\",\n" +
            $"  \"schemaVersion\": {WorkspaceManifest.CurrentSchemaVersion + 1},\n" +
            $"  \"createdUtc\": \"{DateTimeOffset.UtcNow:O}\"\n" +
            "}\n";

        await File.WriteAllTextAsync(
            manifestPath,
            newerManifest);

        global::Nodalis.Infrastructure.Migrations.WorkspaceMigrationService service =
            new WorkspaceMigrationService(
                newerRoot);
        global::Nodalis.Core.Migrations.WorkspaceMigrationPreflight preflight =
            await service.PreflightAsync();

        Assert(
            !preflight.CanMigrate &&
            preflight.MigrationRequired &&
            preflight.Errors.Count > 0,
            "A workspace from a newer schema must be refused without any write.");
    }

    /// <summary>
    /// Computes the deterministic migration backup directory used by the service.
    /// </summary>
    /// <param name="workspaceRoot">The workspace root.</param>
    /// <returns>The backup directory.</returns>
    private static string GetMigrationBackupDirectory(
            string workspaceRoot)
    {
        global::Nodalis.Infrastructure.Migrations.WorkspaceMigrationService service =
            new WorkspaceMigrationService(
                workspaceRoot);

        return service.GetDefaultBackupDirectory();
    }

    /// <summary>
    /// Deletes a directory when it exists.
    /// </summary>
    /// <param name="path">The directory path.</param>
    private static void DeleteDirectoryIfPresent(
            string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(
                path,
                recursive: true);
        }
    }

    /// <summary>
    /// Throws when a smoke-test condition is false.
    /// </summary>
    /// <param name="condition">The condition to validate.</param>
    /// <param name="message">The failure message.</param>
    private static void Assert(
            bool condition,
            string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(
                message);
        }
    }

    /// <summary>
    /// Verifies that an asynchronous operation throws the expected exception type.
    /// </summary>
    /// <typeparam name="TException">The expected exception type.</typeparam>
    /// <param name="action">The asynchronous operation.</param>
    /// <param name="message">The failure message.</param>
    private static async Task AssertThrowsAsync<TException>(
            Func<Task> action,
            string message)
        where TException : Exception
    {
        try
        {
            await action();
        }
        catch (TException)
        {
            return;
        }

        throw new InvalidOperationException(
            message);
    }
}
