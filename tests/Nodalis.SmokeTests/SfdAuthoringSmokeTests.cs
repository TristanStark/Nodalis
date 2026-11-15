using Nodalis.Core.Domain;
using Nodalis.Core.Projects;
using Nodalis.Core.Sfd;
using Nodalis.Infrastructure.Persistence;
using Nodalis.Infrastructure.Projects;

/// <summary>End-to-end authoring regressions using exclusively fictitious SFD data.</summary>
internal static class SfdAuthoringSmokeTests
{
    /// <summary>
    /// Verifies versioned source identity, parent GUIDs, distinct EXU/EXS/EXR
    /// formalism, FCT modes, incomplete drafts and safe error behavior.
    /// </summary>
    /// <param name="root">Temporary suite root.</param>
    /// <returns>Completed filesystem smoke checks.</returns>
    public static async Task RunAsync(string root)
    {
        string workspace = Path.Combine(root, "Sfd-authoring");
        await new FileSystemWorkspaceStore(workspace).InitializeAsync("SFD fictif");
        string app = Path.Combine(workspace, "Applications", "Application fictive");
        Directory.CreateDirectory(app);
        ProjectCreationTarget target = new()
        {
            ApplicationId = Guid.NewGuid(),
            ApplicationName = "Application fictive",
            ParentDirectory = app,
            DisplayName = "Application fictive"
        };

        FileSystemProjectCreator projectCreator = new(workspace);
        ProjectCreationResult project = await projectCreator.CreateAsync(
            new ProjectCreationRequest
            {
                Name = "Processus fictif",
                Kind = ProjectKind.WithSpecifications,
                Complexity = ProjectComplexity.Simple,
                Target = target
            });
        SfdDocumentRegistryService registry = new(workspace);
        SfdDocumentCatalog empty = await registry.ListAsync(project.ProjectDirectory);
        Require(empty.Entries.Count == 0 && empty.Diagnostics.Count == 0,
            "New SFD project must have no invented business objects.");

        SfdDocumentEntry process = await registry.CreateAsync(project.ProjectDirectory,
            SfdDocumentKind.Process, "Gérer des demandes fictives");
        SfdDocumentEntry activity = await registry.CreateAsync(project.ProjectDirectory,
            SfdDocumentKind.Activity, "Contrôler la demande", process.Card.Id);
        SfdDocumentEntry user = await registry.CreateAsync(project.ProjectDirectory,
            SfdDocumentKind.UserRequirement, "Consulter le résultat", activity.Card.Id);
        SfdDocumentEntry system = await registry.CreateAsync(project.ProjectDirectory,
            SfdDocumentKind.SystemRequirement, "Produire un accusé fictif", activity.Card.Id);
        SfdDocumentEntry regulatory = await registry.CreateAsync(project.ProjectDirectory,
            SfdDocumentKind.RegulatoryRequirement, "Conserver une preuve à confirmer",
            activity.Card.Id);
        SfdDocumentEntry ui = await registry.CreateAsync(project.ProjectDirectory,
            SfdDocumentKind.Functionality, "Page de consultation", functionalityMode:
                SfdFunctionalityMode.WithInterface);
        SfdDocumentEntry noUi = await registry.CreateAsync(project.ProjectDirectory,
            SfdDocumentKind.Functionality, "Générer un fichier fictif", functionalityMode:
                SfdFunctionalityMode.WithoutInterface);

        Require(process.Card.Reference == "PM-0001" &&
            activity.Card.Reference == "ACT-0001" &&
            user.Card.Reference == "EXU-0001" &&
            system.Card.Reference == "EXS-0001" &&
            regulatory.Card.Reference == "EXR-0001" &&
            ui.Card.Reference == "FCT-0001" &&
            noUi.Card.Reference == "FCT-0002",
            "Distinct SFD roles must get stable, project-wide references.");
        Require(activity.Card.ParentId == process.Card.Id &&
            user.Card.ParentId == activity.Card.Id,
            "SFD parent relationship must be stored as stable GUIDs.");
        Require(ui.Card.FunctionalityMode == SfdFunctionalityMode.WithInterface &&
            noUi.Card.FunctionalityMode == SfdFunctionalityMode.WithoutInterface,
            "FCT must retain exclusive with/sans IHM mode.");

        string exuMarkdown = await File.ReadAllTextAsync(user.AbsolutePath);
        string exsMarkdown = await File.ReadAllTextAsync(system.AbsolutePath);
        string exrMarkdown = await File.ReadAllTextAsync(regulatory.AbsolutePath);
        Require(exuMarkdown.Contains(
                "Quand [condition], l'utilisateur doit pouvoir [action / résultat].",
                StringComparison.Ordinal) &&
            exuMarkdown.Contains(
                "En tant que [RÔLE], je veux [ACTION] afin de [BUT / BÉNÉFICE] sur [CANAL À CONFIRMER].",
                StringComparison.Ordinal) &&
            exsMarkdown.Contains(
                "Quand [condition], le système doit permettre de [action / résultat].",
                StringComparison.Ordinal) &&
            exrMarkdown.Contains(
                "Quand [condition], le système doit respecter [obligation applicable].",
                StringComparison.Ordinal) &&
            exrMarkdown.Contains("Source réglementaire applicable",
                StringComparison.Ordinal) &&
            !exuMarkdown.Contains("<!-- nodalis-sfd-condition:", StringComparison.Ordinal),
            "EXU/EXS/EXR drafts need distinct confirmed patterns but no fake C/SC/RM.");
        SfdDocumentCard parsed = SfdDocumentMarkdownCodec.Parse(exuMarkdown);
        Require(parsed == user.Card && parsed.Certainty == SfdAssertionCertainty.ToConfirm,
            "The source Markdown must round-trip the original uncertain draft identity.");

        bool badParentRefused = false;
        try
        {
            await registry.CreateAsync(project.ProjectDirectory,
                SfdDocumentKind.RegulatoryRequirement, "Rattachement invalide",
                process.Card.Id);
        }
        catch (InvalidDataException)
        {
            badParentRefused = true;
        }

        Require(badParentRefused,
            "An EXR cannot be attached directly to a process.");

        bool missingModeRefused = false;
        try
        {
            await registry.CreateAsync(project.ProjectDirectory,
                SfdDocumentKind.Functionality, "Mode absent");
        }
        catch (InvalidDataException)
        {
            missingModeRefused = true;
        }

        Require(missingModeRefused,
            "FCT requires an explicit with/sans IHM category.");

        string processFolder = Path.GetDirectoryName(process.AbsolutePath)!;
        Directory.Move(processFolder, processFolder + " renommé");
        SfdDocumentCatalog renamed = await registry.ListAsync(project.ProjectDirectory);
        Require(renamed.Diagnostics.Count == 0 && renamed.Entries.Count == 7 &&
            renamed.Entries.Single(item => item.Card.Id == user.Card.Id)
                .Card.ParentId == activity.Card.Id,
            "Renaming a PM directory must preserve the full identity/parent chain.");
        SfdDocumentEntry anotherActivity = await registry.CreateAsync(
            project.ProjectDirectory, SfdDocumentKind.Activity,
            "Préparer la suite", process.Card.Id);
        Require(anotherActivity.Card.Reference == "ACT-0002" &&
            anotherActivity.AbsolutePath.Contains("renommé", StringComparison.Ordinal),
            "New ACT must resolve its PM using GUID, after a directory rename.");

        string copied = Path.Combine(Path.GetDirectoryName(user.AbsolutePath)!,
            "EXU-9999 - clone.md");
        await File.WriteAllTextAsync(copied, exuMarkdown);
        SfdDocumentCatalog conflicted = await registry.ListAsync(project.ProjectDirectory);
        Require(conflicted.Diagnostics.Any(item =>
                item.Contains("dupliqué", StringComparison.OrdinalIgnoreCase)),
            "Duplicated document IDs and references must be reported.");
        bool conflictRefused = false;
        try
        {
            await registry.CreateAsync(project.ProjectDirectory,
                SfdDocumentKind.Process, "Projet en conflit");
        }
        catch (InvalidDataException)
        {
            conflictRefused = true;
        }

        Require(conflictRefused,
            "Ambiguous identity must prevent creating a new SFD document.");
        File.Delete(copied);

        ProjectCreationResult standard = await projectCreator.CreateAsync(
            new ProjectCreationRequest
            {
                Name = "Projet standard fictif",
                Complexity = ProjectComplexity.Simple,
                Target = target
            });
        bool standardRefused = false;
        try
        {
            await registry.ListAsync(standard.ProjectDirectory);
        }
        catch (InvalidDataException)
        {
            standardRefused = true;
        }

        Require(standardRefused,
            "The SFD registry must never operate on a Standard 2.0 project.");
    }

    /// <summary>Checks one expected filesystem or business invariant.</summary>
    /// <param name="condition">Expected state.</param>
    /// <param name="message">Failure diagnostic.</param>
    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
