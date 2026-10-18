using System.IO;
using System.Windows;
using Nodalis.App.Commands;
using Nodalis.App.Dialogs;
using Nodalis.App.Markdown;
using Nodalis.Core.Composites;
using Nodalis.Core.Links;
using Nodalis.Infrastructure.Composites;
using Nodalis.Infrastructure.Links;

namespace Nodalis.App;

/// <summary>Offers composite Markdown previews and keyboard-accessible source insertion.</summary>
public partial class MainWindow
{
    private CancellationTokenSource? _compositePreviewRequest;
    private int _compositePreviewGeneration;

    /// <summary>Starts a cancellable render only when the current Markdown includes directives.</summary>
    /// <param name="path">Current active tab path.</param>
    /// <param name="markdown">Current editor text, including unsaved changes.</param>
    private void ScheduleCompositePreview(string path, string markdown)
    {
        _compositePreviewRequest?.Cancel();
        int generation = ++_compositePreviewGeneration;
        if (!markdown.Contains("![[", StringComparison.Ordinal))
        {
            return;
        }

        CancellationTokenSource request = new();
        _compositePreviewRequest = request;
        _ = RenderCompositePreviewAsync(path, markdown, request, generation);
    }

    /// <summary>Uses canonical sources and discards stale or cancelled preview generations.</summary>
    /// <param name="path">Tab being rendered.</param>
    /// <param name="markdown">Editor snapshot.</param>
    /// <param name="request">Cancellation owner.</param>
    /// <param name="generation">Current preview serial.</param>
    /// <returns>Latest UI refresh, if the tab still matches.</returns>
    private async Task RenderCompositePreviewAsync(string path, string markdown,
        CancellationTokenSource request, int generation)
    {
        try
        {
            LinkIndexCatalog index = await _linkIndexService.LoadAsync(request.Token);
            CompositeWorkspaceRenderer renderer = new(_root.FullPath, index);
            CompositeRenderResult result = await renderer.RenderAsync(
                path, markdown, request.Token);
            if (request.IsCancellationRequested || generation != _compositePreviewGeneration ||
                _activeDocumentTab is null ||
                !string.Equals(_activeDocumentTab.FullPath, path,
                    StringComparison.OrdinalIgnoreCase) || !_previewVisible)
            {
                return;
            }

            MarkdownPreview.Document = MarkdownFlowDocumentRenderer.Render(
                result.Markdown, Path.GetDirectoryName(path),
                OnCompositeSourceClicked, OnMarkdownLinkClicked);
            if (result.Diagnostics.Count > 0)
            {
                StatusText.Text = "Documents composites : " +
                    result.Diagnostics.Count + " inclusion(s) indisponible(s).";
            }
        }
        catch (OperationCanceledException)
        {
            // A new edit or tab superseded this request.
        }
        catch (Exception error) when (error is IOException or
            UnauthorizedAccessException or InvalidDataException or ArgumentException)
        {
            if (generation == _compositePreviewGeneration &&
                !request.IsCancellationRequested)
            {
                StatusText.Text = "Aperçu composite : " + error.Message;
            }
        }
        finally
        {
            if (ReferenceEquals(_compositePreviewRequest, request))
            {
                _compositePreviewRequest = null;
            }

            request.Dispose();
        }
    }

    /// <summary>Opens the originating document and, if provided, its included section line.</summary>
    /// <param name="reference">Existing internal link target with optional #section.</param>
    private async void OnCompositeSourceClicked(string reference)
    {
        await RunUiActionAsync("Inclusion · ouvrir la source", async () =>
        {
            string[] parts = reference.Split('#', 2);
            LinkResolution link = WorkspaceLinkIndexService.Resolve(
                await _linkIndexService.LoadAsync(), parts[0]);
            if (link.Status != LinkResolutionStatus.Resolved ||
                link.Target is not LinkTargetEntry target)
            {
                StatusText.Text = "Source composite introuvable ou ambiguë : " + reference;
                return;
            }

            int? line = null;
            if (parts.Length == 2)
            {
                string full = Path.GetFullPath(Path.Combine(_root.FullPath,
                    target.RelativePath.Replace('/', Path.DirectorySeparatorChar)));
                string root = Path.TrimEndingDirectorySeparator(
                    Path.GetFullPath(_root.FullPath)) + Path.DirectorySeparatorChar;
                if (full.StartsWith(root, StringComparison.OrdinalIgnoreCase) &&
                    File.Exists(full))
                {
                    CompositeExcerpt section = CompositeMarkdownSections.Extract(
                        await File.ReadAllTextAsync(full), parts[1]);
                    if (section.Diagnostic is null)
                    {
                        line = section.Line;
                    }
                    else
                    {
                        StatusText.Text = section.Diagnostic;
                    }
                }
            }

            await NavigateToLinkTargetAsync(target, line);
        });
    }

    /// <summary>Lets the user select an indexed local document and optionally a unique section.</summary>
    /// <returns>Editor insertion task from Ctrl+P, without modifying included sources.</returns>
    private async Task InsertCompositeReferenceAsync()
    {
        if (_workspaceReadOnly || _activeDocumentTab is null)
        {
            StatusText.Text = "Inclusion indisponible en lecture seule ou sans document ouvert.";
            return;
        }

        LinkIndexCatalog index = await _linkIndexService.LoadAsync();
        LinkTargetEntry[] candidates = index.Targets
            .Where(item => item.Kind == LinkTargetKind.Document)
            .OrderBy(item => item.QualifiedName, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
        if (candidates.Length == 0)
        {
            StatusText.Text = "Aucune source Markdown répertoriée.";
            return;
        }

        PaletteCommand[] choices = candidates.Select(item => new PaletteCommand
        {
            Id = item.Id.ToString("D"),
            Title = item.DisplayName,
            Subtitle = item.QualifiedName,
            Keywords = [item.DisplayName, item.QualifiedName],
            ExecuteAsync = () => Task.CompletedTask
        }).ToArray();
        CommandPaletteDialog pick = new(choices) { Owner = this };
        if (pick.ShowDialog() != true || pick.SelectedCommand is not PaletteCommand selection ||
            !Guid.TryParse(selection.Id, out Guid id))
        {
            return;
        }

        LinkTargetEntry chosen = candidates.Single(item => item.Id == id);
        string full = Path.GetFullPath(Path.Combine(_root.FullPath,
            chosen.RelativePath.Replace('/', Path.DirectorySeparatorChar)));
        string root = Path.TrimEndingDirectorySeparator(
            Path.GetFullPath(_root.FullPath)) + Path.DirectorySeparatorChar;
        if (!full.StartsWith(root, StringComparison.OrdinalIgnoreCase) ||
            !full.EndsWith(".md", StringComparison.OrdinalIgnoreCase) ||
            !File.Exists(full))
        {
            StatusText.Text = "Cible d'inclusion indisponible ou non autorisée.";
            return;
        }

        string source = await File.ReadAllTextAsync(full);
        IReadOnlyList<string> headings = CompositeMarkdownSections.Headings(source);
        PaletteCommand[] sectionOptions =
        [
            new PaletteCommand
            {
                Id = "all", Title = "Inclure le document entier",
                Keywords = ["tout", "document"], ExecuteAsync = () => Task.CompletedTask
            },
            .. headings.Select((heading, index) => new PaletteCommand
            {
                Id = index.ToString(System.Globalization.CultureInfo.InvariantCulture),
                Title = heading, Subtitle = "Section du document",
                Keywords = [heading], ExecuteAsync = () => Task.CompletedTask
            })
        ];
        CommandPaletteDialog chooseSection = new(sectionOptions) { Owner = this };
        if (chooseSection.ShowDialog() != true ||
            chooseSection.SelectedCommand is not PaletteCommand sectionCommand)
        {
            return;
        }

        string? sectionName = sectionCommand.Id == "all" ? null :
            int.TryParse(sectionCommand.Id, out int sectionIndex) &&
            sectionIndex >= 0 && sectionIndex < headings.Count
                ? headings[sectionIndex] : null;
        string marker = CompositeMarkdownSyntax.Format(chosen.Id, sectionName);
        MarkdownEditorTextBox.Focus();
        MarkdownEditorTextBox.SelectedText = Environment.NewLine + marker + Environment.NewLine;
        StatusText.Text = "Inclusion ajoutée : " + chosen.DisplayName +
            (sectionName is null ? "" : " · " + sectionName);
    }
}
