using System.Windows;
using GenDoc.Services;
using GenDoc.Services.Templates;
using GenDoc.Tests.Infrastructure;
using GenDoc.ViewModels.Templates;

namespace GenDoc.Tests.Templates;

public class TemplatesListColumnWidthTests
{
    private static TemplatesViewModel ViewModel(TestDb db) => new(
        new ExportTemplateService(db.Factory, new FakeAuditLog(), new FakeCurrentUser()),
        new TemplateService(db.Factory, new FakeAuditLog(), new FakeCurrentUser()), null!, null!);

    [Fact]
    public void NameAndFileAreProportionalColumns_SoTheyNeverPushOtherColumnsOut()
    {
        using var db = new TestDb();
        var vm = ViewModel(db);

        Assert.True(vm.NameColumnLength.IsStar);
        Assert.True(vm.FileColumnLength.IsStar);
    }

    [Fact]
    public void DraggingTheNameBorder_WidensNameAtTheExpenseOfFile()
    {
        using var db = new TestDb();
        var vm = ViewModel(db);
        var total = vm.NameColumnLength.Value + vm.FileColumnLength.Value;
        var name = vm.NameColumnLength.Value;

        vm.ResizeNameColumn(0.5);

        Assert.Equal(name + 0.5, vm.NameColumnLength.Value, 6);
        Assert.Equal(total, vm.NameColumnLength.Value + vm.FileColumnLength.Value, 6);
    }

    [Fact]
    public void NeitherColumnCanBeDraggedAway()
    {
        using var db = new TestDb();
        var vm = ViewModel(db);
        var total = vm.NameColumnLength.Value + vm.FileColumnLength.Value;

        vm.ResizeNameColumn(1000);
        Assert.Equal(TemplatesViewModel.ListColumnMinStar, vm.FileColumnLength.Value, 6);
        Assert.Equal(total, vm.NameColumnLength.Value + vm.FileColumnLength.Value, 6);

        vm.ResizeNameColumn(-1000);
        Assert.Equal(TemplatesViewModel.ListColumnMinStar, vm.NameColumnLength.Value, 6);
        Assert.Equal(total, vm.NameColumnLength.Value + vm.FileColumnLength.Value, 6);
    }

    [Fact]
    public void MappingPanelTakesNoSpaceUntilATemplateIsSelected()
    {
        using var db = new TestDb();
        var vm = ViewModel(db);

        Assert.Equal(new GridLength(0), vm.MappingPanelColumnWidth);

        vm.SelectedTemplate = new object();

        Assert.True(vm.MappingPanelColumnWidth.IsAbsolute);
        Assert.Equal(vm.MappingPanelWidth, vm.MappingPanelColumnWidth.Value);
    }
}
