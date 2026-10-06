using System.Text.Json;
using Nodalis.Core.Calendar;
using Nodalis.Core.Domain;
using Nodalis.Core.Meetings;
using Nodalis.Infrastructure.Calendar;
using Nodalis.Infrastructure.Meetings;
using Nodalis.Infrastructure.Persistence;

internal static class WorkspaceCalendarSmokeTests
{
    /// <summary>
    /// Verifies derived task, milestone, and meeting events plus safe explicit-date rescheduling.
    /// </summary>
    /// <param name="root">The smoke-test root directory.</param>
    /// <returns>A task representing the verification.</returns>
    public static async Task RunAsync(
            string root)
    {
        string workspaceRoot =
            Path.Combine(
                root,
                "WorkspaceCalendarWorkspace");

        FileSystemWorkspaceStore store =
            new FileSystemWorkspaceStore(
                workspaceRoot);

        await store.InitializeAsync(
            "Calendar Workspace");

        Guid applicationId =
            Guid.NewGuid();
        Guid projectId =
            Guid.NewGuid();

        string applicationDirectory =
            Path.Combine(
                workspaceRoot,
                WorkspaceLayout.ApplicationsDirectoryName,
                "Application Calendrier");
        string projectDirectory =
            Path.Combine(
                applicationDirectory,
                WorkspaceLayout.ProjectsDirectoryName,
                "Projet Calendrier");
        string milestonesDirectory =
            Path.Combine(
                projectDirectory,
                "Jalons");
        string meetingsDirectory =
            Path.Combine(
                projectDirectory,
                "Réunions");

        Directory.CreateDirectory(
            milestonesDirectory);
        Directory.CreateDirectory(
            meetingsDirectory);

        JsonSerializerOptions jsonOptions =
            new JsonSerializerOptions
            {
                PropertyNamingPolicy =
                    JsonNamingPolicy.CamelCase,
                WriteIndented =
                    true
            };

        ApplicationManifest application =
            new ApplicationManifest
            {
                Id =
                    applicationId,
                Name =
                    "Application Calendrier"
            };

        ProjectManifest project =
            new ProjectManifest
            {
                Id =
                    projectId,
                Name =
                    "Projet Calendrier",
                ApplicationId =
                    applicationId,
                InitialComplexity =
                    ProjectComplexity.Simple,
                Sections =
                [
                    new SectionManifest
                    {
                        Id =
                            Guid.NewGuid(),
                        Name =
                            "Jalons",
                        Order =
                            10,
                        TemplateKey =
                            "milestones"
                    },
                    new SectionManifest
                    {
                        Id =
                            Guid.NewGuid(),
                        Name =
                            "Réunions",
                        Order =
                            20,
                        TemplateKey =
                            "meetings"
                    }
                ]
            };

        await File.WriteAllTextAsync(
            Path.Combine(
                applicationDirectory,
                WorkspaceLayout.ApplicationManifestFileName),
            JsonSerializer.Serialize(
                application,
                jsonOptions));

        await File.WriteAllTextAsync(
            Path.Combine(
                projectDirectory,
                WorkspaceLayout.ProjectManifestFileName),
            JsonSerializer.Serialize(
                project,
                jsonOptions));

        string taskPath =
            Path.Combine(
                projectDirectory,
                "Tâches.md");

        await File.WriteAllTextAsync(
            taskPath,
            "# Tâches\n\n- [ ] Préparer livraison | Responsable: Alice | Échéance: 2026-10-10 | Priorité: Haute | Statut: À faire | Tags: #release\n");

        string milestonePath =
            Path.Combine(
                milestonesDirectory,
                "Jalons.md");

        await File.WriteAllTextAsync(
            milestonePath,
            "# Jalons\n\n| Jalon | Date cible | Statut | Description | Lien |\n| --- | --- | --- | --- | --- |\n| Livraison | 2026-10-15 | En cours | Version cible | |\n");

        WorkspaceMeetingService meetingService =
            new WorkspaceMeetingService(
                workspaceRoot);

        MeetingCreationResult meeting =
            await meetingService.CreateAsync(
                projectDirectory,
                new MeetingDraft
                {
                    Title =
                        "Comité calendrier",
                    Date =
                        new DateOnly(
                            2026,
                            10,
                            12)
                });

        WorkspaceCalendarService service =
            new WorkspaceCalendarService(
                workspaceRoot);

        WorkspaceCalendarSnapshot snapshot =
            await service.RefreshAsync();

        CalendarEventItem task =
            snapshot.Events.Single(item =>
                item.Kind ==
                    CalendarEventKind.Task &&
                item.Title ==
                    "Préparer livraison");
        CalendarEventItem milestone =
            snapshot.Events.Single(item =>
                item.Kind ==
                    CalendarEventKind.Milestone &&
                item.Title ==
                    "Livraison");
        CalendarEventItem meetingItem =
            snapshot.Events.Single(item =>
                item.Kind ==
                    CalendarEventKind.Meeting &&
                item.SourceRelativePath.EndsWith(
                    Path.GetFileName(
                        meeting.FilePath),
                    StringComparison.OrdinalIgnoreCase));

        Assert(
            task.Date ==
                new DateOnly(
                    2026,
                    10,
                    10) &&
            milestone.Date ==
                new DateOnly(
                    2026,
                    10,
                    15) &&
            meetingItem.Date ==
                new DateOnly(
                    2026,
                    10,
                    12),
            "Calendar must derive dated tasks, milestones, and meetings from their existing sources.");

        Assert(
            task.CanReschedule &&
            milestone.CanReschedule &&
            !meetingItem.CanReschedule,
            "Only source kinds with an existing explicit safe date update API may be drag-rescheduled.");

        await service.RescheduleAsync(
            task,
            new DateOnly(
                2026,
                10,
                11));

        await service.RescheduleAsync(
            milestone,
            new DateOnly(
                2026,
                10,
                16));

        WorkspaceCalendarSnapshot updated =
            await service.RefreshAsync();

        Assert(
            updated.Events.Single(item =>
                    item.Kind ==
                        CalendarEventKind.Task &&
                    item.Title ==
                        "Préparer livraison")
                .Date ==
                new DateOnly(
                    2026,
                    10,
                    11),
            "Task drag-rescheduling must rewrite the explicit Markdown due date.");

        Assert(
            updated.Events.Single(item =>
                    item.Kind ==
                        CalendarEventKind.Milestone &&
                    item.Title ==
                        "Livraison")
                .Date ==
                new DateOnly(
                    2026,
                    10,
                    16),
            "Milestone drag-rescheduling must rewrite the explicit Markdown target date.");

        bool meetingRejected =
            false;

        try
        {
            await service.RescheduleAsync(
                meetingItem,
                new DateOnly(
                    2026,
                    10,
                    13));
        }
        catch (InvalidOperationException)
        {
            meetingRejected =
                true;
        }

        Assert(
            meetingRejected,
            "Meetings must not be silently renamed or rewritten when no atomic meeting date update API exists.");

        Assert(
            !Directory.EnumerateFiles(
                    workspaceRoot,
                    "*calendar*",
                    SearchOption.AllDirectories)
                .Any(),
            "Calendar must not introduce a secondary opaque calendar file.");
    }

    /// <summary>
    /// Throws when a calendar smoke-test condition is not satisfied.
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
