using GenDoc.Models.TemplateBuilder;
using GenDoc.Services.Templates;
using GenDoc.Tests.Infrastructure;
using GenDoc.ViewModels.Templates.Builder;

namespace GenDoc.Tests.Templates;

public class TemplateBuilderPolishTests
{
    private sealed class StubBuilderService : ITemplateBuilderService
    {
        public IReadOnlyList<BuilderTestPerson> GetTestPeople() => Array.Empty<BuilderTestPerson>();

        public IReadOnlyList<BuilderSignatory> GetSignatories() => new[]
        {
            new BuilderSignatory(7, "капітан Ковальчук В. Б.", "капітан", "Ковальчук В. Б.")
        };

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
        return vm;
    }

    private static BuilderBlockViewModel Paragraph(TemplateBuilderViewModel vm, string text, int sheet)
    {
        var block = BuilderBlockViewModel.CreateNew(TemplateBlockKind.Paragraph, vm.Signatories, vm.Mode);
        block.Text = text;
        block.SheetIndex = sheet;
        return block;
    }

    [Fact]
    public void MoveBlockUp_SkipsBlocksOfOtherSheets()
    {
        var vm = NewExcelBuilder();
        vm.AddSheetCommand.Execute(null);
        vm.Blocks.Clear();
        var a = Paragraph(vm, "A", 0);
        var other = Paragraph(vm, "інший аркуш", 1);
        var b = Paragraph(vm, "B", 0);
        vm.Blocks.Add(a);
        vm.Blocks.Add(other);
        vm.Blocks.Add(b);

        vm.MoveBlockUpCommand.Execute(b);

        Assert.Equal(new[] { "B", "A", "інший аркуш" }, vm.Blocks.Select(x => x.Text).ToArray());

        vm.MoveBlockUpCommand.Execute(other);

        Assert.Equal(new[] { "B", "A", "інший аркуш" }, vm.Blocks.Select(x => x.Text).ToArray());
    }

    [Fact]
    public void MoveBlockDown_StaysWithinTheSheet()
    {
        var vm = NewExcelBuilder();
        vm.AddSheetCommand.Execute(null);
        vm.Blocks.Clear();
        var a = Paragraph(vm, "A", 0);
        var other = Paragraph(vm, "інший аркуш", 1);
        var b = Paragraph(vm, "B", 0);
        vm.Blocks.Add(a);
        vm.Blocks.Add(other);
        vm.Blocks.Add(b);

        vm.MoveBlockDownCommand.Execute(a);

        Assert.Equal(new[] { "інший аркуш", "B", "A" }, vm.Blocks.Select(x => x.Text).ToArray());
        vm.CurrentSheetIndex = 0;
        Assert.Equal(new[] { "B", "A" }, vm.VisibleBlocks.Select(x => x.Text).ToArray());
    }

    [Fact]
    public void WordTable_DoesNotRepeatPerPersonByDefault_AndExplainsWhatTheToggleDoes()
    {
        var vm = new TemplateBuilderViewModel(new StubBuilderService());
        vm.StartNew(TemplateBuilderMode.Word);

        var table = BuilderBlockViewModel.CreateNew(TemplateBlockKind.Table, vm.Signatories, TemplateBuilderMode.Word);

        Assert.False(table.RepeatPerPerson);
        Assert.False(table.ToBlock().Table!.RepeatPerPerson);
        Assert.Contains("окремий документ на кожну особу", table.RepeatHintText);

        table.RepeatPerPerson = true;

        Assert.Contains("груповим", table.RepeatHintText);
        Assert.True(table.ToBlock().Table!.RepeatPerPerson);
    }

    [Fact]
    public void ExcelTable_AlwaysRepeatsPerPerson()
    {
        var vm = NewExcelBuilder();
        var table = BuilderBlockViewModel.CreateNew(TemplateBlockKind.Table, vm.Signatories, TemplateBuilderMode.Excel);

        Assert.True(table.ToBlock().Table!.RepeatPerPerson);
    }

    [Fact]
    public void BlockHasContent_TellsEmptyBlocksFromFilledOnes()
    {
        var vm = new TemplateBuilderViewModel(new StubBuilderService());
        vm.StartNew(TemplateBuilderMode.Word);
        var empty = Paragraph(vm, "   ", 0);
        var filled = Paragraph(vm, "Текст", 0);
        var table = BuilderBlockViewModel.CreateNew(TemplateBlockKind.Table, vm.Signatories, TemplateBuilderMode.Word);

        Assert.False(TemplateBuilderViewModel.BlockHasContent(empty));
        Assert.True(TemplateBuilderViewModel.BlockHasContent(filled));
        Assert.True(TemplateBuilderViewModel.BlockHasContent(table));
    }

    [Fact]
    public void RemoveBlockCore_RemovesTheBlock()
    {
        var vm = new TemplateBuilderViewModel(new StubBuilderService());
        vm.StartNew(TemplateBuilderMode.Word);
        var block = vm.Blocks[1];

        vm.RemoveBlockCore(block);

        Assert.DoesNotContain(block, vm.Blocks);
    }

    [Fact]
    public void RemoveCurrentSheetCore_RemovesTheMiddleSheetAndShiftsLaterBlocks()
    {
        var vm = NewExcelBuilder();
        vm.AddSheetCommand.Execute(null);
        vm.AddSheetCommand.Execute(null);
        var onLast = Paragraph(vm, "останній", 2);
        vm.Blocks.Add(Paragraph(vm, "середній", 1));
        vm.Blocks.Add(onLast);
        vm.CurrentSheetIndex = 1;

        vm.RemoveCurrentSheetCore();

        Assert.Equal(2, vm.Sheets.Count);
        Assert.Equal(new[] { 0, 1 }, vm.Sheets.Select(s => s.Index).ToArray());
        Assert.DoesNotContain(vm.Blocks, b => b.Text == "середній");
        Assert.Equal(1, onLast.SheetIndex);
        Assert.Equal(1, vm.CurrentSheetIndex);
        Assert.Contains(onLast, vm.VisibleBlocks);
    }

    [Fact]
    public void RemoveCurrentSheetCore_KeepsTheOnlySheet()
    {
        var vm = NewExcelBuilder();

        vm.RemoveCurrentSheetCore();

        Assert.Single(vm.Sheets);
    }

    [Fact]
    public void SignatureLine_CanDropTheSignatory()
    {
        var vm = new TemplateBuilderViewModel(new StubBuilderService());
        vm.StartNew(TemplateBuilderMode.Word);
        var block = BuilderBlockViewModel.CreateNew(TemplateBlockKind.Signatures, vm.Signatories, TemplateBuilderMode.Word);
        var line = block.Signatures[0];
        line.SelectedChoice = vm.Signatories[0];
        Assert.Equal(7, block.ToBlock().Signatures![0].RecipientId);

        Assert.Equal("(без підписанта)", SignatureLineViewModel.NoSignatory.Display);
        Assert.Same(SignatureLineViewModel.NoSignatory, line.SignatoryChoices[0]);
        line.SelectedChoice = SignatureLineViewModel.NoSignatory;

        Assert.Null(line.Signatory);
        Assert.Null(block.ToBlock().Signatures![0].RecipientId);
        Assert.Same(SignatureLineViewModel.NoSignatory, line.SelectedChoice);
    }

    [Fact]
    public void Status_IsGreenAfterSaveAndNeutralForHints()
    {
        var vm = new TemplateBuilderViewModel(new StubBuilderService());
        vm.StartNew(TemplateBuilderMode.Word);
        vm.TemplateName = "Довідка";

        vm.SaveCommand.Execute(null);
        Assert.True(vm.IsStatusSuccess);
        Assert.Contains("збережено", vm.StatusMessage);

        vm.ShowHint("Спершу поставте курсор у текст блока");
        Assert.False(vm.IsStatusSuccess);
        Assert.Contains("курсор", vm.StatusMessage);
    }

    [Fact]
    public void SwitchMode_OnAFreshDocument_LeavesItClean()
    {
        var vm = new TemplateBuilderViewModel(new StubBuilderService());
        vm.StartNew(TemplateBuilderMode.Word);
        vm.TemplateName = "Відомість";

        vm.SwitchModeCommand.Execute("Excel");

        Assert.Equal(TemplateBuilderMode.Excel, vm.Mode);
        Assert.Equal("Відомість", vm.TemplateName);
        Assert.False(vm.IsDirty);
    }

    [Fact]
    public void SheetGrid_FitsTheViewportWithoutPermanentScrollbars_AndUsesWriterColumnWidths()
    {
        var vm = NewExcelBuilder();

        vm.SetSheetViewport(500, 300);

        var columns = vm.SheetColumns;
        Assert.True(columns.Count > 3);
        Assert.Equal(TemplateBuilderViewModel.ExcelColumnWidthPx(TemplateBlockXlsxWriter.ColumnWidthChars("№ з/п")), columns[0].Width);
        Assert.Equal(TemplateBuilderViewModel.ExcelColumnWidthPx(TemplateBlockXlsxWriter.ColumnWidthChars("Військове звання")), columns[1].Width);
        Assert.Equal(12, TemplateBlockXlsxWriter.ColumnWidthChars("№ з/п"));
        Assert.Equal(20, TemplateBlockXlsxWriter.ColumnWidthChars("Військове звання"));
        Assert.True(TemplateBuilderViewModel.SheetRowHeaderWidth + columns.Sum(c => c.Width) <= 500 - TemplateBuilderViewModel.ScrollBarSize);
        Assert.True(vm.SheetRows.Count >= 4);
        Assert.True((vm.SheetRows.Count + 1) * TemplateBuilderViewModel.SheetRowHeight <= 300 - TemplateBuilderViewModel.ScrollBarSize);
        Assert.All(vm.SheetRows, row => Assert.Equal(columns.Sum(c => c.Width), row.Cells.Sum(c => c.Width)));
        Assert.All(vm.SheetRows.Where(r => !r.IsMerged), row => Assert.Equal(columns.Count, row.Cells.Count));
        Assert.Equal(columns[1].Width, vm.SheetRows.First(r => r.IsTableHeader).Cells[1].Width);
    }

    [Fact]
    public void SheetGrid_MergedBannerSpansTheDataColumns()
    {
        var vm = NewExcelBuilder();
        vm.Blocks[0].Text = "ВІДОМІСТЬ";

        vm.SetSheetViewport(500, 300);

        var banner = vm.SheetRows.First(r => r.IsMerged);
        Assert.Equal(vm.SheetColumns.Take(3).Sum(c => c.Width), banner.Cells[0].Width);
    }

    [Fact]
    public void Save_KeepsTheEnteredNameAndCleansOnlyTheFileName()
    {
        using var db = new TestDb();
        var service = new TemplateBuilderService(db.Factory, new FakeAuditLog());
        var document = new TemplateBuilderDocument(new[]
        {
            new TemplateBlock(TemplateBlockKind.Paragraph, "Рапорт {{піб}}")
        });

        var id = service.Save(null, "Шаблон_рапорту (2)", document);

        using var ctx = db.Factory.CreateDbContext();
        var stored = ctx.Templates.Single(t => t.Id == id);
        Assert.Equal("Шаблон_рапорту (2)", stored.Name);
        Assert.Equal("рапорту.docx", stored.OriginalFileName);
    }

    [Fact]
    public void Save_ShowsTheStoredNameInTheBuilder()
    {
        using var db = new TestDb();
        var vm = new TemplateBuilderViewModel(new TemplateBuilderService(db.Factory, new FakeAuditLog()));
        vm.StartNew(TemplateBuilderMode.Word);
        vm.Blocks[0].Text = "Рапорт";
        vm.TemplateName = "  Шаблон_рапорту ";

        vm.SaveCommand.Execute(null);

        Assert.Equal("Шаблон_рапорту", vm.TemplateName);
        Assert.False(vm.IsDirty);
    }
}
