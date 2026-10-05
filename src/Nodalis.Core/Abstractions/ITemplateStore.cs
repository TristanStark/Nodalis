using Nodalis.Core.Templates;

namespace Nodalis.Core.Abstractions;

public interface ITemplateStore
{
    /// <summary>
    /// Performs the <c>InitializeDefaultsAsync</c> operation.
    /// </summary>
    /// <param name="cancellationToken">The <c>cancellationToken</c> value.</param>
    /// <returns>The result of the operation.</returns>
    Task InitializeDefaultsAsync(
            CancellationToken cancellationToken = default);

    /// <summary>
    /// Performs the <c>LoadTemplateCatalogAsync</c> operation.
    /// </summary>
    /// <param name="cancellationToken">The <c>cancellationToken</c> value.</param>
    /// <returns>The result of the operation.</returns>
    Task<TemplateCatalog> LoadTemplateCatalogAsync(
            CancellationToken cancellationToken = default);

    /// <summary>
    /// Performs the <c>LoadProjectProfilesAsync</c> operation.
    /// </summary>
    /// <param name="cancellationToken">The <c>cancellationToken</c> value.</param>
    /// <returns>The result of the operation.</returns>
    Task<ProjectProfileCatalog> LoadProjectProfilesAsync(
            CancellationToken cancellationToken = default);

    /// <summary>
    /// Performs the <c>RenderAsync</c> operation.
    /// </summary>
    /// <param name="templateKey">The <c>templateKey</c> value.</param>
    /// <param name="variables">The <c>variables</c> value.</param>
    /// <param name="cancellationToken">The <c>cancellationToken</c> value.</param>
    /// <returns>The result of the operation.</returns>
    Task<string> RenderAsync(
            string templateKey,
            IReadOnlyDictionary<string, string> variables,
            CancellationToken cancellationToken = default);
}
