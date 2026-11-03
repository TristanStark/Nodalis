using System.Text.RegularExpressions;
using Nodalis.Core.Projects;
using Nodalis.Infrastructure.AI;
using Nodalis.Infrastructure.Projects;

/// <summary>Regression of all 31 original user prompts and distinct FRT rules.</summary>
internal static class ProjectFrt100PromptSmokeTests
{
    /// <summary>Checks exact source extraction, catalog and separate channels.</summary>
    /// <param name="workspace">Synthetic workspace, never a real project.</param>
    public static async Task RunAsync(string workspace)
    {
        string root = Path.Combine(AppContext.BaseDirectory, "Prompts");
        FileSystemAiPromptStore store = new(root);
        IReadOnlyList<LocalAiPromptDescriptor> catalog = await store.ListAsync();
        LocalAiPromptDescriptor[] entries = catalog.Where(
            item => item.IsFrt100).ToArray();
        Assert(entries.Length == 31 &&
            entries.Select(item => item.Id).Distinct().Count() == 31 &&
            !catalog.Any(item => item.Id == "frt100-system-v1"),
            "Exactly 30 plus one combined user prompt must be selectable.");

        ProjectJourneyTemplateCatalog defaults =
            ProjectJourneyDefaults.CreateCatalog();
        ProjectJourneyTemplate complex = defaults.Templates.Single(item =>
            item.Key == "complex-v1");
        Assert(complex.Steps.Count == 9 &&
            complex.Steps.SelectMany(item => item.Operations)
                .All(item => item.PromptKey is string key &&
                    entries.Any(template => template.Id == key)) &&
            complex.Steps.Single(item => item.Key == "exigences")
                .Operations[0].PromptKey == "frt100-01" &&
            complex.Steps.Single(item => item.Key == "conception")
                .Operations[0].PromptKey == "frt100-11",
            "New Complex journeys must propose official prompts by step " +
            "without modifying an existing project or requiring an LLM.");

        string original = await File.ReadAllTextAsync(Path.Combine(
            root, "collections", "Prompts_Gestion_Projet_FRT100.md"));
        MatchCollection sections = Regex.Matches(original.Replace(
            "\r\n", "\n", StringComparison.Ordinal),
            @"(?m)^# (?:[1-9]|[12][0-9]|30)\. .*|^# Prompt combiné — .*");
        string normalized = original.Replace("\r\n", "\n",
            StringComparison.Ordinal);
        Assert(sections.Count == 31,
            "The user's unmodified original has 30 plus one combined prompt.");
        for (int index = 0; index < sections.Count; index++)
        {
            string id = index == 30
                ? "frt100-combined"
                : "frt100-" + (index + 1).ToString("D2");
            int from = sections[index].Index;
            int to = index + 1 < sections.Count
                ? sections[index + 1].Index : normalized.Length;
            string section = normalized[from..to];
            if (index + 1 < sections.Count)
            {
                section = Regex.Replace(section, @"\n---\s*\n\s*$", "\n");
            }

            section = section.TrimEnd('\n') + "\n";
            string saved = await store.LoadAsync(id);
            Assert(string.Equals(saved.Replace("\r\n", "\n",
                StringComparison.Ordinal), section, StringComparison.Ordinal),
                "An FRT-100 original user instruction was altered: " + id);
        }

        string system = await store.LoadAsync("frt100-system-v1");
        Assert(system.Contains("# FRT-100", StringComparison.Ordinal) &&
            system.Contains("FRT-LLM-01", StringComparison.Ordinal),
            "The original SYSTEM specification must remain separate.");

        ProjectJourneyOperation operation = new()
        {
            Id = Guid.NewGuid(),
            Name = "Analyser un cahier fictif",
            PromptKey = "frt100-01"
        };
        ProjectJourneyStep step = new()
        {
            Id = Guid.NewGuid(),
            Name = "Exigences",
            Order = 10,
            Operations = [operation]
        };
        ProjectJourneyPromptPreparationService service = new(
            workspace, root);
        ProjectJourneyPromptDraft draft = await service.PrepareAsync(
            step, operation);
        Assert(draft.SystemPrompt == system &&
               draft.Content.Contains(await store.LoadAsync("frt100-01"),
                   StringComparison.Ordinal) &&
               !draft.Content.Contains("FRT-LLM-01",
                   StringComparison.Ordinal) &&
               draft.IncludedSources == 0,
            "System and user prompts must never be silently concatenated.");

        Assert(FileSystemAiPromptStore.IsFrt100UserPromptId("frt100-01") &&
               FileSystemAiPromptStore.IsFrt100UserPromptId("frt100-30") &&
               FileSystemAiPromptStore.IsFrt100UserPromptId("frt100-combined") &&
               !FileSystemAiPromptStore.IsFrt100UserPromptId(
                   "frt100-system-v1") &&
               !FileSystemAiPromptStore.IsFrt100UserPromptId(
                   "frt100-fake"),
            "FRT role must be restricted to the official 31 user keys.");

        IReadOnlyList<string> pending =
            ProjectJourneyPromptVariables.FindPlaceholders(
                "<DOCUMENT> <SOURCE> <DOCUMENT> <CONTEXTE> <!-- html -->");
        Assert(pending.SequenceEqual(new[]
               {
                   "<DOCUMENT>", "<SOURCE>", "<CONTEXTE>"
               }) &&
               draft.Warnings.Any(item => item.Contains(
                   "<DOCUMENT>", StringComparison.Ordinal)),
            "Unresolved user variables must remain explicit, deduplicated " +
            "and never be silently filled from arbitrary sources.");

        // A synthetic Complex project exercises prompt #11 from two
        // explicitly selected sources; no real workspace is touched.
        string evidenceDirectory = Path.Combine(workspace,
            "PromptBindingsSynthetic");
        Directory.CreateDirectory(evidenceDirectory);
        string architecture = Path.Combine(evidenceDirectory,
            "Architecture.md");
        await File.WriteAllTextAsync(architecture,
            "# Architecture\n" +
            "## Solution\nUne solution documentée A.\n" +
            "```md\n## Faux titre en code\n```\n" +
            "## Solution\nUne autre solution B.\n" +
            "# Hors section\nCe texte ne doit pas être importé.\n");
        string requirements = Path.Combine(evidenceDirectory,
            "Exigences.md");
        await File.WriteAllTextAsync(requirements,
            "# Exigences\n## Besoin\nBesoin vérifiable fictif.\n");
        ProjectJourneySourceService linked = new(workspace);
        string architectureReference =
            linked.GetReferenceForExistingFile(architecture);
        string requirementsReference =
            linked.GetReferenceForExistingFile(requirements);
        ProjectJourneyPromptSectionService sectionReader = new(workspace);
        IReadOnlyList<ProjectJourneyPromptSection> headings =
            await sectionReader.ListAsync(architectureReference);
        Assert(headings.Count == 4 &&
            headings.Count(item => item.Title == "Solution") == 2 &&
            headings.All(item => item.Title != "Faux titre en code"),
            "Selection must recognize actual headings and ignore code fences.");
        ProjectJourneyPromptSection selectedSection =
            headings.First(item => item.Title == "Solution");
        ProjectJourneyPromptSection requirementsSection =
            (await sectionReader.ListAsync(requirementsReference)).Single(
                item => item.Title == "Besoin");
        ProjectJourneyOperation operation11 = operation with
        {
            PromptKey = "frt100-11",
            SourcePaths = [architectureReference, requirementsReference]
        };
        ProjectJourneyStep stage11 = step with
        {
            Operations = [operation11]
        };
        ProjectJourneyPromptVariableBinding[] selections =
        [
            new("<SOLUTION>", architectureReference,
                selectedSection.StartLine, selectedSection.Title, null),
            new("<SOLUTION>", requirementsReference,
                requirementsSection.StartLine, requirementsSection.Title, null)
        ];
        ProjectJourneyPromptDraft contextual =
            await service.PrepareWithBindingsAsync(
                stage11, operation11, selections);
        Assert(contextual.IncludedSources == 2 &&
            contextual.SystemPrompt == system &&
            contextual.Content.Contains("Une solution documentée A.",
                StringComparison.Ordinal) &&
            contextual.Content.Contains("Besoin vérifiable fictif.",
                StringComparison.Ordinal) &&
            !contextual.Content.Contains(
                "Une autre solution B.", StringComparison.Ordinal) &&
            !contextual.Content.Contains(
                "Ce texte ne doit pas être importé.", StringComparison.Ordinal) &&
            contextual.Content.Contains("SHA-256 de l'extrait",
                StringComparison.Ordinal) &&
            !contextual.Content.Contains("<SOLUTION>",
                StringComparison.Ordinal),
            "Prompt 11 must combine only the two human-selected sections " +
            "with separate provenance, never other linked document sections.");

        ProjectJourneyPromptDraft noSelection =
            await service.PrepareWithBindingsAsync(
                stage11, operation11, []);
        Assert(noSelection.IncludedSources == 0 &&
            noSelection.Content.Contains("<SOLUTION>",
                StringComparison.Ordinal) &&
            !noSelection.Content.Contains("Une solution documentée",
                StringComparison.Ordinal),
            "An unbound operation must never leak a linked document.");
        ProjectJourneyPromptDraft missing =
            await service.PrepareWithBindingsAsync(
                stage11, operation11,
                [new("<SOLUTION>", architectureReference,
                    selectedSection.StartLine + 1,
                    selectedSection.Title, null)]);
        Assert(missing.IncludedSources == 0 &&
            missing.Warnings.Any(item => item.Contains("déplacée",
                StringComparison.Ordinal)) &&
            missing.Content.Contains("<SOLUTION>", StringComparison.Ordinal),
            "Moving a selected heading must fail closed with a warning.");
        ProjectJourneyPromptDraft manual =
            await service.PrepareWithBindingsAsync(
                stage11, operation11,
                [new("<SOLUTION>", null, null, null,
                    "Saisie explicitement validée.")]);
        Assert(manual.IncludedSources == 0 &&
            manual.Content.Contains("Saisie explicitement validée.",
                StringComparison.Ordinal),
            "Text entered manually cannot read other source files.");
        bool unlinkedRejected = false;
        try
        {
            await service.PrepareWithBindingsAsync(stage11, operation11,
                [new("<SOLUTION>", "HorsDossier/Secret.md",
                    null, null, null)]);
        }
        catch (InvalidDataException)
        {
            unlinkedRejected = true;
        }

        Assert(unlinkedRejected,
            "Unlinked workspace paths must be refused before any file read.");
        Assert(ProjectJourneyPromptVariables.ReplaceBoundVariables(
                "<DOCUMENT> <CONTEXTE>", new Dictionary<string, string>
                {
                    ["<DOCUMENT>"] = "Une source contenant <CONTEXTE>"
                }) == "Une source contenant <CONTEXTE> <CONTEXTE>",
            "Replacing selected variables must never recursively parse source text.");

        ProjectJourneyOperation other = operation with
        {
            PromptKey = null
        };
        ProjectJourneyStep otherStep = step with { Operations = [other] };
        ProjectJourneyPromptDraft plain = await service.PrepareAsync(
            otherStep, other);
        Assert(plain.SystemPrompt is null,
            "Default draft must not pretend to enforce FRT-100.");
    }

    /// <summary>Fails on regression in synthetic prompt usage.</summary>
    private static void Assert(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
