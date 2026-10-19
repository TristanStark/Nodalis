namespace Nodalis.Core.Importing;

/// <summary>Word table representation with explicit layout-loss diagnostics.</summary>
public sealed record DocxTable
{
    public List<DocxTableRow> Rows { get; init; } = [];

    public List<string> Warnings { get; init; } = [];
}

/// <summary>A Word table row, including its optional repeated-header marker.</summary>
public sealed record DocxTableRow
{
    public List<DocxTableCell> Cells { get; init; } = [];

    public bool IsHeader { get; init; }
}

/// <summary>How a Word cell participates in a vertically merged range.</summary>
public enum DocxVerticalMerge
{
    None,
    Start,
    Continue
}

/// <summary>Text, column span and optional nested tables in a Word table cell.</summary>
public sealed record DocxTableCell
{
    public string Text { get; init; } = string.Empty;

    public int ColumnSpan { get; init; } = 1;

    public DocxVerticalMerge VerticalMerge { get; init; }

    public List<DocxTable> NestedTables { get; init; } = [];
}
