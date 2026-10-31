using Nodalis.Core.Domain;
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

            Assert(File.Exists(Path.Combine(project, "Jalons.md")) &&
                File.Exists(Path.Combine(project, "Glossaire.md")) &&
                File.Exists(Path.Combine(project, "Notes rapides.md")) &&
                !Directory.Exists(Path.Combine(project, "Technique")) &&
                !Directory.Exists(Path.Combine(project, "Dépendances")) &&
                !Directory.Exists(Path.Combine(project, "Déploiement")),
                "Complex must preserve root singleton files and avoid legacy duplicate folders.");

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
                File.Exists(Path.Combine(project, "Documentation", "Technique", "Technique.md")),
                "An untouched 1.9 Complex profile must upgrade without migrating existing projects.");

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
