using System.Text.Json;
using System.Text.Json.Nodes;
using Nodalis.Core.Backups;
using Nodalis.Core.Domain;
using Nodalis.Core.Migrations;
using Nodalis.Infrastructure.Backups;
using Nodalis.Infrastructure.Persistence;

namespace Nodalis.Infrastructure.Migrations;

public sealed class WorkspaceMigrationService
{
    private readonly string _workspaceRoot;
    private readonly IReadOnlyList<IWorkspaceMigrationStep> _steps =
    [
        new LegacyUnversionedWorkspaceMigration()
    ];

    /// <summary>
    /// Initializes a workspace migration service for one workspace root.
    /// </summary>
    /// <param name="workspaceRoot">The workspace root directory.</param>
    public WorkspaceMigrationService(string workspaceRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRoot);

        _workspaceRoot = Path.TrimEndingDirectorySeparator(
            Path.GetFullPath(workspaceRoot));
    }

    /// <summary>
    /// Computes a migration plan without changing the workspace.
    /// </summary>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The migration preflight result.</returns>
    public async Task<WorkspaceMigrationPreflight> PreflightAsync(
            CancellationToken cancellationToken = default)
    {
        global::System.Collections.Generic.List<string> errors = new List<string>();
        global::System.Collections.Generic.List<string> warnings = new List<string>();
        global::System.Collections.Generic.List<global::Nodalis.Core.Migrations.WorkspaceMigrationStepInfo> plan =
            new List<WorkspaceMigrationStepInfo>();

        int detectedVersion;

        try
        {
            detectedVersion = await ReadSchemaVersionAsync(
                _workspaceRoot,
                cancellationToken);

            if (ContainsReparsePoints(
                    _workspaceRoot,
                    cancellationToken))
            {
                errors.Add(
                    "Le workspace contient un lien symbolique ou une jonction. " +
                    "La migration transactionnelle refuse ces entrées pour éviter de copier des données hors du workspace.");
            }
        }
        catch (Exception exception) when (
            exception is IOException or
            UnauthorizedAccessException or
            JsonException or
            InvalidDataException)
        {
            errors.Add(exception.Message);
            detectedVersion = -1;
        }

        if (detectedVersion > WorkspaceManifest.CurrentSchemaVersion)
        {
            errors.Add(
                $"Le schéma {detectedVersion} est plus récent que le schéma {WorkspaceManifest.CurrentSchemaVersion} compris par cette version de Nodalis.");
        }
        else if (detectedVersion >= 0)
        {
            int plannedVersion = detectedVersion;

            while (plannedVersion < WorkspaceManifest.CurrentSchemaVersion)
            {
                IWorkspaceMigrationStep? step = _steps.FirstOrDefault(
                    candidate => candidate.Info.FromVersion == plannedVersion);

                if (step is null)
                {
                    errors.Add(
                        $"Aucune migration n'est disponible depuis le schéma {plannedVersion}.");
                    break;
                }

                if (step.Info.ToVersion <= plannedVersion)
                {
                    errors.Add(
                        $"La migration '{step.Info.Id}' ne progresse pas vers une version supérieure.");
                    break;
                }

                plan.Add(step.Info);
                plannedVersion = step.Info.ToVersion;
            }

            if (errors.Count == 0 &&
                plannedVersion != WorkspaceManifest.CurrentSchemaVersion)
            {
                errors.Add(
                    $"La chaîne de migration s'arrête au schéma {plannedVersion} au lieu du schéma {WorkspaceManifest.CurrentSchemaVersion}.");
            }
        }

        if (plan.Count > 0)
        {
            try
            {
                global::Nodalis.Infrastructure.Backups.WorkspaceBackupService backupService =
                    new WorkspaceBackupService(_workspaceRoot);
                backupService.ValidateBackupDestinationPath(
                    GetDefaultBackupDirectory());
            }
            catch (Exception exception) when (
                exception is IOException or
                UnauthorizedAccessException or
                InvalidOperationException)
            {
                errors.Add(
                    "La destination de sauvegarde de migration n'est pas utilisable : " +
                    exception.Message);
            }
        }

        bool migrationRequired =
            detectedVersion >= 0 &&
            detectedVersion != WorkspaceManifest.CurrentSchemaVersion;

        bool backupRequired = plan.Any(
            step => step.BackupPolicy == WorkspaceMigrationBackupPolicy.Required);

        return new WorkspaceMigrationPreflight
        {
            DetectedSchemaVersion = detectedVersion,
            TargetSchemaVersion = WorkspaceManifest.CurrentSchemaVersion,
            MigrationRequired = migrationRequired,
            CanMigrate = errors.Count == 0,
            BackupRequired = backupRequired,
            Steps = plan.ToArray(),
            Warnings = warnings.ToArray(),
            Errors = errors.ToArray()
        };
    }

    /// <summary>
    /// Applies the complete migration plan after explicit approval.
    /// </summary>
    /// <param name="migrationApproved">Whether the caller explicitly approved the migration.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The migration report.</returns>
    public async Task<WorkspaceMigrationReport> MigrateAsync(
            bool migrationApproved,
            CancellationToken cancellationToken = default)
    {
        global::Nodalis.Core.Migrations.WorkspaceMigrationPreflight preflight =
            await PreflightAsync(cancellationToken);

        if (!preflight.CanMigrate)
        {
            throw new InvalidDataException(
                string.Join(
                    Environment.NewLine,
                    preflight.Errors));
        }

        DateTimeOffset startedUtc = DateTimeOffset.UtcNow;

        if (!preflight.MigrationRequired)
        {
            return new WorkspaceMigrationReport
            {
                StartedUtc = startedUtc,
                CompletedUtc = DateTimeOffset.UtcNow,
                InitialSchemaVersion = preflight.DetectedSchemaVersion,
                FinalSchemaVersion = preflight.TargetSchemaVersion,
                MigrationPerformed = false,
                Succeeded = true,
                AppliedSteps = Array.Empty<WorkspaceMigrationStepInfo>(),
                Changes = Array.Empty<WorkspaceMigrationChange>()
            };
        }

        if (!migrationApproved)
        {
            throw new InvalidOperationException(
                "La migration du workspace nécessite une approbation explicite.");
        }

        string backupDirectory = GetDefaultBackupDirectory();
        global::Nodalis.Infrastructure.Backups.WorkspaceBackupService backupService =
            new WorkspaceBackupService(_workspaceRoot);
        global::Nodalis.Core.Backups.WorkspaceBackupInfo backup =
            await backupService.CreateBackupAsync(
                backupDirectory,
                retentionCount: 10,
                cancellationToken);

        string stagingDirectory = CreateSiblingWorkingPath(
            "staging");
        string reportCandidatePath = Path.Combine(
            backupDirectory,
            $"migration-{startedUtc:yyyyMMdd-HHmmssfff}-{Guid.NewGuid():N}.json");

        global::System.Collections.Generic.List<global::Nodalis.Core.Migrations.WorkspaceMigrationStepInfo> appliedSteps =
            new List<WorkspaceMigrationStepInfo>();
        global::System.Collections.Generic.List<global::Nodalis.Core.Migrations.WorkspaceMigrationChange> changes =
            new List<WorkspaceMigrationChange>();

        try
        {
            await CopyWorkspaceAsync(
                _workspaceRoot,
                stagingDirectory,
                cancellationToken);

            foreach (global::Nodalis.Core.Migrations.WorkspaceMigrationStepInfo plannedStep in preflight.Steps)
            {
                cancellationToken.ThrowIfCancellationRequested();

                IWorkspaceMigrationStep step = _steps.Single(
                    candidate => string.Equals(
                        candidate.Info.Id,
                        plannedStep.Id,
                        StringComparison.Ordinal));

                global::System.Collections.Generic.IReadOnlyList<global::Nodalis.Core.Migrations.WorkspaceMigrationChange> stepChanges =
                    await step.ApplyAsync(
                        stagingDirectory,
                        cancellationToken);

                changes.AddRange(stepChanges);
                appliedSteps.Add(plannedStep);

                int stagedVersion = await ReadSchemaVersionAsync(
                    stagingDirectory,
                    cancellationToken);

                if (stagedVersion != plannedStep.ToVersion)
                {
                    throw new InvalidDataException(
                        $"La migration '{plannedStep.Id}' devait produire le schéma {plannedStep.ToVersion}, mais le staging annonce le schéma {stagedVersion}.");
                }
            }

            int finalVersion = await ReadSchemaVersionAsync(
                stagingDirectory,
                cancellationToken);

            if (finalVersion != WorkspaceManifest.CurrentSchemaVersion)
            {
                throw new InvalidDataException(
                    $"Le staging termine au schéma {finalVersion} au lieu du schéma {WorkspaceManifest.CurrentSchemaVersion}.");
            }

            global::Nodalis.Infrastructure.Persistence.FileSystemWorkspaceStore stagedStore =
                new FileSystemWorkspaceStore(stagingDirectory);
            global::Nodalis.Core.Domain.WorkspaceManifest stagedManifest =
                await stagedStore.LoadAsync(cancellationToken);

            if (stagedManifest.SchemaVersion != WorkspaceManifest.CurrentSchemaVersion)
            {
                throw new InvalidDataException(
                    "Le manifest migré n'est pas lisible avec le schéma courant.");
            }

            CommitStagedWorkspace(
                stagingDirectory);

            global::Nodalis.Core.Migrations.WorkspaceMigrationReport successfulReport =
                new WorkspaceMigrationReport
                {
                    StartedUtc = startedUtc,
                    CompletedUtc = DateTimeOffset.UtcNow,
                    InitialSchemaVersion = preflight.DetectedSchemaVersion,
                    FinalSchemaVersion = finalVersion,
                    MigrationPerformed = true,
                    Succeeded = true,
                    AppliedSteps = appliedSteps.ToArray(),
                    Changes = changes.ToArray(),
                    BackupArchivePath = backup.ArchivePath
                };

            string? persistedReportPath = await TryWriteReportAsync(
                reportCandidatePath,
                successfulReport,
                cancellationToken);

            return successfulReport with
            {
                ReportPath = persistedReportPath
            };
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            global::Nodalis.Core.Migrations.WorkspaceMigrationReport failureReport =
                new WorkspaceMigrationReport
                {
                    StartedUtc = startedUtc,
                    CompletedUtc = DateTimeOffset.UtcNow,
                    InitialSchemaVersion = preflight.DetectedSchemaVersion,
                    FinalSchemaVersion = preflight.DetectedSchemaVersion,
                    MigrationPerformed = true,
                    Succeeded = false,
                    AppliedSteps = appliedSteps.ToArray(),
                    Changes = changes.ToArray(),
                    BackupArchivePath = backup.ArchivePath,
                    ErrorMessage = exception.Message
                };

            string? persistedFailurePath = await TryWriteReportAsync(
                reportCandidatePath,
                failureReport,
                CancellationToken.None);

            string reportText = persistedFailurePath is null
                ? "aucun rapport persistant n'a pu être écrit"
                : $"rapport : {persistedFailurePath}";

            throw new InvalidOperationException(
                "La migration a échoué avant validation complète. " +
                $"Le workspace d'origine reste récupérable depuis la sauvegarde '{backup.ArchivePath}' ({reportText}).",
                exception);
        }
        finally
        {
            if (Directory.Exists(stagingDirectory))
            {
                Directory.Delete(
                    stagingDirectory,
                    recursive: true);
            }
        }
    }

    /// <summary>
    /// Returns the default migration backup directory located outside the workspace.
    /// </summary>
    /// <returns>The normalized backup directory.</returns>
    public string GetDefaultBackupDirectory()
    {
        string? parent = Path.GetDirectoryName(
            _workspaceRoot);

        if (string.IsNullOrWhiteSpace(parent))
        {
            throw new InvalidOperationException(
                "Le workspace doit avoir un dossier parent pour créer une sauvegarde de migration.");
        }

        string workspaceName = new DirectoryInfo(
            _workspaceRoot).Name;

        if (string.IsNullOrWhiteSpace(workspaceName))
        {
            throw new InvalidOperationException(
                "Le nom du dossier du workspace ne permet pas de créer une sauvegarde de migration.");
        }

        return Path.Combine(
            parent,
            $".{workspaceName}.nodalis-migration-backups");
    }

    /// <summary>
    /// Reads the schema version from the raw workspace manifest.
    /// </summary>
    /// <param name="workspaceRoot">The workspace root.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>Zero for an unversioned legacy manifest, otherwise the explicit version.</returns>
    private static async Task<int> ReadSchemaVersionAsync(
            string workspaceRoot,
            CancellationToken cancellationToken)
    {
        string manifestPath = Path.Combine(
            workspaceRoot,
            WorkspaceLayout.WorkspaceManifestFileName);

        if (!File.Exists(manifestPath))
        {
            throw new FileNotFoundException(
                "Le dossier sélectionné ne contient pas de manifest .workspace.json.",
                manifestPath);
        }

        await using global::System.IO.FileStream stream = new FileStream(
            manifestPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 4096,
            useAsync: true);

        using JsonDocument document = await JsonDocument.ParseAsync(
            stream,
            cancellationToken: cancellationToken);

        if (document.RootElement.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidDataException(
                "Le manifest .workspace.json doit contenir un objet JSON.");
        }

        if (!document.RootElement.TryGetProperty(
                "schemaVersion",
                out JsonElement schemaElement))
        {
            return 0;
        }

        if (schemaElement.ValueKind != JsonValueKind.Number ||
            !schemaElement.TryGetInt32(out int version) ||
            version < 0)
        {
            throw new InvalidDataException(
                "La propriété schemaVersion du workspace doit être un entier positif ou nul.");
        }

        return version;
    }

    /// <summary>
    /// Copies a workspace into a sibling staging directory while preserving empty directories.
    /// </summary>
    /// <param name="sourceRoot">The source workspace.</param>
    /// <param name="destinationRoot">The staging directory.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    private static Task CopyWorkspaceAsync(
            string sourceRoot,
            string destinationRoot,
            CancellationToken cancellationToken)
    {
        return Task.Run(
            () =>
            {
                Directory.CreateDirectory(
                    destinationRoot);

                foreach (string directory in Directory.EnumerateDirectories(
                             sourceRoot,
                             "*",
                             SearchOption.AllDirectories))
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    string relativeDirectory = Path.GetRelativePath(
                        sourceRoot,
                        directory);

                    Directory.CreateDirectory(
                        Path.Combine(
                            destinationRoot,
                            relativeDirectory));
                }

                foreach (string file in Directory.EnumerateFiles(
                             sourceRoot,
                             "*",
                             SearchOption.AllDirectories))
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    string relativeFile = Path.GetRelativePath(
                        sourceRoot,
                        file);
                    string destinationFile = Path.Combine(
                        destinationRoot,
                        relativeFile);
                    string? destinationParent = Path.GetDirectoryName(
                        destinationFile);

                    if (string.IsNullOrWhiteSpace(destinationParent))
                    {
                        throw new InvalidDataException(
                            $"Le fichier '{relativeFile}' n'a pas de dossier parent valide dans le staging.");
                    }

                    Directory.CreateDirectory(
                        destinationParent);
                    File.Copy(
                        file,
                        destinationFile,
                        overwrite: false);
                }
            },
            cancellationToken);
    }

    /// <summary>
    /// Detects reparse points that could escape the workspace during a recursive copy.
    /// </summary>
    /// <param name="root">The directory to inspect.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>True when a symbolic link or junction is present.</returns>
    private static bool ContainsReparsePoints(
            string root,
            CancellationToken cancellationToken)
    {
        global::System.Collections.Generic.Stack<string> pending =
            new Stack<string>();
        pending.Push(root);

        while (pending.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();

            string current = pending.Pop();
            global::System.IO.DirectoryInfo directory =
                new DirectoryInfo(current);

            foreach (global::System.IO.FileSystemInfo entry in directory.EnumerateFileSystemInfos(
                         "*",
                         SearchOption.TopDirectoryOnly))
            {
                cancellationToken.ThrowIfCancellationRequested();

                if ((entry.Attributes & FileAttributes.ReparsePoint) != 0)
                {
                    return true;
                }

                if (entry is DirectoryInfo childDirectory)
                {
                    pending.Push(
                        childDirectory.FullName);
                }
            }
        }

        return false;
    }

    /// <summary>
    /// Replaces the original workspace only after the staged copy has been fully validated.
    /// </summary>
    /// <param name="stagingDirectory">The validated staging directory.</param>
    private void CommitStagedWorkspace(
            string stagingDirectory)
    {
        string recoveryDirectory = CreateSiblingWorkingPath(
            "recovery");

        Directory.Move(
            _workspaceRoot,
            recoveryDirectory);

        try
        {
            Directory.Move(
                stagingDirectory,
                _workspaceRoot);
        }
        catch
        {
            if (!Directory.Exists(_workspaceRoot) &&
                Directory.Exists(recoveryDirectory))
            {
                Directory.Move(
                    recoveryDirectory,
                    _workspaceRoot);
            }

            throw;
        }

        try
        {
            Directory.Delete(
                recoveryDirectory,
                recursive: true);
        }
        catch (IOException)
        {
            // The migrated workspace is already installed and a verified ZIP
            // backup exists. Keeping this recovery directory is safer than
            // turning a successful migration into a failure.
        }
        catch (UnauthorizedAccessException)
        {
            // Same rationale as above: retain the recovery copy if cleanup is
            // blocked by the operating system.
        }
    }

    /// <summary>
    /// Creates a unique sibling path used for staging or local recovery.
    /// </summary>
    /// <param name="purpose">The short purpose label.</param>
    /// <returns>The unique sibling directory path.</returns>
    private string CreateSiblingWorkingPath(
            string purpose)
    {
        string? parent = Path.GetDirectoryName(
            _workspaceRoot);

        if (string.IsNullOrWhiteSpace(parent))
        {
            throw new InvalidOperationException(
                "Le workspace doit avoir un dossier parent pour préparer une migration.");
        }

        string workspaceName = new DirectoryInfo(
            _workspaceRoot).Name;

        return Path.Combine(
            parent,
            $".{workspaceName}.nodalis-migration-{purpose}-{Guid.NewGuid():N}");
    }

    /// <summary>
    /// Persists a migration report without invalidating an otherwise successful migration.
    /// </summary>
    /// <param name="reportPath">The report path.</param>
    /// <param name="report">The report contents.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The report path when persistence succeeds, otherwise null.</returns>
    private static async Task<string?> TryWriteReportAsync(
            string reportPath,
            WorkspaceMigrationReport report,
            CancellationToken cancellationToken)
    {
        try
        {
            await AtomicJsonFile.WriteAsync(
                reportPath,
                report,
                cancellationToken);

            return reportPath;
        }
        catch (Exception exception) when (
            exception is IOException or
            UnauthorizedAccessException or
            JsonException)
        {
            return null;
        }
    }

    private interface IWorkspaceMigrationStep
    {
        WorkspaceMigrationStepInfo Info { get; }

        /// <summary>
        /// Applies one migration step to a staging workspace.
        /// </summary>
        /// <param name="workspaceRoot">The staging workspace root.</param>
        /// <param name="cancellationToken">A cancellation token.</param>
        /// <returns>The deterministic change report.</returns>
        Task<IReadOnlyList<WorkspaceMigrationChange>> ApplyAsync(
            string workspaceRoot,
            CancellationToken cancellationToken);
    }

    private sealed class LegacyUnversionedWorkspaceMigration : IWorkspaceMigrationStep
    {
        public WorkspaceMigrationStepInfo Info { get; } =
            new WorkspaceMigrationStepInfo
            {
                Id = "workspace-0-to-1",
                FromVersion = 0,
                ToVersion = 1,
                Description = "Rend explicite schemaVersion=1 dans le manifest du workspace legacy.",
                Risk = WorkspaceMigrationRisk.Low,
                BackupPolicy = WorkspaceMigrationBackupPolicy.Required
            };

        /// <summary>
        /// Adds the explicit version marker while preserving every unknown manifest property.
        /// </summary>
        /// <param name="workspaceRoot">The staging workspace root.</param>
        /// <param name="cancellationToken">A cancellation token.</param>
        /// <returns>The deterministic change report.</returns>
        public async Task<IReadOnlyList<WorkspaceMigrationChange>> ApplyAsync(
                string workspaceRoot,
                CancellationToken cancellationToken)
        {
            string manifestPath = Path.Combine(
                workspaceRoot,
                WorkspaceLayout.WorkspaceManifestFileName);
            string json = await File.ReadAllTextAsync(
                manifestPath,
                cancellationToken);
            JsonNode? parsed = JsonNode.Parse(
                json);

            if (parsed is not JsonObject manifest)
            {
                throw new InvalidDataException(
                    "Le manifest legacy doit contenir un objet JSON.");
            }

            if (manifest.TryGetPropertyValue(
                    "schemaVersion",
                    out JsonNode? existingVersion) &&
                existingVersion is not null)
            {
                int version = existingVersion.GetValue<int>();

                if (version == Info.ToVersion)
                {
                    return Array.Empty<WorkspaceMigrationChange>();
                }

                throw new InvalidDataException(
                    $"Le manifest legacy annonce déjà schemaVersion={version}; la migration {Info.Id} refuse de l'écraser.");
            }

            manifest["schemaVersion"] =
                Info.ToVersion;

            await AtomicJsonFile.WriteAsync(
                manifestPath,
                manifest,
                cancellationToken);

            return
            [
                new WorkspaceMigrationChange
                {
                    StepId = Info.Id,
                    RelativePath = WorkspaceLayout.WorkspaceManifestFileName,
                    Description = "Ajout de la propriété schemaVersion=1 sans supprimer les propriétés inconnues."
                }
            ];
        }
    }
}
