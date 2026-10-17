using Nodalis.Core.Maintenance;

/// <summary>Regression coverage for tolerant maintenance history and fast in-memory filtering.</summary>
internal static class MaintenanceHistorySmokeTests
{
    /// <summary>Runs mixed historical, linked, partial, filter and scale tests.</summary>
    public static void Run()
    {
        string incidentId = MaintenanceMarkdownCodec.NewId(MaintenanceRecordKind.Incident);
        string interventionId = MaintenanceMarkdownCodec.NewId(MaintenanceRecordKind.Intervention);
        string markdown = "# Fiche de Maintenance\r\n\r\n" +
            "~~~markdown\r\n## Historique de maintenance\r\n### Incident\r\n~~~\r\n\r\n" +
            "## Historique de maintenance\r\n\r\n" +
            "### Incident\r\n" +
            "- **Identifiant :** " + incidentId + "\r\n" +
            "- **Date :** 2026-10-01\r\n" +
            "- **Résumé :** Échec de l'API\r\n" +
            "- **Statut :** Ouvert\r\n" +
            "- **Catégorie :** Applicatif\r\n" +
            "- **Tags :** api, prod\r\n" +
            "\r\n### Intervention\r\n" +
            "- **Identifiant :** " + interventionId + "\r\n" +
            "- **Date :** 2026-10-03\r\n" +
            "- **Résumé :** Redémarrage API\r\n" +
            "- **Incident :** " + incidentId + "\r\n" +
            "- **Action :** Relancer le service\r\n" +
            "- **Résultat :** Service rétabli\r\n" +
            "- **Statut :** Terminé\r\n" +
            "- **Catégorie :** Applicatif\r\n" +
            "- **Tags :** api, correction\r\n" +
            "\r\n### Intervention\r\n" +
            "- **Résumé :** Tentative ancienne sans date\r\n" +
            "- **Action :** Redémarrage historique\r\n" +
            "~~~markdown\r\n" +
            "- **Date :** 2000-01-01\r\n" +
            "- **Identifiant :** INT-00000000000000000000000000000000\r\n" +
            "~~~\r\n" +
            "\r\n### Incident\r\n" +
            "- **Résumé :** Incident sans métadonnées complètes\r\n" +
            "\r\n## Mes procédures\r\n" +
            "Autres notes libres\r\n";

        IReadOnlyList<MaintenanceHistoryItem> entries =
            MaintenanceHistoryIndex.Parse("C:/Projets/Fiche de Maintenance.md", "Projet Test", markdown);
        Assert(entries.Count == 4, "All real incident/intervention headings must be discovered.");
        Assert(entries.Count(e => e.Kind == MaintenanceRecordKind.Incident) == 2 &&
               entries.Count(e => e.Kind == MaintenanceRecordKind.Intervention) == 2,
            "Incident and intervention types must be separate.");
        Assert(entries.Count(e => e.IsIncomplete) == 2 &&
               entries.Count(e => e.Date is null) == 2,
            "Undated and partially structured source blocks must not vanish.");
        MaintenanceHistoryItem linked = entries.Single(e => e.Id == interventionId);
        Assert(linked.LineNumber > 0 &&
               linked.SourcePath == "C:/Projets/Fiche de Maintenance.md" &&
               linked.Resolution == "Service rétabli" &&
               linked.IncidentId == incidentId,
            "An intervention must expose its exact source and resolution.");
        Assert(!entries.Any(e => e.Date == new DateOnly(2000, 1, 1)),
            "A fenced Markdown example is never a date or history record.");

        IReadOnlyList<MaintenanceHistoryItem> byDate = MaintenanceHistoryIndex.Search(
            entries, new MaintenanceHistoryFilter
            {
                From = new DateOnly(2026, 10, 2),
                To = new DateOnly(2026, 10, 17)
            });
        Assert(byDate.Count == 3 &&
               byDate[0].Id == interventionId &&
               byDate.Count(e => e.Date is null) == 2,
            "Date filtering must keep undated entries explicitly visible and sortable.");
        IReadOnlyList<MaintenanceHistoryItem> byText = MaintenanceHistoryIndex.Search(
            entries, new MaintenanceHistoryFilter { Search = "redémarrage" });
        Assert(byText.Count == 2, "Search must include narrative fields of incomplete records.");
        IReadOnlyList<MaintenanceHistoryItem> byFacets = MaintenanceHistoryIndex.Search(
            entries, new MaintenanceHistoryFilter
            {
                Status = "Terminé",
                Category = "Applicatif",
                Tag = "correction"
            });
        Assert(byFacets.Count == 1 && byFacets[0].Id == interventionId,
            "Status, category and tag facets must work together without changing source.");

        Assert(MaintenanceHistoryIndex.Search(entries,
                new MaintenanceHistoryFilter { Search = "introuvable" }).Count == 0,
            "Queries with no matches should return an empty list.");
        Assert(MaintenanceHistoryIndex.Parse(
                "C:/Fiche.md", "Legacy", "# Ma fiche\n\nNotes libres.\n").Count == 0,
            "Legacy Markdown without the structured section must remain valid.");

        string orphan = markdown.Replace(
            "- **Incident :** " + incidentId,
            "- **Incident :** INC-ffffffffffffffffffffffffffffffff",
            StringComparison.Ordinal);
        IReadOnlyList<MaintenanceHistoryItem> orphanRows =
            MaintenanceHistoryIndex.Parse("C:/Fiche.md", "Projet", orphan);
        Assert(orphanRows.Single(item => item.Id == interventionId).IsIncomplete &&
               orphanRows.Single(item => item.Id == interventionId).Warning.Contains(
                   "introuvable", StringComparison.Ordinal),
            "A link to a missing incident must be visible rather than discarded.");

        // Deliberately high-volume timeline: searching a preloaded projection is free of disk I/O.
        System.Text.StringBuilder large = new System.Text.StringBuilder(
            "## Historique de maintenance\n\n");
        for (int index = 0; index < 1500; index++)
        {
            large.Append("### Intervention\n")
                .Append("- **Identifiant :** ")
                .Append(MaintenanceMarkdownCodec.NewId(MaintenanceRecordKind.Intervention))
                .Append("\n- **Date :** 2026-10-17\n- **Résumé :** Diagnostic volume ")
                .Append(index)
                .Append("\n- **Statut :** Terminé\n\n");
        }

        IReadOnlyList<MaintenanceHistoryItem> volume =
            MaintenanceHistoryIndex.Parse("C:/Large.md", "Large", large.ToString());
        Assert(volume.Count == 1500 && volume.All(e => !e.IsIncomplete),
            "A large Markdown history must index every source entry.");
        IReadOnlyList<MaintenanceHistoryItem> matches =
            MaintenanceHistoryIndex.Search(volume,
                new MaintenanceHistoryFilter { Search = "Diagnostic volume 1499" });
        Assert(matches.Count == 1 && matches[0].LineNumber > 1,
            "Large-scale text search must find the exact record and source line.");
        Assert(markdown.Contains("## Mes procédures\r\n", StringComparison.Ordinal),
            "Indexing and searching must never rewrite the original Markdown.");
    }

    /// <summary>Fails the regression suite when a maintenance contract is violated.</summary>
    /// <param name="condition">Expected result.</param>
    /// <param name="message">Failure description.</param>
    private static void Assert(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
