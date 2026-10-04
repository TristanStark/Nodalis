using Nodalis.Core.Templates;

namespace Nodalis.Core.Abstractions;

public interface ITemplateStore
{
    Task InitializeDefaultsAsync(
        CancellationToken cancellationToken = default);

    Task<TemplateCatalog> LoadTemplateCatalogAsync(
        CancellationToken cancellationToken = default);

    Task<ProjectProfileCatalog> LoadProjectProfilesAsync(
        CancellationToken cancellationToken = default);

    Task<string> RenderAsync(
        string templateKey,
        IReadOnlyDictionary<string, string> variables,
        CancellationToken cancellationToken = default);
}
