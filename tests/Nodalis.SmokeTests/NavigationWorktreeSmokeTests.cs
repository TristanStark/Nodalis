using Nodalis.Core.Navigation;

internal static class NavigationWorktreeSmokeTests
{
    /// <summary>
    /// Confirms that the worktree hides only visual grouping nodes and keeps
    /// canonical identifiers, paths, siblings and deep project content intact.
    /// </summary>
    public static void Run()
    {
        WorkspaceNavigationNode global = Node(
            "Global", WorkspaceNodeKind.Global, "C:/Workspace");
        WorkspaceNavigationNode document = Node(
            "Notes rapides", WorkspaceNodeKind.Document,
            "C:/Workspace/Notes rapides.md");
        global.Children.Add(document);

        WorkspaceNavigationNode applicationA = Node(
            "Application A", WorkspaceNodeKind.Application,
            "C:/Workspace/Applications/AppA");
        WorkspaceNavigationNode applicationB = Node(
            "Application B", WorkspaceNodeKind.Application,
            "C:/Workspace/Applications/AppB");
        WorkspaceNavigationNode projectA = Node(
            "PROJ-001", WorkspaceNodeKind.Project,
            "C:/Workspace/Applications/AppA/Projets/PROJ-001");
        WorkspaceNavigationNode ultraSimple = Node(
            "Projet Bêta", WorkspaceNodeKind.Project,
            "C:/Workspace/Applications/AppA/Projets/Projet-Beta");
        WorkspaceNavigationNode projectFolder = Node(
            "Technique", WorkspaceNodeKind.Section,
            projectA.FullPath + "/Technique");
        WorkspaceNavigationNode deepest = Node(
            "Document technique extrêmement long", WorkspaceNodeKind.Document,
            projectFolder.FullPath + "/Document.md");
        projectFolder.Children.Add(deepest);
        projectA.Children.Add(projectFolder);
        ultraSimple.Children.Add(Node(
            "Fiche de Maintenance", WorkspaceNodeKind.Document,
            ultraSimple.FullPath + "/Fiche de Maintenance.md"));

        WorkspaceNavigationNode projectsRoot = Node(
            "Projets", WorkspaceNodeKind.ProjectsRoot,
            applicationA.FullPath + "/Projets");
        projectsRoot.Children.Add(projectA);
        projectsRoot.Children.Add(ultraSimple);
        projectsRoot.Children.Add(Node(
            "Projet Gamma", WorkspaceNodeKind.Project,
            applicationA.FullPath + "/Projets/Gamma"));
        projectsRoot.Children.Add(Node(
            "Projet Delta", WorkspaceNodeKind.Project,
            applicationA.FullPath + "/Projets/Delta"));
        projectsRoot.Children.Add(Node(
            "Projet Epsilon", WorkspaceNodeKind.Project,
            applicationA.FullPath + "/Projets/Epsilon"));
        applicationA.Children.Add(projectsRoot);
        applicationA.Children.Add(Node(
            "Documentation", WorkspaceNodeKind.Section,
            applicationA.FullPath + "/Documentation"));
        WorkspaceNavigationNode applicationRoot = Node(
            "Applications", WorkspaceNodeKind.ApplicationsRoot,
            "C:/Workspace/Applications");
        applicationRoot.Children.Add(applicationA);
        applicationRoot.Children.Add(applicationB);
        WorkspaceNavigationNode root = Node(
            "Nodalis", WorkspaceNodeKind.Workspace, "C:/Workspace");
        root.Children.Add(global);
        root.Children.Add(applicationRoot);

        IReadOnlyList<WorkspaceNavigationNode> top =
            NavigationPresentationRules.GetVisibleChildren(root);
        IReadOnlyList<WorkspaceNavigationNode> applicationChildren =
            NavigationPresentationRules.GetVisibleChildren(applicationA);
        IReadOnlyList<WorkspaceNavigationNode> nested =
            NavigationPresentationRules.GetVisibleChildren(projectA);

        Assert(top.Count == 3 &&
            ReferenceEquals(top[0], global) &&
            ReferenceEquals(top[1], applicationA) &&
            ReferenceEquals(top[2], applicationB),
            "Global and applications must show without root/Applications rows.");
        Assert(applicationChildren.Count == 6 &&
            ReferenceEquals(applicationChildren[0], projectA) &&
            ReferenceEquals(applicationChildren[1], ultraSimple) &&
            applicationChildren[5].DisplayName == "Documentation",
            "ProjectsRoot is a visual grouping only; other folders survive.");
        Assert(nested.Count == 1 &&
            ReferenceEquals(nested[0], projectFolder) &&
            ReferenceEquals(
                NavigationPresentationRules.GetVisibleChildren(projectFolder).Single(),
                deepest),
            "Deep document descendants must remain accessible.");
        Assert(root.Children.Count == 2 &&
            ReferenceEquals(root.Children[1], applicationRoot) &&
            ReferenceEquals(applicationA.Children[0], projectsRoot) &&
            projectA.Children[0].FullPath == projectFolder.FullPath,
            "Display projection must not mutate canonical business hierarchy.");
        Assert(!NavigationPresentationRules.IsTransparentContainer(
                WorkspaceNodeKind.Module) &&
            !NavigationPresentationRules.IsTransparentContainer(
                WorkspaceNodeKind.Section) &&
            !NavigationPresentationRules.IsTransparentContainer(
                WorkspaceNodeKind.Project),
            "Modules, real sections and projects may never be hidden.");
        Assert(ultraSimple.Children.Count == 1 &&
            ultraSimple.Children[0].DisplayName == "Fiche de Maintenance",
            "Ultra-simple files must remain accessible.");
        VerifyStickyHeaders();
    }

    /// <summary>
    /// Tests project/folder pinning, switchovers and the absence of redundant
    /// headers when the title is still within the scroll viewport.
    /// </summary>
    private static void VerifyStickyHeaders()
    {
        StickyWorktreeHeading app = new StickyWorktreeHeading(
            WorkspaceNodeKind.Application, true);
        StickyWorktreeHeading project = new StickyWorktreeHeading(
            WorkspaceNodeKind.Project, true);
        StickyWorktreeHeading visibleProject = new StickyWorktreeHeading(
            WorkspaceNodeKind.Project, false);
        StickyWorktreeHeading folder = new StickyWorktreeHeading(
            WorkspaceNodeKind.Section, true);
        StickyWorktreeHeading nestedFolder = new StickyWorktreeHeading(
            WorkspaceNodeKind.Folder, true);
        StickyWorktreeHeading document = new StickyWorktreeHeading(
            WorkspaceNodeKind.Document, false);
        StickyWorktreeHeading visibleFolder = new StickyWorktreeHeading(
            WorkspaceNodeKind.Section, false);

        Assert(StickyWorktreeContext.SelectPinnedIndices(
                new[] { app, project, folder, document }).SequenceEqual(
                new[] { 1, 2 }),
            "A scrolled project and its scrolled folder must both stay pinned.");
        Assert(StickyWorktreeContext.SelectPinnedIndices(
                new[] { app, project, folder, nestedFolder, document })
                .SequenceEqual(new[] { 1, 3 }),
            "Nested folders must show the deepest active folder, not all levels.");
        Assert(StickyWorktreeContext.SelectPinnedIndices(
                new[] { app, project, visibleFolder, document })
                .SequenceEqual(new[] { 1 }),
            "Folder heading must not be duplicated while visible.");
        Assert(StickyWorktreeContext.SelectPinnedIndices(
                new[] { app, visibleProject, document }).Count == 0,
            "When a new project heading is visible, no unrelated title is pinned.");
        Assert(StickyWorktreeContext.SelectPinnedIndices(
                new[] { app, folder, document })
                .SequenceEqual(new[] { 0, 1 }),
            "Standalone application folders keep their application context.");
        Assert(StickyWorktreeContext.SelectPinnedIndices(
                new[] { visibleProject, visibleFolder, document }).Count == 0,
            "No sticky overlay is needed for fully visible headings.");
        Assert(StickyWorktreeContext.SelectPinnedIndices(
                new[] { document }).Count == 0,
            "A top-level document must not get a fabricated sticky heading.");
        // Position paths are root-to-leaf indices of visible tree siblings:
        // [1, 0] is the selected project, [1, 0, 2] its Risks folder.
        Assert(StickyWorktreeContext.ShouldPinCurrentHeading(
                new[] { 1, 0 }, new[] { 1, 0, 2, 1 }, false),
            "The active project stays pinned when its own descendants are on screen.");
        Assert(StickyWorktreeContext.ShouldPinCurrentHeading(
                new[] { 1, 0, 2 }, new[] { 1, 0, 5 }, false),
            "The active Risks folder stays pinned beyond its sibling Glossary.");
        Assert(StickyWorktreeContext.ShouldPinCurrentHeading(
                new[] { 1, 0 }, new[] { 2, 0, 0 }, false),
            "The active project stays pinned after scrolling into another application.");
        Assert(StickyWorktreeContext.ShouldPinCurrentHeading(
                new[] { 1, 0, 2 }, new[] { 2, 0, 0 }, false),
            "The active folder stays pinned even across application boundaries.");
        Assert(!StickyWorktreeContext.ShouldPinCurrentHeading(
                new[] { 1, 0, 2 }, new[] { 0, 0 }, false),
            "Headings below the viewport must not pin prematurely.");
        Assert(!StickyWorktreeContext.ShouldPinCurrentHeading(
                new[] { 1, 0, 2 }, new[] { 1, 0, 2 }, false),
            "A heading at the top of the viewport must not be duplicated.");
        Assert(!StickyWorktreeContext.ShouldPinCurrentHeading(
                new[] { 1, 0, 2 }, new[] { 1, 0, 3 }, true),
            "A heading already visible on screen must not be duplicated.");
        Assert(!StickyWorktreeContext.ShouldPinCurrentHeading(
                Array.Empty<int>(), new[] { 2, 0 }, false),
            "Unrealized/stale tree nodes must not become pinned.");
        Assert(!StickyWorktreeContext.ShouldPinCurrentHeading(
                new[] { 1, 0 }, Array.Empty<int>(), false),
            "No sticky heading is inferred from a missing visible row.");
    }

    /// <summary>Creates a canonical test node without relying on real workspaces.</summary>
    /// <param name="name">Fictional label.</param>
    /// <param name="kind">Business node type.</param>
    /// <param name="path">Fictional path.</param>
    /// <returns>Fresh canonical node.</returns>
    private static WorkspaceNavigationNode Node(
        string name,
        WorkspaceNodeKind kind,
        string path) =>
        new WorkspaceNavigationNode
        {
            Id = Guid.NewGuid(),
            DisplayName = name,
            Kind = kind,
            FullPath = path
        };

    /// <summary>Fails a smoke-test invariant.</summary>
    /// <param name="condition">Expected condition.</param>
    /// <param name="message">Failure detail.</param>
    private static void Assert(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
