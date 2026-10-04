using Nodalis.Core.Domain;
using Nodalis.Core.Validation;
using Nodalis.Infrastructure.Persistence;

var root = Path.Combine(
    Path.GetTempPath(),
    "Nodalis-SmokeTests",
    Guid.NewGuid().ToString("N"));

try
{
    var store = new FileSystemWorkspaceStore(root);
    var created = await store.InitializeAsync("Smoke Test Workspace");
    var loaded = await store.LoadAsync();

    Assert(created.Id == loaded.Id, "Workspace identity must survive persistence.");
    Assert(File.Exists(Path.Combine(root, WorkspaceLayout.GlobalQuickNotesFileName)),
        "Global quick notes must be created.");
    Assert(File.Exists(Path.Combine(root, WorkspaceLayout.GlobalGlossaryFileName)),
        "Global glossary must be created.");
    Assert(WindowsPathRules.SanitizeSegment("CON") == "_CON",
        "Reserved Windows names must be escaped.");

    var application = new ApplicationManifest
    {
        Id = Guid.NewGuid(),
        Name = "Application A"
    };

    var module = new ModuleManifest
    {
        Id = Guid.NewGuid(),
        ApplicationId = application.Id,
        Name = "Module A1"
    };

    HierarchyValidator.ValidateModule(module, [application], []);

    var project = new ProjectManifest
    {
        Id = Guid.NewGuid(),
        Name = "Projet Patate",
        ApplicationId = application.Id,
        ModuleId = module.Id,
        InitialComplexity = ProjectComplexity.Simple
    };

    HierarchyValidator.ValidateProject(project, [application], [module], []);

    Console.WriteLine("Nodalis smoke tests passed.");
    return;
}
finally
{
    if (Directory.Exists(root))
    {
        Directory.Delete(root, recursive: true);
    }
}

static void Assert(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}
