using Nodalis.Core.Decisions;
using Nodalis.Core.Links;
using Nodalis.Core.Maintenance;
using Nodalis.Core.Milestones;
using Nodalis.Core.Tasks;
using Nodalis.Core.Views;

/// <summary>Protects the 1.5 read-model and deterministic query contract.</summary>
internal static class WorkspaceViewsSmokeTests
{
    /// <summary>Executes zero-dependency read-only projection and query fixtures.</summary>
    public static void Run()
    {
        Guid projectDocument = Guid.NewGuid();
        Guid globalDocument = Guid.NewGuid();
        Guid maintenanceDocument = Guid.NewGuid();
        Guid missingDocument = Guid.NewGuid();
        LinkIndexCatalog catalog = new()
        {
            Targets =
            [
                Document(projectDocument, "Projets/Alpha/Taches.md", "project:alpha"),
                Document(globalDocument, "Global/Decision.md", "global"),
                Document(maintenanceDocument, "Projets/Alpha/Fiche de Maintenance.md", "project:alpha")
            ]
        };
        TaskItem task = new()
        {
            Id = Guid.NewGuid(),
            SourceDocumentId = projectDocument,
            SourceRelativePath = "Projets/Alpha/Taches.md",
            LineNumber = 15,
            RawLine = "- [ ] Terminer la migration",
            Text = "Terminer la migration",
            IsCompleted = false,
            DueDate = new DateOnly(2026, 10, 21),
            Status = "En cours",
            Priority = "Haute",
            Owner = "Equipe API",
            Tags = ["urgent", "migration"],
            ProjectName = "Alpha",
            ProjectId = Guid.NewGuid()
        };
        TaskItem missingTask = task with
        {
            Id = Guid.NewGuid(), SourceDocumentId = missingDocument,
            Text = "Ne doit pas être projeté"
        };
        MilestoneItem milestone = new()
        {
            Id = Guid.NewGuid(),
            ProjectId = Guid.NewGuid(),
            ProjectName = "Alpha",
            SourceRelativePath = "Projets/Alpha/Taches.md",
            LineNumber = 9,
            RawLine = "| Livraison | 2026-10-23 |",
            Name = "Livraison",
            TargetDate = new DateOnly(2026, 10, 23),
            Status = "Planifié"
        };
        DecisionRecord decision = new()
        {
            SourceRelativePath = "Global/Decision.md",
            Title = "Architecture",
            ScopeKind = "Global",
            ScopeName = "Global",
            Date = new DateOnly(2026, 10, 20),
            Status = "Validée"
        };
        string root = Path.Combine(Path.GetTempPath(), "Nodalis-Views-Test");
        MaintenanceHistoryItem intervention = new()
        {
            SourcePath = Path.Combine(root, "Projets", "Alpha", "Fiche de Maintenance.md"),
            ProjectName = "Alpha",
            LineNumber = 31,
            Kind = MaintenanceRecordKind.Intervention,
            Id = "INT-abcdefabcdefabcdefabcdefabcdefab",
            Date = new DateOnly(2026, 10, 22),
            Summary = "Redémarrage du service",
            Status = "Fermée",
            Category = "Service",
            Tags = "maintenance; production",
            SearchText = "redémarrage"
        };
        WorkspaceViewProjection projected = WorkspaceViewProjector.Build(catalog,
            tasks: [task, missingTask], milestones: [milestone],
            decisions: [decision], maintenance: [intervention], workspaceRoot: root);
        Assert(projected.Items.Count == 7 &&
            projected.Items.Select(item => item.Kind).Distinct().Count() == 5,
            "Projection must reuse three document identities and four existing records.");
        Assert(projected.Diagnostics.Count == 1 &&
            projected.Diagnostics[0].Reason.Contains("introuvable", StringComparison.OrdinalIgnoreCase),
            "Orphan Markdown task must be diagnosed, never redirected to homonym.");

        WorkspaceViewItem taskView = projected.Items.Single(item =>
            item.Kind == WorkspaceViewItemKind.Task);
        Assert(taskView.Source.DocumentId == projectDocument &&
            taskView.Source.LineNumber == 15 &&
            taskView.Fields["date"].Date == new DateOnly(2026, 10, 21) &&
            taskView.Fields["completed"].Boolean == false &&
            taskView.Fields["priority"].Text == "Haute" &&
            !taskView.Fields.ContainsKey("category"),
            "Task projection must retain real identity, source, dates and absent properties.");
        WorkspaceViewItem maintenanceView = projected.Items.Single(item =>
            item.Kind == WorkspaceViewItemKind.Intervention);
        Assert(maintenanceView.Source.DocumentId == maintenanceDocument &&
            maintenanceView.Source.LineNumber == 31 &&
            maintenanceView.Source.RecordId == intervention.Id &&
            maintenanceView.Tags.Contains("production"),
            "Maintenance projection must resolve absolute paths inside the workspace.");

        WorkspaceViewProjection outside = WorkspaceViewProjector.Build(catalog,
            maintenance: [intervention with { SourcePath =
                Path.GetFullPath(Path.Combine(root, "..", "external.md")) }],
            workspaceRoot: root);
        Assert(outside.Items.Count == 3 && outside.Diagnostics.Count == 1,
            "Maintenance source outside the workspace must never be read or projected.");

        WorkspaceViewQueryResult filtered = WorkspaceViewQueryEngine.Execute(
            projected.Items, new WorkspaceViewQuery
            {
                Kinds = [WorkspaceViewItemKind.Task, WorkspaceViewItemKind.Milestone],
                ScopeIdentity = "project:alpha",
                Filter = new WorkspaceViewFilter
                {
                    Logic = WorkspaceViewLogic.All,
                    Predicates =
                    [
                        new WorkspaceViewPredicate
                        {
                            Field = "date", Operator = WorkspaceViewOperator.Between,
                            Date = new DateOnly(2026, 10, 20),
                            EndDate = new DateOnly(2026, 10, 24)
                        }
                    ]
                },
                SortBy = "date", GroupBy = "kind"
            });
        Assert(filtered.TotalMatches == 2 &&
            filtered.Items[0].Kind == WorkspaceViewItemKind.Task &&
            filtered.Items[1].Kind == WorkspaceViewItemKind.Milestone &&
            filtered.Groups.Count == 2,
            "Typed date range and ascending date sort should return both project items.");

        WorkspaceViewQueryResult urgent = WorkspaceViewQueryEngine.Execute(
            projected.Items, new WorkspaceViewQuery
            {
                Filter = new WorkspaceViewFilter
                {
                    Logic = WorkspaceViewLogic.Any,
                    Predicates =
                    [
                        new WorkspaceViewPredicate
                        {
                            Field = "tags", Operator = WorkspaceViewOperator.HasTag,
                            Text = "urgent"
                        },
                        new WorkspaceViewPredicate
                        {
                            Field = "title", Operator = WorkspaceViewOperator.Contains,
                            Text = "Redémarrage"
                        }
                    ]
                },
                SortBy = "title"
            });
        Assert(urgent.TotalMatches == 2,
            "OR filters should combine tag matches and a maintenance title match.");

        WorkspaceViewQueryResult noInventedStatus = WorkspaceViewQueryEngine.Execute(
            projected.Items, new WorkspaceViewQuery
            {
                Filter = new WorkspaceViewFilter
                {
                    Predicates = [new WorkspaceViewPredicate
                    {
                        Field = "status", Operator = WorkspaceViewOperator.NotEquals,
                        Text = "Fermée"
                    }]
                }
            });
        Assert(!noInventedStatus.Items.Any(item =>
                item.Kind == WorkspaceViewItemKind.Document &&
                !item.Fields.ContainsKey("status")),
            "A missing status does not imply an arbitrary other status.");

        WorkspaceViewQueryResult page = WorkspaceViewQueryEngine.Execute(
            projected.Items, new WorkspaceViewQuery
            {
                SortBy = "title", Offset = 2, Limit = 2,
                MaxScannedItems = 2
            });
        Assert(page.IsTruncated && page.ScannedItems == 2 && page.TotalMatches == 2,
            "A capped scan must explicitly report truncation and bounded work.");

        bool invalid = false;
        try
        {
            WorkspaceViewQueryEngine.Execute(projected.Items,
                new WorkspaceViewQuery { SortBy = "DROP TABLE" });
        }
        catch (ArgumentException)
        {
            invalid = true;
        }

        Assert(invalid, "Unknown and executable field expressions must be rejected.");

        WorkspaceViewFilter recursive = new();
        for (int i = 0; i < 10; i++)
        {
            recursive = new WorkspaceViewFilter { Groups = [recursive] };
        }

        invalid = false;
        try
        {
            WorkspaceViewQueryEngine.Execute(projected.Items,
                new WorkspaceViewQuery { Filter = recursive });
        }
        catch (ArgumentException)
        {
            invalid = true;
        }

        Assert(invalid, "Deep nested filter expressions must be rejected.");

        using CancellationTokenSource stopped = new();
        stopped.Cancel();
        bool cancelled = false;
        try
        {
            WorkspaceViewQueryEngine.Execute(projected.Items,
                new WorkspaceViewQuery(), stopped.Token);
        }
        catch (OperationCanceledException)
        {
            cancelled = true;
        }

        Assert(cancelled, "Workspace view queries must honor cooperative cancellation.");

        Console.WriteLine("[Views 1.5] projection, source navigation, filters and pagination passed.");
    }

    /// <summary>Creates a known active canonical document.</summary>
    /// <param name="id">Document GUID.</param>
    /// <param name="path">Canonical relative Markdown path.</param>
    /// <param name="scope">Stable workspace scope.</param>
    /// <returns>Indexed document entry.</returns>
    private static LinkTargetEntry Document(Guid id, string path, string scope)
    {
        return new LinkTargetEntry
        {
            Id = id,
            Kind = LinkTargetKind.Document,
            RelativePath = path,
            DisplayName = Path.GetFileName(path),
            QualifiedName = path,
            ScopeIdentity = scope
        };
    }

    /// <summary>Fails the smoke suite with the relevant behavior.</summary>
    /// <param name="ok">Invariant under test.</param>
    /// <param name="description">Useful failure explanation.</param>
    private static void Assert(bool ok, string description)
    {
        if (!ok)
        {
            throw new InvalidOperationException(description);
        }
    }
}
