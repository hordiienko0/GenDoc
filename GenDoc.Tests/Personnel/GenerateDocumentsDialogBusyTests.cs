using System.Windows;
using GenDoc.Services;
using GenDoc.Services.Generation;
using GenDoc.Tests.Infrastructure;
using GenDoc.ViewModels.Personnel;

namespace GenDoc.Tests.Personnel;

public class GenerateDocumentsDialogBusyTests
{
    private sealed class NoDialogs : IDialogService
    {
        public bool? ShowDialog<TViewModel>(TViewModel viewModel, Window? owner = null) where TViewModel : notnull => null;
    }

    private static GenerateDocumentsDialogViewModel Create(TestDb db) => new(
        TestServices.Generation(db),
        TestServices.Completeness(db, 1),
        TestServices.Archive(db),
        TestServices.ManualTagForm(db, TestServices.Staff(db, 1), new FakeCurrentUser(), new FakeIntakeAccessor(), 1),
        new NoDialogs(),
        new OutputFolderService(db.Factory),
        new[] { (1, "Шевченко Т.") });

    [Fact]
    public void Cancel_IsUnavailable_WhileGenerating()
    {
        using var db = new TestDb();
        var vm = Create(db);

        Assert.True(vm.CancelCommand.CanExecute(null));

        vm.IsBusy = true;
        Assert.False(vm.CancelCommand.CanExecute(null));
        Assert.False(vm.CanClose);

        vm.IsBusy = false;
        Assert.True(vm.CancelCommand.CanExecute(null));
        Assert.True(vm.CanClose);
    }
}
