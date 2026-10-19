using Nodalis.Core.Importing;
using Nodalis.Infrastructure.Importing;

internal static class DocxMappingProfilesSmokeTests
{
    /// <summary>
    /// Verifies deterministic mapping, manual precedence, conflict handling and atomic profile persistence.
    /// </summary>
    public static async Task RunAsync(string root)
    {
        DocxMappingProfileRule matchTests = new DocxMappingProfileRule
        {
            Id = Guid.NewGuid(),
            HeadingText = "Tests",
            HeadingLevel = 1,
            Action = DocxMappingAction.Map,
            TargetSection = "Tests"
        };
        DocxMappingProfileRule matchRecette = new DocxMappingProfileRule
        {
            Id = Guid.NewGuid(),
            HeadingText = "Recette",
            Action = DocxMappingAction.Map,
            TargetSection = "Recette"
        };
        DocxMappingProfile profile = new DocxMappingProfile
        {
            Id = Guid.NewGuid(),
            Name = "Suivi de projet",
            Rules = [matchTests, matchRecette]
        };
        DocxMappingSourceSection source = new DocxMappingSourceSection
        {
            Heading = " Tésts ",
            HeadingLevel = 1
        };
        DocxMappingDecision matching = DocxMappingProfileEngine.Evaluate(profile, source);
        Assert(matching.Matched && !matching.HasConflict &&
               matching.TargetSection == "Tests" && matching.MatchingRuleIds.Count == 1,
            "French diacritic normalization must reproduce the same mapping.");

        DocxMappingProfileRule opposite = matchTests with
        {
            Id = Guid.NewGuid(),
            Action = DocxMappingAction.Ignore,
            TargetSection = null
        };
        DocxMappingProfile conflict = profile with
        {
            Rules = [matchTests, opposite, matchRecette]
        };
        DocxMappingDecision ambiguous = DocxMappingProfileEngine.Evaluate(conflict, source);
        Assert(ambiguous.HasConflict && ambiguous.Action is null,
            "Conflicting rules must not silently pick the first destination.");

        DocxMappingProfile resolved = DocxMappingProfileEngine.SaveOverride(
            conflict, source, DocxMappingAction.Map, "Technique");
        DocxMappingDecision overrideResult = DocxMappingProfileEngine.Evaluate(resolved, source);
        Assert(!overrideResult.HasConflict && overrideResult.IsManualOverride &&
               overrideResult.TargetSection == "Technique",
            "Explicitly saved manual overrides have priority over conflicting rules.");

        DocxMappingProfile disabled = profile with
        {
            Rules = [matchTests with { Enabled = false }, matchRecette]
        };
        Assert(!DocxMappingProfileEngine.Evaluate(disabled, source).Matched,
            "Disabled rules may not run.");

        bool badRuleRejected = false;
        try
        {
            DocxMappingProfileEngine.Validate(profile with
            {
                Rules =
                [
                    matchTests with
                    {
                        TargetSection = "../secrets"
                    }
                ]
            });
        }
        catch (InvalidDataException)
        {
            badRuleRejected = true;
        }

        Assert(badRuleRejected, "Traversal in profile destinations must be refused.");

        string workspace = Path.Combine(root, "docx-profile-smoke");
        Directory.CreateDirectory(workspace);
        DocxMappingProfileStore store = new DocxMappingProfileStore(workspace);
        DocxMappingProfileCatalog empty = await store.LoadAsync();
        Assert(empty.Profiles.Count == 0 &&
               !File.Exists(Path.Combine(workspace, "Templates",
                   DocxMappingProfileStore.FileName)),
            "Reading an absent profile catalog must not write to the workspace.");

        DocxMappingProfileCatalog catalog = new DocxMappingProfileCatalog
        {
            Profiles = [resolved]
        };
        await store.SaveAsync(catalog);
        DocxMappingProfileCatalog reopened = await store.LoadAsync();
        Assert(reopened.Profiles.Single().ManualOverrides.Count == 1 &&
               DocxMappingProfileEngine.Evaluate(reopened.Profiles.Single(), source)
                   .TargetSection == "Technique",
            "Profile rules and overrides must survive round-trip to local JSON.");

        bool invalidCatalogRejected = false;
        try
        {
            await store.SaveAsync(catalog with { SchemaVersion = 999 });
        }
        catch (InvalidDataException)
        {
            invalidCatalogRejected = true;
        }

        Assert(invalidCatalogRejected && (await store.LoadAsync()).Profiles.Count == 1,
            "A rejected catalog write must retain the previously valid data.");

        string export = Path.Combine(root, "docx-profile-smoke-export.json");
        await DocxMappingProfileStore.ExportProfileAsync(export, resolved);
        DocxMappingProfile imported = await DocxMappingProfileStore.ImportProfileAsync(export);
        Assert(imported.Id == resolved.Id && imported.ManualOverrides.Count == 1,
            "Portable JSON import/export must preserve a validated profile.");
    }

    /// <summary>Throws when a test invariant is broken.</summary>
    private static void Assert(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
