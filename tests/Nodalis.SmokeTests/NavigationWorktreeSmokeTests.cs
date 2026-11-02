using Nodalis.Core.Links;
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
        VerifyDocumentNavigationLookup();
        VerifyNavigationLinkTargetLookup();
        VerifyNavigationFilters(root, applicationA, applicationB,
            projectA, projectFolder, deepest, document, ultraSimple);
    }

    /// <summary>
    /// Validates an indexed resolution that preserves the first-match rule
    /// when a legacy document ID or path is duplicated across source nodes.
    /// The test uses only synthetic in-memory navigation nodes.
    /// </summary>
    private static void VerifyDocumentNavigationLookup()
    {
        Guid sharedId = Guid.NewGuid();
        WorkspaceNavigationNode first = Node(
            "Fichier A", WorkspaceNodeKind.Document,
            "C:/Fixture/Applications/AppA/Projets/Test/Note.md");
        WorkspaceNavigationNode second = Node(
            "Fichier B", WorkspaceNodeKind.Document,
            "C:/Fixture/Applications/AppB/Projets/Test/Note.md");
        WorkspaceNavigationNode third = Node(
            "Fichier C", WorkspaceNodeKind.Document,
            "C:/Fixture/Applications/AppC/Projets/Test/Note.md");
        WorkspaceNavigationNode fourth = Node(
            "Fichier D", WorkspaceNodeKind.Document,
            "C:/Fixture/Applications/AppD/Projets/Test/Note.md");
        // IDs on the synthetic navigation model are init-only; index
        // selectors intentionally simulate duplicate legacy identities.
        DocumentNavigationIndex<WorkspaceNavigationNode> index = new(
            new[] { first, second, third, fourth },
            node => ReferenceEquals(node, first) || ReferenceEquals(node, second)
                ? sharedId : node.Id,
            node => ReferenceEquals(node, third)
                ? second.FullPath : node.FullPath);
        Assert(ReferenceEquals(index.Resolve(sharedId, second.FullPath), first),
            "The first ID match precedes later path matches.");
        Assert(ReferenceEquals(index.Resolve(third.Id, second.FullPath), second),
            "The first path match precedes later ID matches.");
        Assert(ReferenceEquals(index.Resolve(Guid.NewGuid(),
                first.FullPath.ToUpperInvariant()), first),
            "Path comparison must remain case-insensitive.");
        Assert(ReferenceEquals(index.ResolveById(sharedId), first) &&
            index.ResolveById(Guid.NewGuid()) is null &&
            index.Resolve(Guid.NewGuid(), "C:/Fixture/Missing.md") is null,
            "Missing identities and paths must never guess a tab target.");
        Assert(ReferenceEquals(index.Resolve(fourth.Id, fourth.FullPath), fourth),
            "Unambiguous documents must resolve to their own source.");
    }

    /// <summary>
    /// Verifies one snapshot lookup for document targets, nested projects and
    /// duplicate legacy paths, without any real workspace or network access.
    /// </summary>
    private static void VerifyNavigationLinkTargetLookup()
    {
        Guid duplicate = Guid.NewGuid();
        LinkTargetEntry project = new()
        {
            Id = duplicate,
            Kind = LinkTargetKind.Project,
            DisplayName = "Projet fictif",
            QualifiedName = "Application A / Projet fictif",
            RelativePath = "Applications/AppA/Projets/Projet"
        };
        LinkTargetEntry subproject = new()
        {
            Id = Guid.NewGuid(),
            Kind = LinkTargetKind.Project,
            DisplayName = "Sous-projet fictif",
            QualifiedName = "Application A / Sous-projet fictif",
            RelativePath = "Applications/AppA/Projets/Projet/Sous-projets/Suite"
        };
        LinkTargetEntry document = new()
        {
            Id = Guid.NewGuid(),
            Kind = LinkTargetKind.Document,
            DisplayName = "Dossier fictif",
            QualifiedName = "Application A / Document",
            RelativePath = "Applications/AppA/Projets/Projet/Notes.md"
        };
        LinkTargetEntry duplicateDocument = new()
        {
            Id = duplicate,
            Kind = LinkTargetKind.Document,
            DisplayName = "Document en collision",
            QualifiedName = "Application B / Document",
            RelativePath = document.RelativePath
        };
        NavigationLinkTargetIndex index = new(
            [project, subproject, document, duplicateDocument]);
        Assert(ReferenceEquals(index.FindById(duplicate), project) &&
            ReferenceEquals(index.FindDocumentByRelativePath(
                document.RelativePath.ToUpperInvariant()), document),
            "First indexed ID/path hits retain historical source precedence.");
        Assert(ReferenceEquals(index.FindNearestProject(
                "Applications/AppA/Projets/Projet/Sous-projets/Suite/Test.md"),
                subproject) &&
            ReferenceEquals(index.FindNearestProject(document.RelativePath),
                project),
            "Nearest project ancestry must use the longest matching path.");
        Assert(index.FindById(Guid.NewGuid()) is null &&
            index.FindDocumentByRelativePath("Other/Missing.md") is null &&
            index.FindNearestProject("Notes.md") is null,
            "Global and missing paths must not fabricate a project target.");
    }

    /// <summary>
    /// Verifies a case-insensitive search, preserved ancestors, branch match,
    /// application-only scope and instant restoration without mutations.
    /// </summary>
    /// <param name="root">Fictional workspace.</param>
    /// <param name="appA">First application.</param>
    /// <param name="appB">Second application.</param>
    /// <param name="project">One nested project.</param>
    /// <param name="folder">A descendant folder.</param>
    /// <param name="document">Its deep document.</param>
    /// <param name="globalNote">Note outside applications.</param>
    /// <param name="ultraSimple">A second project.</param>
    private static void VerifyNavigationFilters(
        WorkspaceNavigationNode root,
        WorkspaceNavigationNode appA,
        WorkspaceNavigationNode appB,
        WorkspaceNavigationNode project,
        WorkspaceNavigationNode folder,
        WorkspaceNavigationNode document,
        WorkspaceNavigationNode globalNote,
        WorkspaceNavigationNode ultraSimple)
    {
        IReadOnlySet<Guid> exact =
            NavigationFilterProjection.GetVisibleIds(root, "EXTRÊMEMENT", null);
        Assert(exact.Contains(root.Id) &&
            exact.Contains(appA.Id) &&
            exact.Contains(project.Id) &&
            exact.Contains(folder.Id) &&
            exact.Contains(document.Id) &&
            !exact.Contains(appB.Id) &&
            !exact.Contains(ultraSimple.Id),
            "Search preserves matching document ancestors only.");

        IReadOnlySet<Guid> branch =
            NavigationFilterProjection.GetVisibleIds(root, "proj-001", null);
        Assert(branch.Contains(project.Id) &&
            branch.Contains(document.Id) &&
            !branch.Contains(ultraSimple.Id),
            "Matching a project name includes its complete subtree.");

        IReadOnlySet<Guid> scoped =
            NavigationFilterProjection.GetVisibleIds(root, string.Empty, appA.Id);
        Assert(scoped.Contains(appA.Id) &&
            scoped.Contains(project.Id) &&
            scoped.Contains(ultraSimple.Id) &&
            !scoped.Contains(appB.Id) &&
            !scoped.Contains(globalNote.Id),
            "Application scope hides other applications and Global without mutation.");

        IReadOnlySet<Guid> scopedSearch =
            NavigationFilterProjection.GetVisibleIds(root, "technique", appA.Id);
        Assert(scopedSearch.Contains(folder.Id) &&
            !scopedSearch.Contains(appB.Id),
            "Scoped search composes with ancestors preserved.");

        IReadOnlySet<Guid> cleared =
            NavigationFilterProjection.GetVisibleIds(root, "   ", null);
        Assert(cleared.Contains(globalNote.Id) &&
            cleared.Contains(appA.Id) &&
            cleared.Contains(appB.Id) &&
            cleared.Contains(ultraSimple.Id) &&
            cleared.Contains(document.Id),
            "Clearing the search restores all nodes.");

        Assert(root.Children.Count == 2 &&
            appA.Children[0].Kind == WorkspaceNodeKind.ProjectsRoot &&
            project.Children[0].FullPath == folder.FullPath,
            "P1 filtering never alters the canonical workspace hierarchy.");
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
