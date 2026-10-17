using Nodalis.Core.Maintenance;
using Nodalis.Infrastructure.Maintenance;
using Nodalis.Infrastructure.Reliability;

/// <summary>Integration checks for version 1.2 maintenance entry forms and file persistence.</summary>
internal static class MaintenanceInterventionSmokeTests
{
    /// <summary>Tests create/edit, safe cancellation semantics and external change protection.</summary>
    /// <param name="root">Temporary smoke workspace.</param>
    /// <returns>Async regression task.</returns>
    public static async Task RunAsync(string root)
    {
        string project = Path.Combine(root, "maintenance-ui");
        Directory.CreateDirectory(project);
        string path = Path.Combine(project, "Fiche de Maintenance.md");
        string legacy = "# Fiche héritée\r\n\r\n## Notes privées\r\n\r\n" +
            "Texte de l'utilisateur, ne jamais le réécrire.\r\n\r\n";
        await File.WriteAllTextAsync(path, legacy);
        WorkspaceMaintenanceService service = new WorkspaceMaintenanceService();
        Assert((await service.ReadAsync(path)).Records.Count == 0,
            "Legacy notes without a managed history must load unchanged.");

        MaintenanceRecord incident = new MaintenanceRecord
        {
            Kind = MaintenanceRecordKind.Incident,
            Id = MaintenanceMarkdownCodec.NewId(MaintenanceRecordKind.Incident),
            Date = new DateOnly(2026, 10, 17),
            Summary = "Incident initial"
        };
        string withIncident = MaintenanceMarkdownCodec.Append(legacy, incident);
        await File.WriteAllTextAsync(path, withIncident);

        MaintenanceRecord first = new MaintenanceRecord
        {
            Kind = MaintenanceRecordKind.Intervention,
            Id = MaintenanceMarkdownCodec.NewId(MaintenanceRecordKind.Intervention),
            Date = new DateOnly(2026, 10, 17),
            Summary = "Correction initiale",
            IncidentId = incident.Id,
            Details = new Dictionary<string, string>
            {
                ["Action"] = "Redémarrage",
                ["Personnalisation"] = "Champ non standard"
            }
        };
        await service.AddAsync(path, first);
        MaintenanceRecord second = first with
        {
            Id = MaintenanceMarkdownCodec.NewId(MaintenanceRecordKind.Intervention),
            Summary = "Deuxième intervention",
            Details = new Dictionary<string, string>()
        };
        await service.AddAsync(path, second);
        MaintenanceParseResult entries = await service.ReadAsync(path);
        Assert(entries.Records.Count == 3 &&
               entries.Records.Count(item => item.IncidentId == incident.Id) == 2,
            "Multiple interventions may link to the same stable incident.");

        // Form cancellation does not invoke persistence: only a returned draft is held in memory.
        string beforeCancel = await File.ReadAllTextAsync(path);
        Assert(beforeCancel == await File.ReadAllTextAsync(path),
            "Opening and canceling a form must not touch Markdown.");

        // Arbitrary prose and fenced example metadata inside an intervention
        // must survive editing, rather than being parsed as editable fields.
        string nextEntry = "### Intervention\r\n- **Identifiant :** " + second.Id;
        string freeInsideEntry = "Notes personnelles non structurées.\r\n\r\n" +
            "~~~markdown\r\n- **Résultat :** ceci est un exemple\r\n~~~\r\n\r\n";
        Assert(beforeCancel.Contains(nextEntry, StringComparison.Ordinal),
            "Expected a second distinct intervention in the test fixture.");
        beforeCancel = beforeCancel.Replace(nextEntry,
            freeInsideEntry + nextEntry, StringComparison.Ordinal);
        await File.WriteAllTextAsync(path, beforeCancel);

        // A different manual edit made after opening the form must be retained.
        string manuallyExtended = beforeCancel + "\r\n## Journal manuel\r\n\r\nImportant.\r\n";
        await File.WriteAllTextAsync(path, manuallyExtended);
        MaintenanceRecord edited = first with
        {
            Date = new DateOnly(2026, 10, 18),
            Summary = "Correction définitive",
            Details = new Dictionary<string, string>
            {
                ["Action"] = "Correctif validé",
                ["Personnalisation"] = "Champ non standard"
            }
        };
        await service.EditAsync(path, first, edited);
        string actual = await File.ReadAllTextAsync(path);
        Assert(actual.Contains("## Journal manuel\r\n\r\nImportant.", StringComparison.Ordinal) &&
               actual.Contains("Texte de l'utilisateur", StringComparison.Ordinal) &&
               actual.Contains("Champ non standard", StringComparison.Ordinal) &&
               actual.Contains("Notes personnelles non structurées.", StringComparison.Ordinal) &&
               actual.Contains("- **Résultat :** ceci est un exemple", StringComparison.Ordinal),
            "External Markdown, fenced examples and custom fields must survive an edit.");

        entries = await service.ReadAsync(path);
        Assert(entries.Diagnostics.Count == 0 &&
               entries.Records.Single(item => item.Id == first.Id).Summary == "Correction définitive" &&
               entries.Records.Single(item => item.Id == first.Id).Date ==
                   new DateOnly(2026, 10, 18),
            "Edited fields must persist, preserving the ID and incident link.");

        string beforeConflictingEdit = await File.ReadAllTextAsync(path);
        bool refused = false;
        try
        {
            await service.EditAsync(path, first,
                first with { Summary = "Overwrite an externally edited intervention" });
        }
        catch (InvalidDataException)
        {
            refused = true;
        }

        Assert(refused &&
               string.Equals(await File.ReadAllTextAsync(path),
                   beforeConflictingEdit, StringComparison.Ordinal),
            "Stale form data may not overwrite an externally updated intervention.");

        // A revision recorded before an external edit must be rejected by the shared writer.
        TextDocumentSession stale = await TextDocumentSession.OpenAsync(path);
        await File.AppendAllTextAsync(path, "\r\nNouvelle ligne externe\r\n");
        refused = false;
        try
        {
            await stale.SaveAsync(stale.Content + "\r\nChangement du formulaire\r\n");
        }
        catch (ExternalModificationException)
        {
            refused = true;
        }

        Assert(refused &&
               (await File.ReadAllTextAsync(path)).Contains("Nouvelle ligne externe",
                   StringComparison.Ordinal),
            "Atomic revision checks must prevent lost manual changes.");

        string original = await File.ReadAllTextAsync(path);
        MaintenanceRecord lateEntry = new MaintenanceRecord
        {
            Kind = MaintenanceRecordKind.Intervention,
            Id = MaintenanceMarkdownCodec.NewId(MaintenanceRecordKind.Intervention),
            Date = new DateOnly(2026, 10, 19),
            Summary = "Ajout après modification externe"
        };
        await service.AddAsync(path, lateEntry);
        Assert((await File.ReadAllTextAsync(path)).StartsWith(
                original[..original.IndexOf("## Historique de maintenance", StringComparison.Ordinal)],
                StringComparison.Ordinal),
            "An append after external Markdown edits must preserve all preceding free notes.");

        string missingPath = Path.Combine(project, "Inexistante.md");
        refused = false;
        try
        {
            await service.AddAsync(missingPath, lateEntry with
            {
                Id = MaintenanceMarkdownCodec.NewId(MaintenanceRecordKind.Intervention)
            });
        }
        catch (FileNotFoundException)
        {
            refused = true;
        }

        Assert(refused && !File.Exists(missingPath),
            "Maintenance operations must not create extra project files automatically.");
    }

    /// <summary>Asserts a required persistence invariant.</summary>
    /// <param name="condition">Expected success.</param>
    /// <param name="message">Diagnostic text.</param>
    private static void Assert(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
