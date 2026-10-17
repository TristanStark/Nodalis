using Nodalis.Core.Links;
using Nodalis.Core.Maintenance;
using Nodalis.Core.Relations;
using Nodalis.Infrastructure.Maintenance;
using Nodalis.Infrastructure.Reliability;

/// <summary>Regression tests for stable maintenance references and conflict safety.</summary>
internal static class MaintenanceRelationsSmokeTests
{
    /// <summary>Checks multiple interventions, target moves, deletion, restoration and stale writes.</summary>
    /// <param name="root">Isolated testing workspace.</param>
    /// <returns>Asynchronous regression task.</returns>
    public static async Task RunAsync(string root)
    {
        string folder = Path.Combine(root, "maintenance-relation-fixtures");
        Directory.CreateDirectory(folder);
        string path = Path.Combine(folder, "Fiche de Maintenance.md");
        MaintenanceRecord incident = Record(MaintenanceRecordKind.Incident, "Panne API");
        MaintenanceRecord actionA = Record(MaintenanceRecordKind.Intervention,
            "Vérification API") with { IncidentId = incident.Id };
        MaintenanceRecord actionB = Record(MaintenanceRecordKind.Intervention,
            "Contrôle configuration") with { IncidentId = incident.Id };
        MaintenanceRecord standalone = Record(MaintenanceRecordKind.Intervention,
            "Maintenance autonome");
        string markdown = "# Fiche de Maintenance\r\n\r\n## Notes\r\nTexte libre conservé.\r\n";
        foreach (MaintenanceRecord record in new[] { incident, actionA, actionB, standalone })
        {
            markdown = MaintenanceMarkdownCodec.Append(markdown, record);
        }

        markdown += "\r\n## Informations libres\r\nNe pas supprimer.\r\n";
        await File.WriteAllTextAsync(path, markdown);
        Guid specificationId = Guid.NewGuid();
        Guid decisionId = Guid.NewGuid();
        LinkTargetEntry specification = Target(
            specificationId, "Spécification.md", "Applications/Test/Spécification.md");
        LinkTargetEntry decision = Target(
            decisionId, "Décision.md", "Applications/Test/Décision.md");
        LinkIndexCatalog current = new LinkIndexCatalog { Targets = [specification, decision] };
        WorkspaceMaintenanceService service = new WorkspaceMaintenanceService();

        MaintenanceHistoryItem original = Get(path, markdown, actionA.Id);
        string a = await service.AddRelationAsync(original, "documente",
            specificationId, specification.DisplayName);
        Assert(a.Contains("Texte libre conservé.", StringComparison.Ordinal) &&
               a.Contains("Ne pas supprimer.", StringComparison.Ordinal) &&
               MaintenanceMarkdownCodec.Parse(a).Diagnostics.Count == 0 &&
               a.Contains(MarkdownRelationParser.Format(
                   "documente", specificationId, specification.DisplayName),
                   StringComparison.Ordinal),
            "Adding a relation must preserve existing Markdown and use native relation syntax.");
        Assert(a == await service.AddRelationAsync(Get(path, a, actionA.Id),
                "documente", specificationId, specification.DisplayName),
            "Repeating the same typed link must not duplicate it.");
        bool rejected = false;
        try
        {
            await service.AddRelationAsync(original, "teste", decisionId, decision.DisplayName);
        }
        catch (InvalidDataException)
        {
            rejected = true;
        }

        Assert(rejected && a == await File.ReadAllTextAsync(path),
            "Stale block fingerprints must reject edits without overwriting changes.");

        string b = await service.AddRelationAsync(Get(path, a, actionB.Id), "est lié à",
            decisionId, decision.DisplayName);
        string c = await service.AddRelationAsync(Get(path, b, incident.Id), "documente",
            decisionId, decision.DisplayName);
        string d = await service.AddRelationAsync(Get(path, c, standalone.Id), "teste",
            specificationId, specification.DisplayName);
        IReadOnlyList<MaintenanceDocumentReference> references =
            MaintenanceRelationIndex.Parse(path, d, current);
        IReadOnlyList<MaintenanceDocumentReference> forIncident =
            MaintenanceRelationIndex.ForIncident(Get(path, d, incident.Id), references);
        Assert(references.Count == 4 && forIncident.Count == 3 &&
               forIncident.Any(x => x.RecordId == actionA.Id) &&
               forIncident.Any(x => x.RecordId == actionB.Id) &&
               forIncident.Any(x => x.RecordId == incident.Id) &&
               !forIncident.Any(x => x.RecordId == standalone.Id) &&
               MaintenanceRelationIndex.ForRecord(
                   Get(path, d, actionA.Id), references).Count == 1,
            "Incident references must include only explicitly linked interventions and own links.");

        LinkIndexCatalog renamed = new LinkIndexCatalog
        {
            Targets =
            [
                Target(specificationId, "Spécification v2.md",
                    "Applications/Autre/Spécification v2.md"),
                decision
            ]
        };
        MaintenanceDocumentReference renamedReference =
            MaintenanceRelationIndex.Parse(path, d, renamed)
                .Single(x => x.RecordId == actionA.Id);
        Assert(!renamedReference.IsBroken &&
               renamedReference.TargetId == specificationId &&
               renamedReference.TargetRelativePath == "Applications/Autre/Spécification v2.md",
            "Renamed or moved documents must resolve by the unchanged GUID.");

        LinkIndexCatalog deleted = new LinkIndexCatalog { Targets = [decision] };
        IReadOnlyList<MaintenanceDocumentReference> broken =
            MaintenanceRelationIndex.Parse(path, d, deleted);
        Assert(broken.Count == 4 && broken.Count(x => x.IsBroken) == 2 &&
               broken.Where(x => x.TargetId == specificationId)
                   .All(x => x.TargetRelativePath is null),
            "Deleted documents must remain visibly broken, not resolve by an old name.");

        LinkIndexCatalog restored = new LinkIndexCatalog
        {
            Targets =
            [
                Target(specificationId, "Spécification restaurée.md",
                    "Applications/Archives/Spécification restaurée.md"),
                decision
            ]
        };
        IReadOnlyList<MaintenanceDocumentReference> recovered =
            MaintenanceRelationIndex.Parse(path, d, restored);
        Assert(recovered.Count == 4 && recovered.All(x => !x.IsBroken) &&
               recovered.Single(x => x.RecordId == actionA.Id).TargetRelativePath ==
                   "Applications/Archives/Spécification restaurée.md" &&
               await File.ReadAllTextAsync(path) == d,
            "Restoration of the same GUID must recover the relation without rewriting Markdown.");

        string fence = new string((char)96, 3);
        string example = d + "\r\n" + fence + "markdown\r\n> Relation: teste | Cible: Faux | ID: " +
            Guid.NewGuid().ToString("D") + "\r\n" + fence + "\r\n";
        Assert(MaintenanceRelationIndex.Parse(path, example, restored).Count == 4,
            "Relations in fenced code examples must not be treated as declared dependencies.");

        TextDocumentSession stale = await TextDocumentSession.OpenAsync(path);
        await File.AppendAllTextAsync(path, "\r\nModification externe\r\n");
        rejected = false;
        try
        {
            await stale.SaveAsync(markdown);
        }
        catch (ExternalModificationException)
        {
            rejected = true;
        }

        Assert(rejected && (await File.ReadAllTextAsync(path)).Contains(
            "Modification externe", StringComparison.Ordinal),
            "Concurrent edits must not be overwritten.");
    }

    /// <summary>Builds a valid stable maintenance record.</summary>
    /// <param name="kind">Record kind.</param>
    /// <param name="summary">Summary.</param>
    /// <returns>Valid record.</returns>
    private static MaintenanceRecord Record(MaintenanceRecordKind kind, string summary) =>
        new MaintenanceRecord
        {
            Kind = kind,
            Id = MaintenanceMarkdownCodec.NewId(kind),
            Date = new DateOnly(2026, 10, 17),
            Summary = summary
        };

    /// <summary>Finds the latest source record snapshot by ID.</summary>
    /// <param name="path">Maintenance file.</param>
    /// <param name="content">Current Markdown.</param>
    /// <param name="id">Record ID.</param>
    /// <returns>Source snapshot.</returns>
    private static MaintenanceHistoryItem Get(string path, string content, string id) =>
        MaintenanceHistoryIndex.Parse(path, "Démo", content).Single(x =>
            string.Equals(x.Id, id, StringComparison.OrdinalIgnoreCase));

    /// <summary>Builds one fake identity-registry document target.</summary>
    /// <param name="id">Stable GUID.</param>
    /// <param name="name">Display name.</param>
    /// <param name="path">Relative path.</param>
    /// <returns>Registered target.</returns>
    private static LinkTargetEntry Target(Guid id, string name, string path) =>
        new LinkTargetEntry
        {
            Id = id,
            Kind = LinkTargetKind.Document,
            DisplayName = name,
            QualifiedName = path,
            RelativePath = path
        };

    /// <summary>Fails a violating regression.</summary>
    /// <param name="ok">Expected condition.</param>
    /// <param name="message">Failure explanation.</param>
    private static void Assert(bool ok, string message)
    {
        if (!ok)
        {
            throw new InvalidOperationException(message);
        }
    }
}
