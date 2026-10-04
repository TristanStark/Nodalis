using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Nodalis.Core.Markdown;

namespace Nodalis.App.Markdown;

public static class MarkdownFlowDocumentRenderer
{
    /// <summary>
    /// Performs the <c>Render</c> operation.
    /// </summary>
    /// <param name="markdown">The <c>markdown</c> value.</param>
    /// <param name="baseDirectory">The <c>baseDirectory</c> value.</param>
    /// <param name="internalLinkClicked">The <c>internalLinkClicked</c> value.</param>
    /// <param name="linkClicked">The <c>linkClicked</c> value.</param>
    /// <returns>The result of the operation.</returns>
public static FlowDocument Render(
        string markdown,
        string? baseDirectory = null,
        Action<string>? internalLinkClicked = null,
        Action<string>? linkClicked = null)
    {
        global::System.Windows.Documents.FlowDocument document = new FlowDocument
        {
            PagePadding = new Thickness(22),
            FontFamily = new FontFamily("Segoe UI"),
            FontSize = 14,
            Foreground = GetBrush("PrimaryTextBrush", Brushes.White),
            Background = GetBrush("WindowBackgroundBrush", Brushes.Transparent)
        };

        foreach (global::Nodalis.Core.Markdown.MarkdownBlock block in MarkdownDocumentParser.Parse(markdown))
        {
            document.Blocks.Add(
                CreateBlock(
                    block,
                    baseDirectory,
                    internalLinkClicked,
                    linkClicked));
        }

        return document;
    }

    /// <summary>
    /// Performs the <c>CreateBlock</c> operation.
    /// </summary>
    /// <param name="block">The <c>block</c> value.</param>
    /// <param name="baseDirectory">The <c>baseDirectory</c> value.</param>
    /// <param name="internalLinkClicked">The <c>internalLinkClicked</c> value.</param>
    /// <param name="linkClicked">The <c>linkClicked</c> value.</param>
    /// <returns>The result of the operation.</returns>
private static Block CreateBlock(
        MarkdownBlock block,
        string? baseDirectory,
        Action<string>? internalLinkClicked,
        Action<string>? linkClicked)
    {
        return block.Kind switch
        {
            MarkdownBlockKind.Heading =>
                CreateHeading(
                    block,
                    baseDirectory,
                    internalLinkClicked,
                    linkClicked),

            MarkdownBlockKind.UnorderedListItem =>
                CreatePrefixedParagraph(
                    "• ",
                    block.Text,
                    baseDirectory,
                    internalLinkClicked,
                    linkClicked),

            MarkdownBlockKind.OrderedListItem =>
                CreatePrefixedParagraph(
                    "1. ",
                    block.Text,
                    baseDirectory,
                    internalLinkClicked,
                    linkClicked),

            MarkdownBlockKind.ChecklistItem =>
                CreatePrefixedParagraph(
                    block.IsChecked == true ? "☑ " : "☐ ",
                    block.Text,
                    baseDirectory,
                    internalLinkClicked,
                    linkClicked),

            MarkdownBlockKind.Quote =>
                CreateQuote(
                    block.Text,
                    baseDirectory,
                    internalLinkClicked,
                    linkClicked),

            MarkdownBlockKind.CodeBlock =>
                CreateCodeBlock(block),

            MarkdownBlockKind.Table =>
                CreateTable(
                    block,
                    baseDirectory,
                    internalLinkClicked,
                    linkClicked),

            _ =>
                CreateParagraph(
                    block.Text,
                    baseDirectory,
                    internalLinkClicked,
                    linkClicked)
        };
    }

    /// <summary>
    /// Performs the <c>CreateHeading</c> operation.
    /// </summary>
    /// <param name="block">The <c>block</c> value.</param>
    /// <param name="baseDirectory">The <c>baseDirectory</c> value.</param>
    /// <param name="internalLinkClicked">The <c>internalLinkClicked</c> value.</param>
    /// <param name="linkClicked">The <c>linkClicked</c> value.</param>
    /// <returns>The result of the operation.</returns>
private static Paragraph CreateHeading(
        MarkdownBlock block,
        string? baseDirectory,
        Action<string>? internalLinkClicked,
        Action<string>? linkClicked)
    {
        global::System.Windows.Documents.Paragraph paragraph = CreateParagraph(
            block.Text,
            baseDirectory,
            internalLinkClicked,
            linkClicked);

        paragraph.FontWeight = FontWeights.SemiBold;
        paragraph.FontSize = block.Level switch
        {
            1 => 30,
            2 => 24,
            3 => 20,
            4 => 18,
            5 => 16,
            _ => 15
        };

        paragraph.Margin = new Thickness(
            0,
            block.Level <= 2 ? 18 : 12,
            0,
            8);

        return paragraph;
    }

    /// <summary>
    /// Performs the <c>CreateParagraph</c> operation.
    /// </summary>
    /// <param name="text">The <c>text</c> value.</param>
    /// <param name="baseDirectory">The <c>baseDirectory</c> value.</param>
    /// <param name="internalLinkClicked">The <c>internalLinkClicked</c> value.</param>
    /// <param name="linkClicked">The <c>linkClicked</c> value.</param>
    /// <returns>The result of the operation.</returns>
private static Paragraph CreateParagraph(
        string text,
        string? baseDirectory,
        Action<string>? internalLinkClicked,
        Action<string>? linkClicked)
    {
        global::System.Windows.Documents.Paragraph paragraph = new Paragraph
        {
            Margin = new Thickness(0, 4, 0, 7),
            LineHeight = 22
        };

        AddInlines(
            paragraph.Inlines,
            text,
            baseDirectory,
            internalLinkClicked,
            linkClicked);

        return paragraph;
    }

    /// <summary>
    /// Performs the <c>CreatePrefixedParagraph</c> operation.
    /// </summary>
    /// <param name="prefix">The <c>prefix</c> value.</param>
    /// <param name="text">The <c>text</c> value.</param>
    /// <param name="baseDirectory">The <c>baseDirectory</c> value.</param>
    /// <param name="internalLinkClicked">The <c>internalLinkClicked</c> value.</param>
    /// <param name="linkClicked">The <c>linkClicked</c> value.</param>
    /// <returns>The result of the operation.</returns>
private static Paragraph CreatePrefixedParagraph(
        string prefix,
        string text,
        string? baseDirectory,
        Action<string>? internalLinkClicked,
        Action<string>? linkClicked)
    {
        global::System.Windows.Documents.Paragraph paragraph = new Paragraph
        {
            Margin = new Thickness(16, 2, 0, 4),
            LineHeight = 22
        };

        paragraph.Inlines.Add(
            new Run(prefix)
            {
                Foreground = GetBrush("AccentBrush", Brushes.CornflowerBlue)
            });

        AddInlines(
            paragraph.Inlines,
            text,
            baseDirectory,
            internalLinkClicked,
            linkClicked);

        return paragraph;
    }

    /// <summary>
    /// Performs the <c>CreateQuote</c> operation.
    /// </summary>
    /// <param name="text">The <c>text</c> value.</param>
    /// <param name="baseDirectory">The <c>baseDirectory</c> value.</param>
    /// <param name="internalLinkClicked">The <c>internalLinkClicked</c> value.</param>
    /// <param name="linkClicked">The <c>linkClicked</c> value.</param>
    /// <returns>The result of the operation.</returns>
private static Paragraph CreateQuote(
        string text,
        string? baseDirectory,
        Action<string>? internalLinkClicked,
        Action<string>? linkClicked)
    {
        global::System.Windows.Documents.Paragraph paragraph = CreateParagraph(
            text,
            baseDirectory,
            internalLinkClicked,
            linkClicked);

        paragraph.Margin = new Thickness(10, 7, 0, 7);
        paragraph.Padding = new Thickness(12, 6, 8, 6);
        paragraph.BorderBrush = GetBrush(
            "AccentBrush",
            Brushes.CornflowerBlue);
        paragraph.BorderThickness = new Thickness(3, 0, 0, 0);
        paragraph.Foreground = GetBrush(
            "SecondaryTextBrush",
            Brushes.LightGray);

        return paragraph;
    }

    /// <summary>
    /// Performs the <c>CreateCodeBlock</c> operation.
    /// </summary>
    /// <param name="block">The <c>block</c> value.</param>
    /// <returns>The result of the operation.</returns>
private static Paragraph CreateCodeBlock(
        MarkdownBlock block)
    {
        global::System.Windows.Documents.Paragraph paragraph = new Paragraph(
            new Run(block.Text))
        {
            Margin = new Thickness(0, 8, 0, 8),
            Padding = new Thickness(12),
            FontFamily = new FontFamily("Consolas"),
            FontSize = 13,
            Background = GetBrush(
                "PanelElevatedBrush",
                Brushes.DimGray)
        };

        if (!string.IsNullOrWhiteSpace(block.Language))
        {
            paragraph.ToolTip = block.Language;
        }

        return paragraph;
    }

    /// <summary>
    /// Performs the <c>CreateTable</c> operation.
    /// </summary>
    /// <param name="block">The <c>block</c> value.</param>
    /// <param name="baseDirectory">The <c>baseDirectory</c> value.</param>
    /// <param name="internalLinkClicked">The <c>internalLinkClicked</c> value.</param>
    /// <param name="linkClicked">The <c>linkClicked</c> value.</param>
    /// <returns>The result of the operation.</returns>
private static Table CreateTable(
        MarkdownBlock block,
        string? baseDirectory,
        Action<string>? internalLinkClicked,
        Action<string>? linkClicked)
    {
        global::System.Windows.Documents.Table table = new Table
        {
            CellSpacing = 0,
            Margin = new Thickness(0, 8, 0, 10)
        };

        int columnCount = block.TableRows.Count == 0
            ? 0
            : block.TableRows.Max(row => row.Count);

        for (int index = 0; index < columnCount; index++)
        {
            table.Columns.Add(new TableColumn());
        }

        global::System.Windows.Documents.TableRowGroup group = new TableRowGroup();
        table.RowGroups.Add(group);

        for (int rowIndex = 0;
             rowIndex < block.TableRows.Count;
             rowIndex++)
        {
            global::System.Collections.Generic.List<string> sourceRow = block.TableRows[rowIndex];
            global::System.Windows.Documents.TableRow row = new TableRow();

            for (int columnIndex = 0;
                 columnIndex < columnCount;
                 columnIndex++)
            {
                string text = columnIndex < sourceRow.Count
                    ? sourceRow[columnIndex]
                    : string.Empty;

                global::System.Windows.Documents.Paragraph paragraph = CreateParagraph(
                    text,
                    baseDirectory,
                    internalLinkClicked,
                    linkClicked);

                paragraph.Margin = new Thickness(0);

                if (rowIndex == 0)
                {
                    paragraph.FontWeight = FontWeights.SemiBold;
                }

                row.Cells.Add(
                    new TableCell(paragraph)
                    {
                        Padding = new Thickness(8, 5, 8, 5),
                        BorderBrush = GetBrush(
                            "BorderBrush",
                            Brushes.Gray),
                        BorderThickness = new Thickness(0.5)
                    });
            }

            group.Rows.Add(row);
        }

        return table;
    }

    /// <summary>
    /// Performs the <c>AddInlines</c> operation.
    /// </summary>
    /// <param name="target">The <c>target</c> value.</param>
    /// <param name="text">The <c>text</c> value.</param>
    /// <param name="baseDirectory">The <c>baseDirectory</c> value.</param>
    /// <param name="internalLinkClicked">The <c>internalLinkClicked</c> value.</param>
    /// <param name="linkClicked">The <c>linkClicked</c> value.</param>
    /// <returns>The result of the operation.</returns>
private static void AddInlines(
        InlineCollection target,
        string text,
        string? baseDirectory,
        Action<string>? internalLinkClicked,
        Action<string>? linkClicked)
    {
        foreach (global::Nodalis.Core.Markdown.MarkdownInline inline in MarkdownInlineParser.Parse(text))
        {
            target.Add(
                CreateInline(
                    inline,
                    baseDirectory,
                    internalLinkClicked,
                    linkClicked));
        }
    }

    /// <summary>
    /// Performs the <c>CreateInline</c> operation.
    /// </summary>
    /// <param name="inline">The <c>inline</c> value.</param>
    /// <param name="baseDirectory">The <c>baseDirectory</c> value.</param>
    /// <param name="internalLinkClicked">The <c>internalLinkClicked</c> value.</param>
    /// <param name="linkClicked">The <c>linkClicked</c> value.</param>
    /// <returns>The result of the operation.</returns>
private static Inline CreateInline(
        MarkdownInline inline,
        string? baseDirectory,
        Action<string>? internalLinkClicked,
        Action<string>? linkClicked)
    {
        return inline.Kind switch
        {
            MarkdownInlineKind.Bold =>
                new Bold(new Run(inline.Text)),

            MarkdownInlineKind.Italic =>
                new Italic(new Run(inline.Text)),

            MarkdownInlineKind.Code =>
                new Run(inline.Text)
                {
                    FontFamily = new FontFamily("Consolas"),
                    Background = GetBrush(
                        "PanelElevatedBrush",
                        Brushes.DimGray)
                },

            MarkdownInlineKind.InternalLink =>
                CreateLink(
                    inline.Text,
                    inline.Target,
                    internalLinkClicked),

            MarkdownInlineKind.Link =>
                CreateLink(
                    inline.Text,
                    inline.Target,
                    linkClicked),

            MarkdownInlineKind.Image =>
                CreateImageInline(
                    inline,
                    baseDirectory),

            _ => new Run(inline.Text)
        };
    }

    /// <summary>
    /// Performs the <c>CreateLink</c> operation.
    /// </summary>
    /// <param name="text">The <c>text</c> value.</param>
    /// <param name="target">The <c>target</c> value.</param>
    /// <param name="clicked">The <c>clicked</c> value.</param>
    /// <returns>The result of the operation.</returns>
private static Hyperlink CreateLink(
        string text,
        string? target,
        Action<string>? clicked)
    {
        global::System.Windows.Documents.Hyperlink hyperlink = new Hyperlink(
            new Run(text))
        {
            Foreground = GetBrush(
                "AccentBrush",
                Brushes.CornflowerBlue),
            ToolTip = target
        };

        if (!string.IsNullOrWhiteSpace(target) &&
            clicked is not null)
        {
            hyperlink.Click += (_, _) => clicked(target);
        }

        return hyperlink;
    }

    /// <summary>
    /// Performs the <c>CreateImageInline</c> operation.
    /// </summary>
    /// <param name="inline">The <c>inline</c> value.</param>
    /// <param name="baseDirectory">The <c>baseDirectory</c> value.</param>
    /// <returns>The result of the operation.</returns>
private static Inline CreateImageInline(
        MarkdownInline inline,
        string? baseDirectory)
    {
        if (string.IsNullOrWhiteSpace(inline.Target) ||
            string.IsNullOrWhiteSpace(baseDirectory) ||
            Uri.TryCreate(
                inline.Target,
                UriKind.Absolute,
                out global::System.Uri? absoluteUri) &&
            !absoluteUri.IsFile)
        {
            return new Run(
                $"[image: {inline.Text}]")
            {
                Foreground = GetBrush(
                    "SecondaryTextBrush",
                    Brushes.LightGray),
                ToolTip = inline.Target
            };
        }

        string path = Path.IsPathRooted(inline.Target)
            ? inline.Target
            : Path.Combine(baseDirectory, inline.Target);

        path = Path.GetFullPath(path);

        if (!File.Exists(path))
        {
            return new Run(
                $"[image introuvable: {inline.Text}]")
            {
                Foreground = GetBrush(
                    "SecondaryTextBrush",
                    Brushes.LightGray),
                ToolTip = path
            };
        }

        try
        {
            global::System.Windows.Media.Imaging.BitmapImage bitmap = new BitmapImage();

            using (global::System.IO.FileStream stream = new FileStream(
                       path,
                       FileMode.Open,
                       FileAccess.Read,
                       FileShare.ReadWrite))
            {
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.StreamSource = stream;
                bitmap.EndInit();
                bitmap.Freeze();
            }

            return new InlineUIContainer(
                new Image
                {
                    Source = bitmap,
                    MaxWidth = 640,
                    MaxHeight = 480,
                    Stretch = Stretch.Uniform,
                    ToolTip = inline.Text
                })
            {
                BaselineAlignment = BaselineAlignment.Center
            };
        }
        catch (Exception exception) when (
            exception is IOException or
            UnauthorizedAccessException or
            NotSupportedException)
        {
            return new Run(
                $"[image illisible: {inline.Text}]")
            {
                Foreground = GetBrush(
                    "SecondaryTextBrush",
                    Brushes.LightGray),
                ToolTip = path
            };
        }
    }

    /// <summary>
    /// Performs the <c>GetBrush</c> operation.
    /// </summary>
    /// <param name="resourceKey">The <c>resourceKey</c> value.</param>
    /// <param name="fallback">The <c>fallback</c> value.</param>
    /// <returns>The result of the operation.</returns>
private static Brush GetBrush(
        string resourceKey,
        Brush fallback) =>
        Application.Current.TryFindResource(resourceKey) as Brush
        ?? fallback;
}
