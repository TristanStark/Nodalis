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
