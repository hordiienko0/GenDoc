using GenDoc.Models.TemplateBuilder;
using GenDoc.Services.Templates;
using GenDoc.ViewModels.Templates.Builder;

namespace GenDoc.Tests.Templates;

public class TemplateBuilderCellGridTests
{
    private sealed class StubBuilderService : ITemplateBuilderService
    {
        public IReadOnlyList<BuilderTestPerson> GetTestPeople() => Array.Empty<BuilderTestPerson>();
        public IReadOnlyList<BuilderSignatory> GetSignatories() => Array.Empty<BuilderSignatory>();
        public IReadOnlyDictionary<string, string> ResolveValues(int recipientId, IReadOnlyList<string> tags)
            => new Dictionary<string, string>();
        public byte[] BuildDocx(TemplateBuilderDocument document) => Array.Empty<byte>();
        public XlsxBuildResult BuildXlsx(TemplateBuilderDocument document) => new(Array.Empty<byte>(), 0);
        public BuilderTemplateSource? Load(int templateId, TemplateBuilderMode mode) => null;
        public int Save(int? templateId, string name, TemplateBuilderDocument document) => templateId ?? 1;
    }

    private static TemplateBuilderViewModel NewExcelBuilder()
    {
        var vm = new TemplateBuilderViewModel(new StubBuilderService());
        vm.StartNew(TemplateBuilderMode.Excel);
        vm.Blocks[0].Text = "ВІДОМІСТЬ";
        vm.SetSheetViewport(900, 400);
        return vm;
    }

    private static string CellText(SheetCellViewModel cell) => string.Concat(cell.Runs.Select(r => r.Text));

    private static SheetRowViewModel Row(TemplateBuilderViewModel vm, int number) => vm.SheetRows.Single(r => r.Number == number);

    [Fact]
    public void AnchoredTable_LandsAtItsColumnOffset_WithWriterWidthsOnTheRealColumns()
    {
        var vm = NewExcelBuilder();
        var table = vm.Blocks[1];

        table.AnchorRow = 4;
        table.AnchorColumn = 3;

        var header = Row(vm, 4);
        Assert.True(header.IsTableHeader);
        Assert.Equal(string.Empty, CellText(header.Cells[0]));
        Assert.Equal(string.Empty, CellText(header.Cells[1]));
        Assert.Equal("№ з/п", CellText(header.Cells[2]));
        Assert.Equal("ПІБ", CellText(header.Cells[4]));
        Assert.Equal(TemplateBlockPreview.EmptyPlaceholder, CellText(Row(vm, 5).Cells[4]));

        Assert.Equal(TemplateBuilderViewModel.SheetCellWidth, vm.SheetColumns[0].Width);
        Assert.Equal(TemplateBuilderViewModel.ExcelColumnWidthPx(TemplateBlockXlsxWriter.ColumnWidthChars("№ з/п")), vm.SheetColumns[2].Width);
        Assert.Equal(vm.SheetColumns[2].Width, header.Cells[2].Width);

        Assert.Equal(Enumerable.Range(1, vm.SheetRows.Count), vm.SheetRows.Select(r => r.Number));
        Assert.All(Row(vm, 2).Cells, c => Assert.Equal(string.Empty, CellText(c)));
        Assert.Equal(new[] { "C", "D", "E" }, table.Columns.Select(c => c.Letter));
        Assert.Equal("шапка 4 · шаблон 5 · C–E", table.LayoutCaption);
    }

    [Fact]
    public void AnchoredBanner_MergesOnlyItsOwnSpan()
    {
        var vm = NewExcelBuilder();
        var title = vm.Blocks[0];

        title.AnchorRow = 1;
        title.AnchorColumn = 2;
        title.SpanColumns = 2;

        var row = Row(vm, 1);
        Assert.True(row.IsMerged);
        Assert.Equal(vm.SheetColumns[0].Width, row.Cells[0].Width);
        Assert.Equal("ВІДОМІСТЬ", CellText(row.Cells[1]));
        Assert.Equal(vm.SheetColumns[1].Width + vm.SheetColumns[2].Width, row.Cells[1].Width);
        Assert.Equal(vm.SheetColumns.Sum(c => c.Width), row.Cells.Sum(c => c.Width));
        Assert.Equal("рядок 1 · B–C", title.LayoutCaption);
    }

    [Fact]
    public void TwoBlocksOnOneRow_ShareTheGridRow_EachWithItsOwnStyle()
    {
        var vm = NewExcelBuilder();
        var note = BuilderBlockViewModel.CreateNew(TemplateBlockKind.Paragraph, vm.Signatories, vm.Mode);
        note.Text = "Примітка";
        note.AnchorRow = 1;
        note.AnchorColumn = 5;
        note.SpanColumns = 1;
        note.IsItalic = true;
        vm.Blocks.Add(note);

        var row = Row(vm, 1);
        Assert.Single(vm.SheetRows, r => r.Number == 1);
        Assert.Equal("ВІДОМІСТЬ", CellText(row.Cells[0]));
        var noteCell = row.Cells.Single(c => CellText(c) == "Примітка");
        Assert.Same(noteCell, row.Cells[2]);
        Assert.Equal(System.Windows.FontStyles.Italic, noteCell.Style.Slant);
        Assert.Equal(System.Windows.FontStyles.Normal, row.Cells[0].Style.Slant);
        Assert.Empty(vm.SheetRows.Where(r => r.IsMerged && r.Number != 1));
        Assert.False(note.IsConflicting);
    }

    [Fact]
    public void OverlappingBlocks_AreFlaggedInTheGridAndInTheCaption()
    {
        var vm = NewExcelBuilder();
        var note = BuilderBlockViewModel.CreateNew(TemplateBlockKind.Paragraph, vm.Signatories, vm.Mode);
        note.Text = "Примітка";
        note.AnchorRow = 3;
        note.AnchorColumn = 2;
        note.SpanColumns = 1;
        vm.Blocks.Add(note);

        var table = vm.Blocks[1];
        Assert.True(note.IsConflicting);
        Assert.True(table.IsConflicting);
        Assert.Equal("Блок «Абзац» перекриває блок «Таблиця» у B3", note.LayoutCaption);
        Assert.Equal("Блок «Абзац» перекриває блок «Таблиця» у B3", table.LayoutCaption);
        Assert.Contains(Row(vm, 3).Cells, c => c.IsConflict);
        Assert.DoesNotContain(Row(vm, 2).Cells, c => c.IsConflict);
        Assert.False(vm.Blocks[0].IsConflicting);

        note.AnchorRow = 4;

        Assert.False(note.IsConflicting);
        Assert.False(table.IsConflicting);
        Assert.Equal("рядок 4 · B", note.LayoutCaption);
    }

    [Fact]
    public void Anchors_RoundTripThroughTheBlockViewModel()
    {
        var source = new TemplateBlock(TemplateBlockKind.Title, "ВІДОМІСТЬ", AnchorRow: 2, AnchorColumn: 3, SpanColumns: 4);

        var block = BuilderBlockViewModel.FromBlock(source, Array.Empty<BuilderSignatory>(), TemplateBuilderMode.Excel);

        Assert.Equal((2, 3, 4), (block.AnchorRow, block.AnchorColumn, block.SpanColumns));
        Assert.Equal((2, 3, 4), (block.ToBlock().AnchorRow, block.ToBlock().AnchorColumn, block.ToBlock().SpanColumns));

        var plain = BuilderBlockViewModel.CreateNew(TemplateBlockKind.Title, Array.Empty<BuilderSignatory>(), TemplateBuilderMode.Excel);
        Assert.Null(plain.ToBlock().AnchorRow);
        Assert.Null(plain.ToBlock().AnchorColumn);
        Assert.Null(plain.ToBlock().SpanColumns);
    }

    [Fact]
    public void ChangingAnAnchor_MakesTheDocumentDirty()
    {
        var vm = NewExcelBuilder();
        vm.TemplateName = "Відомість";
        vm.SaveCommand.Execute(null);
        Assert.False(vm.IsDirty);

        vm.Blocks[1].AnchorColumn = 2;

        Assert.True(vm.IsDirty);
    }
}
