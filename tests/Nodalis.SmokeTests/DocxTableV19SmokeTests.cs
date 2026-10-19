using System.IO.Compression;
using System.Text;
using Nodalis.Core.Importing;
using Nodalis.Infrastructure.Importing;

internal static class DocxTableV19SmokeTests
{
    /// <summary>Exercises real Word XML gridSpan, vMerge, headers and nested cells.</summary>
    public static async Task RunAsync(string root)
    {
        string path = Path.Combine(root, "docx-19-tables.docx");
        const string body = """
            <?xml version="1.0" encoding="utf-8"?>
            <w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
              <w:body>
                <w:tbl>
                  <w:tr>
                    <w:trPr><w:tblHeader/></w:trPr>
                    <w:tc>
                      <w:tcPr><w:gridSpan w:val="2"/><w:vMerge w:val="restart"/></w:tcPr>
                      <w:p><w:r><w:t>Critère &amp; risque</w:t></w:r></w:p>
                    </w:tc>
                    <w:tc><w:p><w:r><w:t>État</w:t></w:r></w:p></w:tc>
                  </w:tr>
                  <w:tr>
                    <w:tc>
                      <w:tcPr><w:vMerge/></w:tcPr>
                      <w:p><w:r><w:t></w:t></w:r></w:p>
                    </w:tc>
                    <w:tc>
                      <w:p><w:r><w:t>&lt;script&gt;alert(1)&lt;/script&gt;</w:t></w:r></w:p>
                      <w:tbl>
                        <w:tr><w:tc><w:p><w:r><w:t>Nested value</w:t></w:r></w:p></w:tc></w:tr>
                      </w:tbl>
                    </w:tc>
                  </w:tr>
                </w:tbl>
              </w:body>
            </w:document>
            """;

        using (ZipArchive package = ZipFile.Open(path, ZipArchiveMode.Create))
        {
            using StreamWriter writer = new StreamWriter(
                package.CreateEntry("word/document.xml").Open(), new UTF8Encoding(false));
            writer.Write(body);
        }

        ParsedDocxDocument parsed = await new DocxParser().ParseAsync(path);
        DocxTable table = parsed.Blocks.Single().Table ??
            throw new InvalidOperationException("The Word table disappeared.");
        Assert(table.Rows.Count == 2 &&
               table.Rows[0].IsHeader &&
               table.Rows[0].Cells[0].ColumnSpan == 2 &&
               table.Rows[1].Cells[0].VerticalMerge == DocxVerticalMerge.Continue &&
               table.Rows[1].Cells[1].NestedTables.Count == 1,
            "Header, horizontal/vertical merges and nested structure must be captured.");
        Assert(table.Warnings.Count >= 3,
            "Word table limitations must be listed as user-visible warnings.");

        string rendered = DocxMarkdownConverter.ConvertBlocks(parsed.Blocks);
        Assert(rendered.Contains("<table>", StringComparison.Ordinal) &&
               rendered.Contains("colspan=\"2\"", StringComparison.Ordinal) &&
               rendered.Contains("data-word-vmerge=\"continue\"", StringComparison.Ordinal) &&
               rendered.Contains("Nested value", StringComparison.Ordinal),
            "Complex layout must retain nested content and merge metadata in local HTML.");
        Assert(rendered.Contains("&lt;script&gt;", StringComparison.Ordinal) &&
               !rendered.Contains("<script>", StringComparison.Ordinal),
            "Word text must not be executable HTML in fallback markup.");

        DocxTable basic = new DocxTable
        {
            Rows =
            [
                new DocxTableRow
                {
                    Cells = [new DocxTableCell { Text = "ID" }, new DocxTableCell { Text = "Résultat" }]
                },
                new DocxTableRow
                {
                    Cells = [new DocxTableCell { Text = "T1" }, new DocxTableCell { Text = "OK" }]
                }
            ]
        };
        string simple = DocxMarkdownConverter.ConvertBlocks(
            [new DocxBlock { Kind = DocxBlockKind.Table, Table = basic }]);
        Assert(simple.Contains("| --- | --- |", StringComparison.Ordinal) &&
               !simple.Contains("<table>", StringComparison.Ordinal),
            "Regular rectangular tables must still use ordinary Markdown.");
    }

    /// <summary>Throws if a conversion invariant is violated.</summary>
    private static void Assert(bool ok, string message)
    {
        if (!ok)
        {
            throw new InvalidOperationException(message);
        }
    }
}
