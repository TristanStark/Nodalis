using Nodalis.Core.Maintenance;

internal static class MaintenanceMarkdownSmokeTests
{
    /// <summary>
    /// Verifies the versioned Markdown contract, non-destructive round trips,
    /// stable IDs, damaged records, and legacy compatibility.
    /// </summary>
    public static void Run()
    {
        string incidentId = MaintenanceMarkdownCodec.NewId(MaintenanceRecordKind.Incident);
        string interventionId = MaintenanceMarkdownCodec.NewId(MaintenanceRecordKind.Intervention);
        string legacy = "# Fiche libre\r\n\r\n" +
            "Une note historique à conserver exactement, même avec des accents.\r\n\r\n" +
            "## Notes utilisateur\r\n\r\n" +
            "~~~markdown\r\n## Historique de maintenance\r\n### Incident\r\n~~~\r\n" +
            "### Partie libre\r\n\r\n- ne pas perdre cette liste\r\n";
        Assert(MaintenanceMarkdownCodec.Parse(legacy).Records.Count == 0,
            "A fenced history example must not become a maintenance record.");

        MaintenanceRecord incident = new MaintenanceRecord
        {
            Kind = MaintenanceRecordKind.Incident,
            Id = incidentId,
            Date = new DateOnly(2026, 10, 17),
            Summary = "Impossible de démarrer le service",
            Details = new Dictionary<string, string>
            {
                ["Symptômes"] = "Erreur 503",
                ["Diagnostic"] = "Service indisponible"
            }
        };
        MaintenanceRecord intervention = new MaintenanceRecord
        {
            Kind = MaintenanceRecordKind.Intervention,
            Id = interventionId,
            Date = new DateOnly(2026, 10, 18),
            Summary = "Redémarrage contrôlé",
            IncidentId = incidentId,
            Details = new Dictionary<string, string>
            {
                ["Action"] = "Relancer le service",
                ["Résultat"] = "Service rétabli",
                ["Équipe"] = "Exploitation"
            }
        };

        string afterIncident = MaintenanceMarkdownCodec.Append(legacy, incident);
        string updated = MaintenanceMarkdownCodec.Append(afterIncident, intervention);
        Assert(updated.StartsWith(legacy, StringComparison.Ordinal),
            "Existing arbitrary Markdown must remain a byte-for-byte prefix.");
        Assert(!updated.Replace("\r\n", string.Empty, StringComparison.Ordinal).Contains('\n'),
            "CRLF document must keep CRLF line endings.");
        MaintenanceParseResult parsed = MaintenanceMarkdownCodec.Parse(updated);
        Assert(parsed.Diagnostics.Count == 0 && parsed.Records.Count == 2,
            "Both records must round-trip without diagnostics.");
        Assert(parsed.Records[0].Id == incidentId &&
               parsed.Records[1].Id == interventionId &&
               parsed.Records[1].IncidentId == incidentId &&
               parsed.Records[1].Details["Équipe"] == "Exploitation",
            "Identity, cross-references and arbitrary readable fields must survive parsing.");

        string withFencedExample = updated +
            "~~~markdown\r\n- **Identifiant :** exemple\r\n- **Résumé :** faux champ\r\n~~~\r\n";
        Assert(MaintenanceMarkdownCodec.Parse(withFencedExample).Diagnostics.Count == 0,
            "Code examples inside a record must not be parsed as duplicate fields.");

        // Simulating a document rename or move does not alter inline record identities.
        string movedDocument = updated;
        Assert(MaintenanceMarkdownCodec.Parse(movedDocument).Records[0].Id == incidentId,
            "Record identities must not depend on file names or paths.");

        string otherSection = updated + "## Historique futur\r\n\r\nAutres notes.\r\n";
        string extended = MaintenanceMarkdownCodec.Append(otherSection,
            intervention with { Id = MaintenanceMarkdownCodec.NewId(MaintenanceRecordKind.Intervention) });
        Assert(extended.EndsWith("## Historique futur\r\n\r\nAutres notes.\r\n", StringComparison.Ordinal) &&
               MaintenanceMarkdownCodec.Parse(extended).Records.Count == 3,
            "Appending in an existing history must not alter the following free-form section.");

        AssertThrows(() => MaintenanceMarkdownCodec.Append(updated, incident),
            "An existing stable record ID must never be replaced.");
        AssertThrows(() => MaintenanceMarkdownCodec.Append(legacy, intervention),
            "An intervention may not silently point to an absent incident.");
        AssertThrows(() => MaintenanceMarkdownCodec.Append(legacy,
            incident with { Id = MaintenanceMarkdownCodec.NewId(MaintenanceRecordKind.Incident),
                            Summary = "Résumé\n### Intervention" }),
            "A generated summary must not inject additional Markdown headings.");

        string corrupted = "## Historique de maintenance\n\n### Incident\n" +
            "- **Identifiant :** " + incidentId + "\n" +
            "- **Résumé :** Date absente\n";
        MaintenanceParseResult damaged = MaintenanceMarkdownCodec.Parse(corrupted);
        Assert(damaged.Records.Count == 0 && damaged.Diagnostics.Count > 0,
            "A malformed record must be reported, not guessed or migrated.");
        AssertThrows(() => MaintenanceMarkdownCodec.Append(corrupted, incident),
            "A damaged block must be repaired explicitly before insertion.");

        string deletedIncident = updated.Replace(
            "### Incident\r\n", "### Ancien incident\r\n", StringComparison.Ordinal);
        Assert(MaintenanceMarkdownCodec.Parse(deletedIncident).Diagnostics
                .Any(item => item.Message.Contains("manquant", StringComparison.OrdinalIgnoreCase)),
            "An intervention referencing a deleted incident must remain visible as a diagnostic.");

        string duplicate = updated + "\r\n" + updated[updated.IndexOf("### Incident", StringComparison.Ordinal)..];
        Assert(MaintenanceMarkdownCodec.Parse(duplicate).Diagnostics
                .Any(item => item.Message.Contains("double", StringComparison.OrdinalIgnoreCase)),
            "Duplicate incident IDs must be reported.");
        string ambiguous = "## Historique de maintenance\n\n## Divers\n\n## Historique de maintenance\n";
        AssertThrows(() => MaintenanceMarkdownCodec.Append(ambiguous, incident),
            "Two similarly named managed sections must never be updated ambiguously.");
        Assert(MaintenanceMarkdownCodec.Parse("# Fiche de Maintenance\n\nNotes libres.\n")
                .Records.Count == 0,
            "An Ultra-simple template or legacy arbitrary note must remain readable.");
    }

    /// <summary>Fails fast on a violated Markdown persistence invariant.</summary>
    /// <param name="condition">Condition that must hold.</param>
    /// <param name="message">Failure explanation.</param>
    private static void Assert(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    /// <summary>Asserts that an invalid append is safely rejected.</summary>
    /// <param name="action">Mutation that must not succeed.</param>
    /// <param name="message">Failure explanation.</param>
    private static void AssertThrows(Action action, string message)
    {
        bool rejected = false;
        try
        {
            action();
        }
        catch (ArgumentException)
        {
            rejected = true;
        }
        catch (InvalidDataException)
        {
            rejected = true;
        }

        Assert(rejected, message);
    }
}
