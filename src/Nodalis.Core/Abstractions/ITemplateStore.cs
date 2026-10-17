using Nodalis.Core.Domain;
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
    /// Loads the Markdown source for one configured template.
    /// </summary>
    /// <param name="templateKey">The stable template key.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The Markdown template source.</returns>
    Task<string> LoadTemplateContentAsync(
            string templateKey,
            CancellationToken cancellationToken = default);

    /// <summary>
    /// Saves editable template metadata and Markdown content.
    /// </summary>
    /// <param name="definition">The updated template definition.</param>
    /// <param name="content">The Markdown template content.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task representing the save.</returns>
    Task SaveTemplateAsync(
            MarkdownTemplateDefinition definition,
            string content,
            CancellationToken cancellationToken = default);

    /// <summary>
    /// Duplicates one configured template under a new display name.
    /// </summary>
    /// <param name="templateKey">The source template key.</param>
    /// <param name="displayName">The display name for the duplicate.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The newly created template definition.</returns>
    Task<MarkdownTemplateDefinition> DuplicateTemplateAsync(
            string templateKey,
            string displayName,
            CancellationToken cancellationToken = default);

    /// <summary>
    /// Restores one built-in template to its shipped metadata and Markdown.
    /// </summary>
    /// <param name="templateKey">The built-in template key.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task representing the restore.</returns>
    Task RestoreTemplateDefaultAsync(
            string templateKey,
            CancellationToken cancellationToken = default);

    /// <summary>
    /// Persists the complete project-profile catalog after validation.
    /// </summary>
    /// <param name="catalog">The project-profile catalog.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task representing the save.</returns>
    Task SaveProjectProfilesAsync(
            ProjectProfileCatalog catalog,
            CancellationToken cancellationToken = default);

    /// <summary>
    /// Restores one built-in project profile.
    /// </summary>
    /// <param name="complexity">The profile complexity to restore.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task representing the restore.</returns>
    Task RestoreProjectProfileDefaultAsync(
            ProjectComplexity complexity,
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
