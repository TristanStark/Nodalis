using Nodalis.Core.Backups;
using Nodalis.Core.Contracts;
using Nodalis.Core.Domain;
using Nodalis.Core.Links;
using Nodalis.Core.Settings;

internal static class PersistenceContractSmokeTests
{
    /// <summary>
    /// Verifies the frozen 1.0 persistence contract registry and authority classifications.
    /// </summary>
    public static void Run()
    {
        IReadOnlyList<PersistenceContractDefinition> contracts =
            PersistenceContractCatalog.Contracts;

        Assert(
            PersistenceContractCatalog.StableWorkspaceSchemaVersion ==
                1 &&
            WorkspaceManifest.CurrentSchemaVersion ==
                PersistenceContractCatalog.StableWorkspaceSchemaVersion,
            "The 1.0 contract must freeze workspace schema version 1.");

        Assert(
            contracts
                .Select(contract =>
                    contract.Key)
                .Distinct(
                    StringComparer.Ordinal)
                .Count() ==
            contracts.Count,
            "Persistence contract keys must be unique.");

        PersistenceContractCategory[] requiredCategories =
        [
            PersistenceContractCategory.CanonicalBusinessData,
            PersistenceContractCategory.IdentityRegistry,
            PersistenceContractCategory.DerivedCache,
            PersistenceContractCategory.RecoveryData,
            PersistenceContractCategory.UserPreference
        ];

        foreach (PersistenceContractCategory category in requiredCategories)
        {
            Assert(
                contracts.Any(contract =>
                    contract.Category ==
                    category),
                "Every 1.0 persistence authority category must be represented.");
        }

        PersistenceContractDefinition projectGlossary =
            contracts.Single(contract =>
                contract.Key ==
                "project-glossary");
        PersistenceContractDefinition milestones =
            contracts.Single(contract =>
                contract.Key ==
                "markdown-milestones");
        PersistenceContractDefinition targetRegistry =
            contracts.Single(contract =>
                contract.Key ==
                "link-target-registry");
        PersistenceContractDefinition referenceIndex =
            contracts.Single(contract =>
                contract.Key ==
                "link-reference-index");
        PersistenceContractDefinition relationIndex =
            contracts.Single(contract =>
                contract.Key ==
                "relation-index");
        PersistenceContractDefinition preferences =
            contracts.Single(contract =>
                contract.Key ==
                "user-preferences");
        PersistenceContractDefinition blockIdentity =
            contracts.Single(contract => contract.Key == "markdown-block-identity");
        PersistenceContractDefinition blockIndex =
            contracts.Single(contract => contract.Key == "block-identity-index");
        PersistenceContractDefinition blockIssues =
            contracts.Single(contract => contract.Key == "block-issue-index");

        Assert(
            projectGlossary.Location ==
                "**/Glossaire.md" &&
            milestones.Location ==
                "**/Jalons.md#milestone-table",
            "Jalons and project glossary contracts must remain root Markdown documents, not section directories.");

        Assert(
            LinkIndexCatalog.CurrentSchemaVersion ==
                5 &&
            WorkspaceBackupManifest.CurrentSchemaVersion ==
                1 &&
            UserPreferences.CurrentSchemaVersion ==
                1,
            "The explicit 2.0 source-block projection advances only the link index to v5, leaving workspace schema 1 intact.");

        Assert(
            targetRegistry.Category ==
                PersistenceContractCategory.IdentityRegistry &&
            !targetRegistry.Rebuildable &&
            targetRegistry.IndependentSchemaVersion ==
                LinkIndexCatalog.CurrentSchemaVersion,
            "Document target IDs and aliases must be classified as persistent identity state, not disposable cache.");

        Assert(
            blockIdentity.Category == PersistenceContractCategory.CanonicalBusinessData &&
            !blockIdentity.Rebuildable &&
            blockIndex.Category == PersistenceContractCategory.DerivedCache &&
            blockIndex.Rebuildable &&
            blockIndex.IndependentSchemaVersion == LinkIndexCatalog.CurrentSchemaVersion &&
            blockIssues.Category == PersistenceContractCategory.DerivedCache &&
            blockIssues.Rebuildable,
            "Version 5 preserves canonical block markers and rebuildable derived block projections.");

        Assert(
            referenceIndex.Category ==
                PersistenceContractCategory.DerivedCache &&
            referenceIndex.Rebuildable &&
            relationIndex.Category ==
                PersistenceContractCategory.DerivedCache &&
            relationIndex.Rebuildable,
            "Reference and relation indexes must remain explicitly rebuildable from canonical Markdown.");

        Assert(
            preferences.Category ==
                PersistenceContractCategory.UserPreference &&
            !preferences.GovernedByWorkspaceSchema &&
            preferences.IndependentSchemaVersion ==
                UserPreferences.CurrentSchemaVersion,
            "User preferences must remain separate from the workspace business-data schema.");

        Assert(
            contracts
                .Where(contract =>
                    contract.Category ==
                    PersistenceContractCategory.CanonicalBusinessData)
                .All(contract =>
                    contract.GovernedByWorkspaceSchema),
            "Canonical workspace business contracts must be governed by the workspace schema envelope.");
    }

    /// <summary>
    /// Throws when a persistence-contract smoke-test condition fails.
    /// </summary>
    /// <param name="condition">Condition to verify.</param>
    /// <param name="message">Failure message.</param>
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
}
