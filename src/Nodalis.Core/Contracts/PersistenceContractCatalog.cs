using Nodalis.Core.Backups;
using Nodalis.Core.Domain;
using Nodalis.Core.Links;
using Nodalis.Core.Settings;

namespace Nodalis.Core.Contracts;

/// <summary>
/// Classifies persisted Nodalis data by authority and lifecycle.
/// </summary>
public enum PersistenceContractCategory
{
    /// <summary>Human or structural business data that is authoritative for the workspace.</summary>
    CanonicalBusinessData,

    /// <summary>Technical identity state that must be preserved even when surrounding index fields are derived.</summary>
    IdentityRegistry,

    /// <summary>Rebuildable data derived from canonical workspace content.</summary>
    DerivedCache,

    /// <summary>Recovery metadata or snapshots used by trash and backup workflows.</summary>
    RecoveryData,

    /// <summary>Per-user application state that is not part of workspace business data.</summary>
    UserPreference
}

/// <summary>
/// Describes one stable 1.0 persistence contract or a separately governed persisted sub-contract.
/// </summary>
public sealed record PersistenceContractDefinition
{
    /// <summary>Gets the stable contract key used by documentation and tests.</summary>
    public required string Key { get; init; }

    /// <summary>Gets the file location or logical location of the persisted contract.</summary>
    public required string Location { get; init; }

    /// <summary>Gets the authority/lifecycle classification.</summary>
    public required PersistenceContractCategory Category { get; init; }

    /// <summary>Gets an independently stored schema version, when the contract has one.</summary>
    public int? IndependentSchemaVersion { get; init; }

    /// <summary>Gets whether the workspace schema version governs compatibility for this contract.</summary>
    public bool GovernedByWorkspaceSchema { get; init; }

    /// <summary>Gets whether the persisted data may be reconstructed without losing authoritative information.</summary>
    public bool Rebuildable { get; init; }

    /// <summary>Gets the stable identity and rename invariant for this contract.</summary>
    public required string IdentityAndRenameInvariant { get; init; }
}

/// <summary>
/// Central registry for the persistence contracts frozen for the Nodalis 1.0 data format.
/// </summary>
public static class PersistenceContractCatalog
{
    /// <summary>
    /// Gets the workspace schema version frozen by the 1.0 contract definition.
    /// </summary>
    public const int StableWorkspaceSchemaVersion =
        WorkspaceManifest.CurrentSchemaVersion;

    /// <summary>
    /// Gets every persisted contract that forms or supports the 1.0 workspace format.
    /// </summary>
    public static IReadOnlyList<PersistenceContractDefinition> Contracts { get; } =
    [
        new PersistenceContractDefinition
        {
            Key = "workspace-manifest",
            Location = ".workspace.json",
            Category = PersistenceContractCategory.CanonicalBusinessData,
            IndependentSchemaVersion = WorkspaceManifest.CurrentSchemaVersion,
            GovernedByWorkspaceSchema = true,
            Rebuildable = false,
            IdentityAndRenameInvariant =
                "Workspace Id is created once and remains stable. Renaming changes Name, not Id."
        },
        new PersistenceContractDefinition
        {
            Key = "application-manifest",
            Location = "Applications/*/.application.json",
            Category = PersistenceContractCategory.CanonicalBusinessData,
            GovernedByWorkspaceSchema = true,
            Rebuildable = false,
            IdentityAndRenameInvariant =
                "Application Id is immutable. Folder/display renames must preserve the manifest Id."
        },
        new PersistenceContractDefinition
        {
            Key = "module-manifest",
            Location = "Applications/**/Modules/*/.module.json",
            Category = PersistenceContractCategory.CanonicalBusinessData,
            GovernedByWorkspaceSchema = true,
            Rebuildable = false,
            IdentityAndRenameInvariant =
                "Module Id is immutable; ApplicationId and ParentModuleId express stable ownership, independent of names."
        },
        new PersistenceContractDefinition
        {
            Key = "project-manifest",
            Location = "Applications/**/Projets/*/.project.json",
            Category = PersistenceContractCategory.CanonicalBusinessData,
            GovernedByWorkspaceSchema = true,
            Rebuildable = false,
            IdentityAndRenameInvariant =
                "Project Id is immutable; ApplicationId, ModuleId and ParentProjectId express ownership independent of paths."
        },
        new PersistenceContractDefinition
        {
            Key = "project-sections",
            Location = ".project.json#/sections",
            Category = PersistenceContractCategory.CanonicalBusinessData,
            GovernedByWorkspaceSchema = true,
            Rebuildable = false,
            IdentityAndRenameInvariant =
                "Section Id is immutable. Name and ordering may change without replacing the section identity."
        },
        new PersistenceContractDefinition
        {
            Key = "markdown-documents",
            Location = "**/*.md",
            Category = PersistenceContractCategory.CanonicalBusinessData,
            GovernedByWorkspaceSchema = true,
            Rebuildable = false,
            IdentityAndRenameInvariant =
                "Markdown text is authoritative. Document technical identity is retained by the link target registry across recognized moves/renames."
        },
        new PersistenceContractDefinition
        {
            Key = "markdown-front-matter",
            Location = "**/*.md#front-matter",
            Category = PersistenceContractCategory.CanonicalBusinessData,
            GovernedByWorkspaceSchema = true,
            Rebuildable = false,
            IdentityAndRenameInvariant =
                "Properties are part of the Markdown document; unknown keys are preserved and no property is required for validity."
        },
        new PersistenceContractDefinition
        {
            Key = "markdown-tasks",
            Location = "**/*.md#checkbox-tasks",
            Category = PersistenceContractCategory.CanonicalBusinessData,
            GovernedByWorkspaceSchema = true,
            Rebuildable = false,
            IdentityAndRenameInvariant =
                "Checkbox state and inline metadata are authoritative. Parsed task IDs are derived occurrence identifiers and are not a separate persistence source."
        },
        new PersistenceContractDefinition
        {
            Key = "markdown-milestones",
            Location = "**/Jalons/*.md#milestone-table",
            Category = PersistenceContractCategory.CanonicalBusinessData,
            GovernedByWorkspaceSchema = true,
            Rebuildable = false,
            IdentityAndRenameInvariant =
                "Milestone rows and their readable dependency references are authoritative in Markdown; parsed view models are derived."
        },
        new PersistenceContractDefinition
        {
            Key = "markdown-decisions",
            Location = "**/*.md#decision-format",
            Category = PersistenceContractCategory.CanonicalBusinessData,
            GovernedByWorkspaceSchema = true,
            Rebuildable = false,
            IdentityAndRenameInvariant =
                "Decision Markdown is authoritative; lifecycle and source references must survive file renames through link resolution or explicit rewrite."
        },
        new PersistenceContractDefinition
        {
            Key = "markdown-relations",
            Location = "**/*.md#typed-relations",
            Category = PersistenceContractCategory.CanonicalBusinessData,
            GovernedByWorkspaceSchema = true,
            Rebuildable = false,
            IdentityAndRenameInvariant =
                "The stored target GUID is authoritative for relation identity; the human label is descriptive and may be rewritten after a rename."
        },
        new PersistenceContractDefinition
        {
            Key = "link-target-registry",
            Location = ".nodalis-links.json#/targets",
            Category = PersistenceContractCategory.IdentityRegistry,
            IndependentSchemaVersion = LinkIndexCatalog.CurrentSchemaVersion,
            GovernedByWorkspaceSchema = false,
            Rebuildable = false,
            IdentityAndRenameInvariant =
                "Target Id and Aliases preserve technical identity across recognized renames/moves. Deleting this registry can reassign document IDs and lose aliases."
        },
        new PersistenceContractDefinition
        {
            Key = "link-reference-index",
            Location = ".nodalis-links.json#/references",
            Category = PersistenceContractCategory.DerivedCache,
            IndependentSchemaVersion = LinkIndexCatalog.CurrentSchemaVersion,
            GovernedByWorkspaceSchema = false,
            Rebuildable = true,
            IdentityAndRenameInvariant =
                "References are rebuilt from Markdown links and never override Markdown source text."
        },
        new PersistenceContractDefinition
        {
            Key = "relation-index",
            Location = ".nodalis-links.json#/relations",
            Category = PersistenceContractCategory.DerivedCache,
            IndependentSchemaVersion = LinkIndexCatalog.CurrentSchemaVersion,
            GovernedByWorkspaceSchema = false,
            Rebuildable = true,
            IdentityAndRenameInvariant =
                "Indexed relation rows are rebuilt from typed relation lines in Markdown and are never authoritative."
        },
        new PersistenceContractDefinition
        {
            Key = "trash-entry",
            Location = "Corbeille/*/.trash.json",
            Category = PersistenceContractCategory.RecoveryData,
            GovernedByWorkspaceSchema = true,
            Rebuildable = false,
            IdentityAndRenameInvariant =
                "EntryId identifies the trash operation; ItemId preserves the deleted entity identity when available; restore targets OriginalRelativePath."
        },
        new PersistenceContractDefinition
        {
            Key = "backup-archive",
            Location = "Nodalis-<workspaceId>-<timestamp>.zip#/.nodalis-backup.json",
            Category = PersistenceContractCategory.RecoveryData,
            IndependentSchemaVersion = WorkspaceBackupManifest.CurrentSchemaVersion,
            GovernedByWorkspaceSchema = false,
            Rebuildable = false,
            IdentityAndRenameInvariant =
                "WorkspaceId must match the archived workspace; WorkspaceSchemaVersion records the format of the captured canonical data."
        },
        new PersistenceContractDefinition
        {
            Key = "user-preferences",
            Location = "%LOCALAPPDATA%/Nodalis/preferences.json",
            Category = PersistenceContractCategory.UserPreference,
            IndependentSchemaVersion = UserPreferences.CurrentSchemaVersion,
            GovernedByWorkspaceSchema = false,
            Rebuildable = false,
            IdentityAndRenameInvariant =
                "Preferences are local to the user/machine and never define workspace business identity."
        }
    ];
}
