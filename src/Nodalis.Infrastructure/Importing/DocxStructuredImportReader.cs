using System.Security.Cryptography;
using Nodalis.Core.Importing;

namespace Nodalis.Infrastructure.Importing;

/// <summary>
/// Adapts the existing dependency-free DOCX parser to the format-neutral import tree.
/// The legacy import preview and commit paths remain unchanged until the new wizard is ready.
/// </summary>
public sealed class DocxStructuredImportReader : IImportDocumentReader
{
    private readonly DocxParser _parser = new DocxParser();

    /// <inheritdoc />
    public string FormatId => "docx";

    /// <inheritdoc />
    public async Task<ImportDocumentTree> ReadAsync(
        string sourcePath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        string fullPath = Path.GetFullPath(sourcePath);

        using FileStream stream = new FileStream(
            fullPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read);
        byte[] fingerprint = await SHA256.HashDataAsync(stream, cancellationToken);
        ParsedDocxDocument parsed = await _parser.ParseAsync(fullPath, cancellationToken);
        return FromParsed(parsed, Path.GetFileName(fullPath), Convert.ToHexString(fingerprint));
    }

    /// <summary>
    /// Builds the same tree from a previously parsed document, useful for fixtures.
    /// All blocks (including preamble and tables) preserve their source order.
    /// </summary>
    /// <param name="document">Existing parsed DOCX.</param>
    /// <param name="fileName">Source file name, without directories.</param>
    /// <param name="sha256">Original source file's hexadecimal SHA-256.</param>
    /// <returns>Nested source sections with stable IDs within this document snapshot.</returns>
    public static ImportDocumentTree FromParsed(
        ParsedDocxDocument document,
        string fileName,
        string sha256)
    {
        ArgumentNullException.ThrowIfNull(document);

        List<MutableNode> roots = [];
        Stack<MutableNode> headings = new Stack<MutableNode>();

        for (int index = 0; index < document.Blocks.Count; index++)
        {
            DocxBlock block = document.Blocks[index];
            int? level = block.Paragraph?.HeadingLevel;
            bool isHeading =
                block.Kind == DocxBlockKind.Paragraph &&
                level is >= 1 and <= 9 &&
                !string.IsNullOrWhiteSpace(block.Paragraph?.Text);

            ImportDocumentNodeKind kind = block.Kind == DocxBlockKind.Table
                ? ImportDocumentNodeKind.Table
                : isHeading
                    ? ImportDocumentNodeKind.Section
                    : block.Paragraph?.IsListItem == true
                        ? ImportDocumentNodeKind.ListItem
                        : ImportDocumentNodeKind.Paragraph;

            string text = block.Paragraph?.Text ??
                string.Join("\n", block.Table?.Rows.Select(row =>
                    string.Join(" | ", row.Cells.Select(cell => cell.Text))) ??
                    Enumerable.Empty<string>());

            MutableNode node = new MutableNode
            {
                SourceId = "docx-b" + (index + 1).ToString("D8", System.Globalization.CultureInfo.InvariantCulture),
                Kind = kind,
                SourceOrder = index,
                Text = text,
                HeadingLevel = isHeading ? level : null,
                SourceStyle = block.Paragraph?.StyleId
            };

            if (isHeading)
            {
                while (headings.Count > 0 &&
                       headings.Peek().HeadingLevel >= level)
                {
                    headings.Pop();
                }
            }

            if (headings.Count == 0)
            {
                roots.Add(node);
            }
            else
            {
                headings.Peek().Children.Add(node);
            }

            if (isHeading)
            {
                headings.Push(node);
            }
        }

        return new ImportDocumentTree
        {
            FormatId = "docx",
            SourceFileName = fileName,
            SourceSha256 = sha256,
            Roots = roots.Select(ConvertNode).ToList()
        };
    }

    /// <summary>
    /// Freezes a mutable construction node into a public structured import node.
    /// </summary>
    /// <param name="node">Tree construction node.</param>
    /// <returns>The independent source node.</returns>
    private static ImportDocumentNode ConvertNode(MutableNode node)
    {
        return new ImportDocumentNode
        {
            SourceId = node.SourceId,
            Kind = node.Kind,
            SourceOrder = node.SourceOrder,
            Text = node.Text,
            HeadingLevel = node.HeadingLevel,
            SourceStyle = node.SourceStyle,
            Children = node.Children.Select(ConvertNode).ToList()
        };
    }

    private sealed class MutableNode
    {
        public required string SourceId { get; init; }
        public required ImportDocumentNodeKind Kind { get; init; }
        public required int SourceOrder { get; init; }
        public required string Text { get; init; }
        public int? HeadingLevel { get; init; }
        public string? SourceStyle { get; init; }
        public List<MutableNode> Children { get; } = [];
    }
}
