using GenDoc.Models.TemplateBuilder;
using GenDoc.Services.Templates;

namespace GenDoc.Tests.Templates;

public class TemplateBlockAnchorPreviewTests
{
    private static readonly IReadOnlyDictionary<string, string> NoValues = new Dictionary<string, string>();

    private static TableSpec ThreeColumns() => new(new[]
    {
        new TableColumn("№ з/п", "{{номер}}"),
        new TableColumn("Звання", "{{звання}}"),
        new TableColumn("ПІБ", "{{піб}}")
    }, RepeatPerPerson: true);

    [Fact]
    public void Preview_puts_cells_at_the_real_column_offsets()
    {
        var blocks = new[]
        {
            new TemplateBlock(TemplateBlockKind.Title, "ВІДОМІСТЬ", AnchorRow: 1, AnchorColumn: 2, SpanColumns: 3),
            new TemplateBlock(TemplateBlockKind.Table, Table: ThreeColumns(), AnchorRow: 3, AnchorColumn: 3)
        };

        var preview = TemplateBlockPreview.BuildSheet(blocks, NoValues);

        Assert.Equal(new[] { "A", "B", "C", "D", "E" }, preview.ColumnLetters);

        var banner = preview.Rows[0];
        Assert.True(banner.IsMerged);
        Assert.Equal(2, banner.FirstColumn);
        Assert.Equal(3, banner.SpanColumns);
        Assert.Equal("ВІДОМІСТЬ", string.Concat(banner.Cells[0].Select(r => r.Text)));

        var header = preview.Rows[1];
        Assert.Equal(3, header.Number);
        Assert.Equal(3, header.FirstColumn);
        Assert.Equal(3, header.Cells.Count);
        Assert.False(header.IsConflicting);
    }

    [Fact]
    public void Preview_flags_rows_of_overlapping_blocks()
    {
        var blocks = new[]
        {
            new TemplateBlock(TemplateBlockKind.Table, Table: ThreeColumns(), AnchorRow: 2, AnchorColumn: 1),
            new TemplateBlock(TemplateBlockKind.Paragraph, "Примітка", AnchorRow: 3, AnchorColumn: 2, SpanColumns: 1)
        };

        var preview = TemplateBlockPreview.BuildSheet(blocks, NoValues);

        Assert.True(preview.Rows.Single(r => r.IsMerged).IsConflicting);
        Assert.True(preview.Rows.Single(r => r.IsTemplateRow).IsConflicting);
        Assert.False(preview.Rows.Single(r => r.IsTableHeader).IsConflicting);
    }
}
