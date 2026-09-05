using GenDoc.Models.TemplateBuilder;
using GenDoc.Services.Templates;
using GenDoc.ViewModels.Templates.Builder;

namespace GenDoc.Tests.Templates;

public class TemplateBuilderCellNudgeTests
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
        return vm;
    }

    [Theory]
    [InlineData("c4", 4, 3)]
    [InlineData("C4", 4, 3)]
    [InlineData(" AB12 ", 12, 28)]
    [InlineData("С4", 4, 3)]
    [InlineData("C", null, 3)]
    [InlineData("4", 4, null)]
    [InlineData("", null, null)]
    [InlineData(null, null, null)]
    public void CellAddress_ParsesLettersAndDigits(string? text, int? row, int? column)
    {
        Assert.True(TemplateSheetLayout.TryParseCellAddress(text, out var parsedRow, out var parsedColumn));
        Assert.Equal((row, column), (parsedRow, parsedColumn));
    }

    [Theory]
    [InlineData("4C")]
    [InlineData("C0")]
    [InlineData("C-1")]
    [InlineData("ABCD1")]
    [InlineData("C4:D5")]
    public void CellAddress_RejectsNonsense(string text)
        => Assert.False(TemplateSheetLayout.TryParseCellAddress(text, out _, out _));

    [Fact]
    public void TypingAnAddress_PinsTheBlock_AndTheGridFollows()
    {
        var vm = NewExcelBuilder();
        var table = vm.Blocks[1];
        Assert.Equal(string.Empty, table.CellAddress);

        table.CellAddress = "c4";

        Assert.Equal((4, 3), (table.AnchorRow, table.AnchorColumn));
        Assert.Equal("C4", table.CellAddress);
        Assert.Equal("шапка 4 · шаблон 5 · C–E", table.LayoutCaption);
        Assert.Null(vm.StatusMessage);
    }

    [Fact]
    public void InvalidAddress_ShowsAHint_AndLeavesTheBlockWhereItWas()
    {
        var vm = NewExcelBuilder();
        var table = vm.Blocks[1];
        table.CellAddress = "C4";

        table.CellAddress = "4C";

        Assert.Equal((4, 3), (table.AnchorRow, table.AnchorColumn));
        Assert.Equal("C4", table.CellAddress);
        Assert.Contains("C4", vm.StatusMessage);
        Assert.False(vm.IsStatusSuccess);
    }

    [Fact]
    public void EmptyAddress_ReturnsTheBlockToTheFlow()
    {
        var vm = NewExcelBuilder();
        var table = vm.Blocks[1];
        table.CellAddress = "C4";

        table.CellAddress = "  ";

        Assert.Null(table.AnchorRow);
        Assert.Null(table.AnchorColumn);
        Assert.Equal("шапка 2 · шаблон 3 · A–C", table.LayoutCaption);
    }

    [Fact]
    public void Nudging_MovesOneCellAtATime_FromTheCurrentPosition()
    {
        var vm = NewExcelBuilder();
        var table = vm.Blocks[1];
        Assert.Equal("шапка 2 · шаблон 3 · A–C", table.LayoutCaption);

        table.MoveBlockCellCommand.Execute("Right");
        Assert.Equal("B2", table.CellAddress);

        table.MoveBlockCellCommand.Execute("Down");
        Assert.Equal("B3", table.CellAddress);
        Assert.Equal("шапка 3 · шаблон 4 · B–D", table.LayoutCaption);

        table.MoveBlockCellCommand.Execute("Left");
        table.MoveBlockCellCommand.Execute("Left");
        table.MoveBlockCellCommand.Execute("Up");
        table.MoveBlockCellCommand.Execute("Up");
        table.MoveBlockCellCommand.Execute("Up");
        Assert.Equal("A1", table.CellAddress);
        Assert.True(table.IsConflicting);
    }

    [Fact]
    public void Nudging_InWordMode_DoesNothing()
    {
        var vm = new TemplateBuilderViewModel(new StubBuilderService());
        vm.StartNew(TemplateBuilderMode.Word);
        var block = vm.Blocks[0];

        block.MoveBlockCellCommand.Execute("Right");

        Assert.Null(block.AnchorColumn);
        Assert.False(block.IsCellControlVisible);
        Assert.False(block.IsSpanControlVisible);
    }

    [Fact]
    public void Span_IsEditableForBannersOnly()
    {
        var vm = NewExcelBuilder();
        var title = vm.Blocks[0];
        var table = vm.Blocks[1];

        Assert.True(title.IsCellControlVisible);
        Assert.True(title.IsSpanControlVisible);
        Assert.True(table.IsCellControlVisible);
        Assert.False(table.IsSpanControlVisible);
        Assert.Equal(string.Empty, title.SpanText);

        title.SpanText = "2";
        Assert.Equal(2, title.SpanColumns);
        Assert.Equal("рядок 1 · A–B", title.LayoutCaption);

        title.ChangeSpanCommand.Execute("1");
        Assert.Equal("3", title.SpanText);

        title.ChangeSpanCommand.Execute("-1");
        title.ChangeSpanCommand.Execute("-1");
        title.ChangeSpanCommand.Execute("-1");
        Assert.Equal(1, title.SpanColumns);

        title.SpanText = "багато";
        Assert.Equal(1, title.SpanColumns);
        Assert.Contains("клітинок", vm.StatusMessage);

        title.SpanText = string.Empty;
        Assert.Null(title.SpanColumns);
        Assert.Equal("рядок 1 · A–C", title.LayoutCaption);
    }

    [Fact]
    public void WideningAnAutoBanner_StartsFromItsCurrentWidth()
    {
        var vm = NewExcelBuilder();
        var title = vm.Blocks[0];

        title.ChangeSpanCommand.Execute("1");

        Assert.Equal(4, title.SpanColumns);
    }
}
