using Nodalis.Core.Maintenance;
using Nodalis.Core.Settings;
using Nodalis.Infrastructure.Maintenance;
using Nodalis.Infrastructure.Reliability;
using System.Text.Json;

/// <summary>Positive/negative regression fixtures for conservative offline recurrence hypotheses.</summary>
internal static class MaintenanceRecurrenceSmokeTests
{
    /// <summary>Runs deterministic matching, false-positive, persistence and conflict checks.</summary>
    /// <param name="root">Isolated temporary workspace path.</param>
    /// <returns>Task for disk-based safety regression.</returns>
    public static async Task RunAsync(string root)
    {
        string directory = Path.Combine(root, "recurrence-fixtures");
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "Fiche de Maintenance.md");
        string unrelatedPath = Path.Combine(directory, "Other.md");

        MaintenanceRecord a = Incident("API bloquée après saturation disque",
            "APP-42", "latence lecture disque saturé timeout", "disque, production");
        MaintenanceRecord b = Incident("Blocage API suite à saturation",
            "app-42", "lecture disque saturé timeout", "disque, production");
        MaintenanceRecord sameReferenceOnly = Incident("Certificat TLS expiré",
            "APP-42", "handshake SSL certificat invalide", "securite");
        MaintenanceRecord textOnly = Incident("API bloquée après saturation disque",
            "", "", "");
        MaintenanceRecord tagsA = Incident("Base données lente transaction bloquée",
            "", "", "base, production");
        MaintenanceRecord tagsB = Incident("Base données lente transaction bloquée",
            "", "", "base, production");
        MaintenanceRecord unrelated = Incident("Erreur de session graphique",
            "", "écran déconnecté résolution réduite", "visuel");

        string markdown = "# Fiche de Maintenance\r\n\r\n## Mes notes\r\nTexte libre conservé.\r\n\r\n";
        foreach (MaintenanceRecord item in new[]
                 {
                     a, b, sameReferenceOnly, textOnly, tagsA, tagsB, unrelated
                 })
        {
            markdown = MaintenanceMarkdownCodec.Append(markdown, item);
        }

        markdown += "\r\n## Annexes\r\nMarkdown utilisateur inchangé.\r\n";
        await File.WriteAllTextAsync(path, markdown);

        IReadOnlyList<MaintenanceHistoryItem> rows =
            MaintenanceHistoryIndex.Parse(path, "Projet Démo", markdown);
        IReadOnlyList<MaintenanceRecurrenceSuggestion> suggestions =
            MaintenanceRecurrenceMatcher.Find(rows);

        string[] positives =
        [
            Pair(a.Id, b.Id),
            Pair(tagsA.Id, tagsB.Id)
        ];
        string[] actual = suggestions.Select(item =>
            Pair(item.First.Id!, item.Second.Id!)).ToArray();
        int truePositives = actual.Count(positives.Contains);
        int falsePositives = actual.Length - truePositives;
        int falseNegatives = positives.Length - truePositives;
        Assert(truePositives == 2 && falsePositives == 0 && falseNegatives == 0,
            $"Fixture precision failure: TP={truePositives}, FP={falsePositives}, FN={falseNegatives}.");
        Assert(suggestions.All(item => item.Score >= MaintenanceRecurrenceMatcher.MinimumScore &&
               !string.IsNullOrWhiteSpace(item.Explanation)),
            "Every candidate must show an explicit threshold and deterministic explanation.");
        Assert(MaintenanceRecurrenceMatcher.Find(rows).Select(item => item.PairKey)
                .SequenceEqual(suggestions.Select(item => item.PairKey)),
            "Rebuilding from the same Markdown must produce the same order and candidate pairs.");

        // Explicit reference alone and summary-only similarity must never suffice.
        Assert(!suggestions.Any(item => item.First.Id == sameReferenceOnly.Id ||
                                         item.Second.Id == sameReferenceOnly.Id ||
                                         item.First.Id == textOnly.Id ||
                                         item.Second.Id == textOnly.Id),
            "A shared project reference alone or merely similar text must not imply recurrence.");

        string otherMarkdown = MaintenanceMarkdownCodec.Append(
            "# Autre projet\n\n", a with
            {
                Id = MaintenanceMarkdownCodec.NewId(MaintenanceRecordKind.Incident)
            });
        IReadOnlyList<MaintenanceHistoryItem> other =
            MaintenanceHistoryIndex.Parse(unrelatedPath, "Autre projet", otherMarkdown);
        Assert(MaintenanceRecurrenceMatcher.Find(rows.Concat(other)).Count == suggestions.Count,
            "Matching must not conflate identical symptoms from different project files.");

        WorkspaceMaintenanceService service = new WorkspaceMaintenanceService();
        MaintenanceRecurrenceSuggestion linked = suggestions.Single(item =>
            Pair(item.First.Id!, item.Second.Id!) == Pair(a.Id, b.Id));
        string before = await File.ReadAllTextAsync(path);
        string saved = await service.ReviewIncidentsAsync(
            linked.First, linked.Second, MaintenanceRecurrenceDecision.Link);
        Assert(saved.Contains("Récurrences liées", StringComparison.Ordinal) &&
               saved.Contains("Texte libre conservé.", StringComparison.Ordinal) &&
               saved.Contains("Markdown utilisateur inchangé.", StringComparison.Ordinal),
            "Link must edit only a structured metadata bullet and preserve unrelated Markdown.");
        Assert(MaintenanceMarkdownCodec.Parse(saved).Records.Count == 7 &&
               MaintenanceMarkdownCodec.Parse(saved).Diagnostics.Count == 0,
            "A link must never merge, delete, close or invalidate any incident.");

        IReadOnlyList<MaintenanceHistoryItem> linkedRows =
            MaintenanceHistoryIndex.Parse(path, "Projet Démo", saved);
        Assert(MaintenanceRecurrenceMatcher.Find(linkedRows).Count == 1,
            "A reviewed link must not be suggested again after a fresh Markdown index.");

        // Immutable snapshot from the dialog must never overwrite an externally changed pair.
        bool refused = false;
        try
        {
            await service.ReviewIncidentsAsync(
                linked.First, linked.Second, MaintenanceRecurrenceDecision.Ignore);
        }
        catch (InvalidDataException)
        {
            refused = true;
        }

        Assert(refused && saved == await File.ReadAllTextAsync(path),
            "A stale comparison must be rejected without modifying the source.");

        MaintenanceRecurrenceSuggestion toIgnore = MaintenanceRecurrenceMatcher
            .Find(linkedRows).Single();
        string ignored = await service.ReviewIncidentsAsync(
            toIgnore.First, toIgnore.Second, MaintenanceRecurrenceDecision.Ignore);
        Assert(ignored.Contains("Récurrences ignorées", StringComparison.Ordinal) &&
               MaintenanceRecurrenceMatcher.Find(
                   MaintenanceHistoryIndex.Parse(path, "Projet Démo", ignored)).Count == 0,
            "Ignored pairs must disappear while maintenance remains enabled.");

        // Decision reversal is an explicit Markdown-only change, with stable IDs.
        MaintenanceHistoryItem[] current = MaintenanceHistoryIndex.Parse(
            path, "Projet Démo", ignored).Where(item =>
                item.Id == toIgnore.First.Id || item.Id == toIgnore.Second.Id).ToArray();
        string reversed = await service.ReviewIncidentsAsync(
            current.Single(item => item.Id == toIgnore.First.Id),
            current.Single(item => item.Id == toIgnore.Second.Id),
            MaintenanceRecurrenceDecision.Link);
        Assert(!reversed.Contains("- **Récurrences ignorées :** " +
                toIgnore.Second.Id, StringComparison.OrdinalIgnoreCase) &&
               reversed.Contains(toIgnore.Second.Id!, StringComparison.OrdinalIgnoreCase) &&
               MaintenanceMarkdownCodec.Parse(reversed).Records.Count == 7,
            "Changing a human decision must retain all original incident records.");

        // The atomic session must also reject filesystem modifications during a write.
        TextDocumentSession stale = await TextDocumentSession.OpenAsync(path);
        await File.AppendAllTextAsync(path, "\r\nAjout externe\r\n");
        refused = false;
        try
        {
            await stale.SaveAsync(before);
        }
        catch (ExternalModificationException)
        {
            refused = true;
        }

        Assert(refused && (await File.ReadAllTextAsync(path)).Contains(
            "Ajout externe", StringComparison.Ordinal),
            "Concurrent external edits must not be silently overwritten.");

        UserPreferences disabled = UserPreferences.Default with
        {
            ShowMaintenanceSuggestions = false
        };
        UserPreferences? restored = JsonSerializer.Deserialize<UserPreferences>(
            JsonSerializer.Serialize(disabled));
        Assert(UserPreferences.Default.ShowMaintenanceSuggestions &&
               restored is { ShowMaintenanceSuggestions: false },
            "Suggestion visibility must persist independently from maintenance features.");
    }

    /// <summary>Creates one incident using only Markdown-contract fields.</summary>
    /// <param name="summary">Human incident summary.</param>
    /// <param name="reference">Explicit project/equipment reference.</param>
    /// <param name="symptoms">Observed symptoms.</param>
    /// <param name="tags">Comma-separated tags.</param>
    /// <returns>A structured incident.</returns>
    private static MaintenanceRecord Incident(
        string summary, string reference, string symptoms, string tags)
    {
        Dictionary<string, string> details = new Dictionary<string, string>();
        if (!string.IsNullOrEmpty(reference))
        {
            details["Référence"] = reference;
        }

        if (!string.IsNullOrEmpty(symptoms))
        {
            details["Symptômes"] = symptoms;
        }

        if (!string.IsNullOrEmpty(tags))
        {
            details["Tags"] = tags;
        }

        return new MaintenanceRecord
        {
            Kind = MaintenanceRecordKind.Incident,
            Id = MaintenanceMarkdownCodec.NewId(MaintenanceRecordKind.Incident),
            Date = new DateOnly(2026, 10, 17),
            Summary = summary,
            Details = details
        };
    }

    /// <summary>Creates a symmetric deterministic key for comparing two incident IDs.</summary>
    /// <param name="first">First ID.</param>
    /// <param name="second">Second ID.</param>
    /// <returns>Canonical pair key.</returns>
    private static string Pair(string first, string second)
    {
        return string.Compare(first, second, StringComparison.OrdinalIgnoreCase) <= 0
            ? first + "|" + second : second + "|" + first;
    }

    /// <summary>Raises a regression failure with a stable explanation.</summary>
    /// <param name="condition">Expected property.</param>
    /// <param name="message">Failure explanation.</param>
    private static void Assert(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
