using Nodalis.Core.Domain;
using Nodalis.Core.Milestones;
using Nodalis.Infrastructure.Milestones;
using Nodalis.Infrastructure.Exporting;
using Nodalis.Core.Meetings;
using Nodalis.Core.Decisions;
using Nodalis.Infrastructure.Meetings;
using Nodalis.Infrastructure.Decisions;
using Nodalis.Core.Navigation;
using Nodalis.Core.Projects;
using Nodalis.Core.Templates;
using Nodalis.Infrastructure.Navigation;
using Nodalis.Infrastructure.Persistence;
using Nodalis.Infrastructure.Projects;
using Nodalis.Infrastructure.Templates;

/// <summary>Regressions for explicit nested section paths in new Complex projects.</summary>
internal static class ComplexProjectLayoutSmokeTests
{
    /// <summary>
    /// Checks created structure, nested stable navigation IDs, document ownership,
    /// legacy profile upgrades and unchanged Ultra-simple project behavior.
    /// </summary>
    /// <param name="root">Temporary smoke suite root.</param>
    /// <returns>Isolated filesystem regression task.</returns>
    public static async Task RunAsync(string root)
    {
        string workspace = Path.Combine(root, "Complex-layout-v2");
        await new FileSystemWorkspaceStore(workspace).InitializeAsync("Complex v2");
        try
        {
            FileSystemTemplateStore templates = new(workspace);
            ProjectProfileCatalog current = await templates.LoadProjectProfilesAsync();
            ProjectProfileDefinition builtIn = current.Profiles.Single(
                profile => profile.Complexity == ProjectComplexity.Complex);

            Assert(builtIn.Sections.Single(section => section.Name == "Jalons")
                    .RelativeDirectory == "Management",
                "New built-in Complex profiles must place the unique milestone file in Management.");

            Assert(builtIn.Sections.Single(section => section.Name == "Technique")
                    .RelativeDirectory == "Documentation/Technique" &&
                builtIn.Sections.Single(section => section.Name == "Architecture")
                    .RelativeDirectory == "Documentation/Technique/Architecture" &&
                builtIn.Sections.Single(section => section.Name == "Réunions")
                    .RelativeDirectory == "Management/Réunions" &&
                builtIn.Sections.Single(section => section.Name == "Exigences")
                    .RelativeDirectory == "Documentation/Exigences",
                "The new Complex default must declare the complete nested hierarchy.");

            Guid appId = Guid.NewGuid();
            string app = Path.Combine(
                workspace, WorkspaceLayout.ApplicationsDirectoryName, "Test application");
            Directory.CreateDirectory(app);
            ProjectCreationTarget destination = new ProjectCreationTarget
            {
                ApplicationId = appId,
                ApplicationName = "Test application",
                ParentDirectory = app,
                DisplayName = "Test application"
            };

            FileSystemProjectCreator creator = new(workspace);
            ProjectCreationResult complex = await creator.CreateAsync(new ProjectCreationRequest
            {
                Name = "Complexe V2",
                Complexity = ProjectComplexity.Complex,
                Target = destination
            });

            string project = complex.ProjectDirectory;
            string[] paths =
            [
                "Documentation/Exigences",
                "Documentation/Fonctionnel/Fonctionnel.md",
                "Documentation/Technique/Technique.md",
                "Documentation/Technique/Architecture",
                "Analyse/Risques/Risques.md",
                "Analyse/Dépendances/Dépendances.md",
                "Management/Réunions",
                "Management/Décisions",
                "Management/Jalons.md",
                "Exploitation/Déploiement/Déploiement.md",
                "Tests/Tests.md"
            ];
            foreach (string relativePath in paths)
            {
                string fullPath = Path.Combine(project, relativePath.Replace(
                    '/', Path.DirectorySeparatorChar));
                Assert(File.Exists(fullPath) || Directory.Exists(fullPath),
                    $"Missing Complex v2 project path {relativePath}.");
            }

            ComplexProjectMigrationPreviewService migrations =
                new ComplexProjectMigrationPreviewService(workspace);
            ComplexProjectMigrationPreview inventory =
                await migrations.PreviewAsync(project);
            Assert(inventory.Moves.Count == 7 &&
                inventory.Moves.All(move => move.State ==
                    ComplexMigrationMoveState.AlreadyPlaced) &&
                inventory.MissingDirectories.Count == 0,
                "A newly created v2 Complex layout must not be flagged as needing legacy migration.");

            string managedMilestones = Path.Combine(project, "Management", "Jalons.md");
            Assert(File.Exists(managedMilestones) &&
                !File.Exists(Path.Combine(project, "Jalons.md")) &&
                File.Exists(Path.Combine(project, "Glossaire.md")) &&
                File.Exists(Path.Combine(project, "Notes rapides.md")) &&
                !Directory.Exists(Path.Combine(project, "Technique")) &&
                !Directory.Exists(Path.Combine(project, "Dépendances")) &&
                !Directory.Exists(Path.Combine(project, "Déploiement")),
                "Complex must create one managed milestone file, preserve root glossary and quick notes and avoid legacy duplicate folders.");

            WorkspaceMilestoneService milestoneService = new(workspace);
            MilestoneItem createdMilestone = await milestoneService.AddAsync(
                project,
                new MilestoneDraft
                {
                    Name = "Validation structure",
                    TargetDate = new DateOnly(2026, 11, 15),
                    Status = "À faire"
                });

            Assert(createdMilestone.SourceRelativePath.EndsWith(
                    "/Management/Jalons.md", StringComparison.OrdinalIgnoreCase) &&
                (await milestoneService.GetMilestonesAsync(project))
                    .Any(item => item.Id == createdMilestone.Id) &&
                File.Exists(managedMilestones),
                "Milestone create/list/calendar sources must resolve nested Management/Jalons.md.");

            // A stale legacy root file must never win over the declared new path.
            string obsoleteRoot = Path.Combine(project, "Jalons.md");
            await File.WriteAllTextAsync(obsoleteRoot, "# Obsolete duplicated milestones\n");
            bool duplicateRefused = false;
            try
            {
                await milestoneService.GetMilestonesAsync(project);
            }
            catch (InvalidDataException)
            {
                duplicateRefused = true;
            }

            Assert(duplicateRefused &&
                File.Exists(obsoleteRoot) &&
                File.Exists(managedMilestones),
                "Two competing milestone sources must be refused without changing either.");
            File.Delete(obsoleteRoot);

            WorkspaceMeetingService meetingService = new(workspace);
            MeetingCreationResult meeting = await meetingService.CreateAsync(project,
                new MeetingDraft
                {
                    Title = "Revue de conception",
                    Date = new DateOnly(2026, 10, 31),
                    AiSummary = "La nouvelle structure est validée."
                });
            WorkspaceDecisionService decisionService = new(workspace);
            DecisionCreationResult decision = await decisionService.CreateAsync(project,
                new DecisionDraft
                {
                    Title = "Architecture retenue",
                    Decision = "Conserver les GUID des sections.",
                    Date = new DateOnly(2026, 10, 31)
                });

            Assert(Path.GetDirectoryName(meeting.FilePath) ==
                    Path.Combine(project, "Management", "Réunions") &&
                Path.GetDirectoryName(decision.FilePath) ==
                    Path.Combine(project, "Management", "Décisions") &&
                !Directory.Exists(Path.Combine(project, "Réunions")) &&
                !Directory.Exists(Path.Combine(project, "Décisions")) &&
                (await decisionService.GetDecisionsAsync(project)).Count == 1,
                "New Complex meetings and decisions must use declared Management folders.");

            // A prompt created from a nested meeting must still find its owner.
            string presentationFile = Path.Combine(project, "Présentation.md");
            string existingPresentation = await File.ReadAllTextAsync(presentationFile);
            string normalizedPresentation = existingPresentation
                .Replace("\r\n", "\n", StringComparison.Ordinal)
                .Replace('\r', '\n');
            string updatedPresentation = normalizedPresentation.Replace(
                "## Présentation\n\n", "## Présentation\n\nContexte projet.\n\n",
                StringComparison.Ordinal);
            Assert(updatedPresentation.Contains("Contexte projet.", StringComparison.Ordinal),
                "The generated presentation fixture must contain a project introduction.");
            await File.WriteAllTextAsync(presentationFile, updatedPresentation);

            ProjectMeetingPromptService prompt = new(workspace);
            (string promptPath, string promptContent) =
                await prompt.GenerateAsync(meeting.FilePath);
            try
            {
                Assert(File.Exists(promptPath) &&
                    promptContent.Contains("Contexte projet.", StringComparison.Ordinal) &&
                    promptContent.Contains("La nouvelle structure est validée.",
                        StringComparison.Ordinal),
                    "Nested project meetings must support the existing project update prompt.");
            }
            finally
            {
                File.Delete(promptPath);
            }

            WorkspaceProjectStructureService structure = new(workspace);
            ProjectStructureState state = await structure.GetStructureAsync(project);
            ProjectSectionState documentation = state.Sections.Single(
                section => section.Name == "Documentation");
            ProjectSectionState technical = state.Sections.Single(
                section => section.Name == "Technique");
            ProjectSectionState architecture = state.Sections.Single(
                section => section.Name == "Architecture");

            Assert(documentation.Documents.SequenceEqual(["Documentation.md"]) &&
                technical.Documents.SequenceEqual(["Technique.md"]) &&
                architecture.Documents.Count == 0 &&
                technical.Id == complex.Project.Sections.Single(section =>
                    section.Name == "Technique").Id,
                "Nested section documents must not appear in their parent section twice.");

            WorkspaceNavigationBuilder navigationBuilder = new();
            WorkspaceNavigationNode tree = await navigationBuilder.BuildAsync(workspace);
            WorkspaceNavigationNode projectNode = All(tree).Single(node =>
                node.Id == complex.Project.Id);
            WorkspaceNavigationNode docsNode = All(projectNode).Single(node =>
                node.Id == documentation.Id);
            WorkspaceNavigationNode techNode = All(docsNode).Single(node =>
                node.Id == technical.Id);
            WorkspaceNavigationNode architectureNode = All(techNode).Single(node =>
                node.Id == architecture.Id);

            Assert(techNode.FullPath == technical.DirectoryPath &&
                architectureNode.FullPath == architecture.DirectoryPath &&
                projectNode.Children.Count(child => child.DisplayName == "Documentation") == 1 &&
                projectNode.Children.All(child => child.Id != technical.Id),
                "Explorer must nest sections with stable manifest identities, not duplicate roots.");

            WorkspaceProjectExportService exporter = new(workspace);
            global::Nodalis.Core.Exporting.ProjectExportPreview exportPreview =
                await exporter.PreparePreviewAsync(project);
            Assert(exportPreview.Sections.Single(section => section.Name == "Jalons")
                    .DocumentCount == 1 &&
                exportPreview.Sections.Single(section => section.Name == "Management")
                    .DocumentCount == 0 &&
                exportPreview.Sections.Single(section => section.Name == "Documentation")
                    .DocumentCount == 1 &&
                exportPreview.Sections.Single(section => section.Name == "Technique")
                    .DocumentCount == 1 &&
                exportPreview.MarkdownDocumentCount ==
                    Directory.EnumerateFiles(project, "*.md",
                        SearchOption.AllDirectories).Count() - 2,
                "Project export preview must count nested documents once and avoid child duplication.");

            string source = Path.Combine(technical.DirectoryPath, "Scenario.md");
            await File.WriteAllTextAsync(source, "# Scenario\n");
            Guid riskSectionId = state.Sections.Single(section => section.Name == "Risques").Id;
            string moved = await structure.MoveDocumentAsync(project, source, riskSectionId);
            Assert(!File.Exists(source) &&
                File.Exists(moved) &&
                Path.GetDirectoryName(moved) ==
                    Path.Combine(project, "Analyse", "Risques"),
                "Moving a nested Markdown document must target its real source/target folder.");

            bool parentDeleteRejected = false;
            try
            {
                await structure.RemoveSectionAsync(project, documentation.Id);
            }
            catch (InvalidOperationException)
            {
                parentDeleteRejected = true;
            }

            Assert(parentDeleteRejected &&
                Directory.Exists(documentation.DirectoryPath),
                "Deleting a parent containing declared child sections must be refused.");

            ProjectCreationResult medium = await creator.CreateAsync(
                new ProjectCreationRequest
                {
                    Name = "Moyen historique",
                    Complexity = ProjectComplexity.Medium,
                    Target = destination
                });

            MeetingCreationResult mediumMeeting = await meetingService.CreateAsync(
                medium.ProjectDirectory,
                new MeetingDraft { Title = "Comité moyen" });
            DecisionCreationResult mediumDecision = await decisionService.CreateAsync(
                medium.ProjectDirectory,
                new DecisionDraft
                {
                    Title = "Décision moyenne",
                    Decision = "Préserver la structure existante."
                });

            Assert(Path.GetDirectoryName(mediumMeeting.FilePath) ==
                    Path.Combine(medium.ProjectDirectory, "Réunions") &&
                Path.GetDirectoryName(mediumDecision.FilePath) ==
                    Path.Combine(medium.ProjectDirectory, "Décisions") &&
                (await decisionService.GetDecisionsAsync(medium.ProjectDirectory)).Count == 1,
                "Older project profiles must retain root meeting and decision collections.");

            ProjectCreationResult ultra = await creator.CreateAsync(new ProjectCreationRequest
            {
                Name = "Ultra-simple",
                Complexity = ProjectComplexity.UltraSimple,
                Target = destination
            });
            Assert(ultra.Project.Sections.Count == 0 &&
                Directory.GetDirectories(ultra.ProjectDirectory).Length == 0 &&
                Directory.GetFiles(ultra.ProjectDirectory).Length == 3,
                "Ultra-simple must retain only its three original root files.");

            ProjectProfileDefinition oldBuiltIn = LegacyComplexProfile();
            ProjectProfileCatalog legacy = await templates.LoadProjectProfilesAsync();
            await templates.SaveProjectProfilesAsync(legacy with
            {
                Profiles = legacy.Profiles.Select(profile =>
                    profile.Complexity == ProjectComplexity.Complex
                        ? oldBuiltIn : profile).ToList()
            });
            await templates.InitializeDefaultsAsync();
            ProjectProfileDefinition upgraded = (await templates.LoadProjectProfilesAsync())
                .Profiles.Single(profile => profile.Complexity == ProjectComplexity.Complex);
            Assert(upgraded.Sections.Any(section =>
                    section.RelativeDirectory == "Documentation/Technique") &&
                upgraded.Sections.Single(section => section.Name == "Jalons")
                    .RelativeDirectory == "Management" &&
                File.Exists(Path.Combine(project, "Documentation", "Technique", "Technique.md")),
                "An untouched 1.9 Complex profile must upgrade without migrating existing projects.");

            ProjectProfileDefinition earlyV2 = upgraded with
            {
                Sections = upgraded.Sections.Select(section =>
                    section.TemplateKey == "milestones"
                        ? section with { RelativeDirectory = null }
                        : section).ToList()
            };
            ProjectProfileCatalog interim = await templates.LoadProjectProfilesAsync();
            await templates.SaveProjectProfilesAsync(interim with
            {
                Profiles = interim.Profiles.Select(profile =>
                    profile.Complexity == ProjectComplexity.Complex
                        ? earlyV2 : profile).ToList()
            });
            await templates.InitializeDefaultsAsync();

            ProjectProfileDefinition upgradedEarlyV2 = (await templates.LoadProjectProfilesAsync())
                .Profiles.Single(profile => profile.Complexity == ProjectComplexity.Complex);
            Assert(upgradedEarlyV2.Sections.Single(section =>
                    section.TemplateKey == "milestones").RelativeDirectory == "Management",
                "An untouched early v2 Complex profile must receive the new milestone location.");

            ProjectProfileDefinition customized = oldBuiltIn with
            {
                DisplayName = "Complexe personnalisé"
            };
            ProjectProfileCatalog fresh = await templates.LoadProjectProfilesAsync();
            await templates.SaveProjectProfilesAsync(fresh with
            {
                Profiles = fresh.Profiles.Select(profile =>
                    profile.Complexity == ProjectComplexity.Complex
                        ? customized : profile).ToList()
            });
            await templates.InitializeDefaultsAsync();
            ProjectProfileDefinition retained = (await templates.LoadProjectProfilesAsync())
                .Profiles.Single(profile => profile.Complexity == ProjectComplexity.Complex);
            Assert(retained.DisplayName == "Complexe personnalisé" &&
                retained.Sections.All(section => section.RelativeDirectory is null),
                "User-customized legacy project profiles must never be silently replaced.");

            bool traversalRejected = false;
            try
            {
                ProjectSectionDirectoryLayout.Resolve(project, "Escape", "../Escape");
            }
            catch (InvalidDataException)
            {
                traversalRejected = true;
            }

            Assert(traversalRejected,
                "Explicit paths must never traverse outside the project.");
        }
        finally
        {
            if (Directory.Exists(workspace))
            {
                Directory.Delete(workspace, recursive: true);
            }
        }
    }

    /// <summary>Returns all nodes of one workspace navigation tree.</summary>
    /// <param name="node">Root navigation node.</param>
    /// <returns>Tree nodes, including root.</returns>
    private static IEnumerable<WorkspaceNavigationNode> All(WorkspaceNavigationNode node)
    {
        yield return node;
        foreach (WorkspaceNavigationNode child in node.Children)
        {
            foreach (WorkspaceNavigationNode nested in All(child))
            {
                yield return nested;
            }
        }
    }

    /// <summary>Reconstructs the exact shipped legacy Complex definition.</summary>
    /// <returns>Original v1.9 built-in project profile.</returns>
    private static ProjectProfileDefinition LegacyComplexProfile()
    {
        (string Name, int Order, string? Key, bool Singleton)[] definitions =
        [
            ("Jalons", 10, "milestones", true),
            ("Documentation", 20, "documentation", true),
            ("Fonctionnel", 30, "functional", true),
            ("Technique", 40, "technical-complex", true),
            ("Exploitation", 50, "operations", true),
            ("Déploiement", 60, "deployment", true),
            ("Glossaire", 70, "glossary", true),
            ("Tests", 80, "tests-complex", true),
            ("Réunions", 90, null, false),
            ("Décisions", 100, null, false),
            ("Risques", 110, "risks", true),
            ("Dépendances", 120, "dependencies", true)
        ];

        return new ProjectProfileDefinition
        {
            Complexity = ProjectComplexity.Complex,
            DisplayName = "Complexe",
            Sections = definitions.Select(definition => new ProjectSectionTemplateDefinition
            {
                Name = definition.Name,
                Order = definition.Order,
                TemplateKey = definition.Key,
                IsSingleton = definition.Singleton
            }).ToList()
        };
    }

    /// <summary>Reports a failing project layout regression.</summary>
    /// <param name="condition">Invariant.</param>
    /// <param name="message">Failure detail.</param>
    private static void Assert(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
