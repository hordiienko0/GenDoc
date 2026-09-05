using ClosedXML.Excel;
using GenDoc.Models.TemplateBuilder;
using GenDoc.Services.Templates;

namespace GenDoc.Tests.Templates;

public class TemplateBlockAnchorTests
{
    private static TableSpec ThreeColumns() => new(new[]
    {
        new TableColumn("№ з/п", "{{номер}}"),
        new TableColumn("Звання", "{{звання}}"),
        new TableColumn("ПІБ", "{{піб}}")
    }, RepeatPerPerson: true);

    private static IXLWorksheet SheetOf(TemplateBuilderDocument document)
    {
        var workbook = new XLWorkbook(new MemoryStream(TemplateBlockXlsxWriter.Write(document).Content));
        return workbook.Worksheets.First();
    }

    [Fact]
    public void Anchored_table_starts_at_its_cell_and_the_next_block_flows_below_it()
    {
        var blocks = new[]
        {
            new TemplateBlock(TemplateBlockKind.Title, "ВІДОМІСТЬ"),
            new TemplateBlock(TemplateBlockKind.Table, Table: ThreeColumns(), AnchorRow: 4, AnchorColumn: 3),
            new TemplateBlock(TemplateBlockKind.Signatures, Signatures: new[] { new SignatureLine("Склав", null) })
        };

        var layout = TemplateSheetLayout.Compute(blocks);

        Assert.Equal((1, 1, 1, 3), Corners(layout.Placements[0]));
        Assert.Equal((4, 5, 3, 5), Corners(layout.Placements[1]));
        Assert.Equal((6, 6, 1, 3), Corners(layout.Placements[2]));
        Assert.Equal(4, layout.HeaderRowIndex);
        Assert.Equal(5, layout.TemplateRowIndex);
        Assert.Equal(6, layout.LastRow);
        Assert.Equal(5, layout.LastColumn);
        Assert.Equal("A1:E6", TemplateSheetLayout.Range(layout));
        Assert.Empty(layout.Conflicts);
    }

    [Fact]
    public void Banner_can_be_moved_to_a_column_and_given_its_own_width()
    {
        var blocks = new[]
        {
            new TemplateBlock(TemplateBlockKind.Title, "ВІДОМІСТЬ", AnchorRow: 2, AnchorColumn: 4, SpanColumns: 2),
            new TemplateBlock(TemplateBlockKind.Table, Table: ThreeColumns())
        };

        var layout = TemplateSheetLayout.Compute(blocks);

        Assert.Equal((2, 2, 4, 5), Corners(layout.Placements[0]));
        Assert.Equal((3, 4, 1, 3), Corners(layout.Placements[1]));
        Assert.Equal("A2:E4", TemplateSheetLayout.Range(layout));
    }

    [Fact]
    public void Column_only_anchor_keeps_the_row_flow()
    {
        var blocks = new[]
        {
            new TemplateBlock(TemplateBlockKind.Title, "ВІДОМІСТЬ"),
            new TemplateBlock(TemplateBlockKind.Paragraph, "м. Київ", AnchorColumn: 5, SpanColumns: 1)
        };

        var layout = TemplateSheetLayout.Compute(blocks);

        Assert.Equal((2, 2, 5, 5), Corners(layout.Placements[1]));
    }

    [Fact]
    public void Overlapping_blocks_are_reported_not_moved()
    {
        var blocks = new[]
        {
            new TemplateBlock(TemplateBlockKind.Title, "ВІДОМІСТЬ"),
            new TemplateBlock(TemplateBlockKind.Table, Table: ThreeColumns(), AnchorRow: 4, AnchorColumn: 3),
            new TemplateBlock(TemplateBlockKind.Paragraph, "Примітка", AnchorRow: 5, AnchorColumn: 4, SpanColumns: 3)
        };

        var layout = TemplateSheetLayout.Compute(blocks);

        var conflict = Assert.Single(layout.Conflicts);
        Assert.Equal(2, conflict.BlockIndex);
        Assert.Equal(1, conflict.OtherBlockIndex);
        Assert.Equal("D5", conflict.Cell);
        Assert.Equal((5, 5, 4, 6), Corners(layout.Placements[2]));
    }

    [Fact]
    public void Blocks_without_anchors_lay_out_exactly_as_before()
    {
        var blocks = new[]
        {
            new TemplateBlock(TemplateBlockKind.Title, "ВІДОМІСТЬ"),
            new TemplateBlock(TemplateBlockKind.Table, Table: ThreeColumns()),
            new TemplateBlock(TemplateBlockKind.Signatures, Signatures: new[] { new SignatureLine("Склав", null) })
        };

        var layout = TemplateSheetLayout.Compute(blocks);

        Assert.All(layout.Placements, p => Assert.Equal((1, 3), (p.FirstColumn, p.LastColumn)));
        Assert.Equal("A1:C4", TemplateSheetLayout.Range(layout));
        Assert.Empty(layout.Conflicts);
    }

    [Fact]
    public void Writer_puts_the_anchored_table_and_banner_where_the_layout_says()
    {
        var document = new TemplateBuilderDocument(new[]
        {
            new TemplateBlock(TemplateBlockKind.Title, "ВІДОМІСТЬ", AnchorRow: 2, AnchorColumn: 3, SpanColumns: 2),
            new TemplateBlock(TemplateBlockKind.Table, Table: ThreeColumns(), AnchorRow: 4, AnchorColumn: 3),
            new TemplateBlock(TemplateBlockKind.Signatures, Signatures: new[] { new SignatureLine("Склав", null) })
        }, Mode: TemplateBuilderMode.Excel);

        var result = TemplateBlockXlsxWriter.Write(document);
        var sheet = SheetOf(document);

        Assert.Equal("ВІДОМІСТЬ", sheet.Cell("C2").GetString());
        Assert.Contains(sheet.MergedRanges, m => m.RangeAddress.ToString() == "C2:D2");
        Assert.Equal("№ з/п", sheet.Cell("C4").GetString());
        Assert.Equal("ПІБ", sheet.Cell("E4").GetString());
        Assert.Equal("{{піб}}", sheet.Cell("E5").GetString());
        Assert.True(sheet.Cell("A4").IsEmpty());
        Assert.Equal("Склав: _______________", sheet.Cell("A6").GetString());
        Assert.Contains(sheet.MergedRanges, m => m.RangeAddress.ToString() == "A6:C6");
        Assert.Equal(TemplateBlockXlsxWriter.ColumnWidthChars("Звання"), sheet.Column(4).Width);
        Assert.Equal(5, result.TemplateRowIndex);
    }

    [Fact]
    public void Column_widths_follow_the_real_columns_of_the_table()
    {
        var blocks = new[]
        {
            new TemplateBlock(TemplateBlockKind.Table, Table: ThreeColumns(), AnchorColumn: 2)
        };

        var widths = TemplateBlockXlsxWriter.ColumnWidths(blocks, TemplateSheetLayout.Compute(blocks));

        Assert.False(widths.ContainsKey(1));
        Assert.Equal(TemplateBlockXlsxWriter.ColumnWidthChars("№ з/п"), widths[2]);
        Assert.Equal(TemplateBlockXlsxWriter.ColumnWidthChars("ПІБ"), widths[4]);
    }

    [Fact]
    public void Anchors_survive_the_json_round_trip_and_stay_absent_when_unset()
    {
        var document = new TemplateBuilderDocument(new[]
        {
            new TemplateBlock(TemplateBlockKind.Title, "ВІДОМІСТЬ", AnchorRow: 2, AnchorColumn: 3, SpanColumns: 4),
            new TemplateBlock(TemplateBlockKind.Paragraph, "текст")
        }, Mode: TemplateBuilderMode.Excel);

        var json = TemplateBuilderJson.Serialize(document);
        var restored = TemplateBuilderJson.Deserialize(json)!;

        Assert.Equal((2, 3, 4), (restored.Blocks[0].AnchorRow, restored.Blocks[0].AnchorColumn, restored.Blocks[0].SpanColumns));
        Assert.Null(restored.Blocks[1].AnchorRow);
        Assert.Null(restored.Blocks[1].AnchorColumn);
        Assert.Null(restored.Blocks[1].SpanColumns);
        Assert.Equal(1, json.Split("AnchorRow").Length - 1);
    }

    [Fact]
    public void Json_saved_before_anchors_existed_loads_as_auto_flow()
    {
        const string legacy = """
            {"Blocks":[{"Kind":"Title","Text":"АКТ","SheetIndex":0},{"Kind":"Table","Table":{"Columns":[{"Title":"ПІБ","Cell":"{{піб}}"}],"RepeatPerPerson":true},"SheetIndex":0}],"Version":1,"Mode":"Excel"}
            """;

        var restored = TemplateBuilderJson.Deserialize(legacy)!;

        Assert.All(restored.Blocks, b => Assert.Null(b.AnchorRow));
        Assert.All(restored.Blocks, b => Assert.Null(b.AnchorColumn));
        Assert.All(restored.Blocks, b => Assert.Null(b.SpanColumns));
        Assert.Equal("A1:A3", TemplateSheetLayout.Range(TemplateSheetLayout.Compute(restored.Blocks)));
    }

    private static (int, int, int, int) Corners(SheetBlockPlacement p)
        => (p.FirstRow, p.LastRow, p.FirstColumn, p.LastColumn);
}
