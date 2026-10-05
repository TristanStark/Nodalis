namespace Nodalis.Core.Importing;

public sealed record DocxTable
{
    public List<DocxTableRow> Rows { get; init; } = [];
}

public sealed record DocxTableRow
{
    public List<DocxTableCell> Cells { get; init; } = [];
}

public sealed record DocxTableCell
{
    public string Text { get; init; } = string.Empty;
}
