using GenDoc.Services.Generation;
using GenDoc.ViewModels.Generation;

namespace GenDoc.Tests.Generation;

public class GenerationResultViewModelTests
{
    [Fact]
    public void SummaryText_CountsAllThreePhases()
    {
        var vm = new GenerationResultViewModel(new RunResult(31, 2, 1, 1, 0, 0, 0, 0, 1, RunId: 7,
            Issues: new[] { new RunIssue(RunIssue.PhaseDocx, "ШЕВЧЕНКО Тарас", "Рапорт", "немає тегу") }), @"D:\out");

        Assert.Equal("Згенеровано 32 · Пропущено 2 · Помилок 2", vm.SummaryText);
        Assert.True(vm.HasIssues);
        Assert.Single(vm.Issues);
        Assert.Equal(7, vm.RunId);
    }

    [Fact]
    public void SummaryText_WithoutIssues()
    {
        var vm = new GenerationResultViewModel(new RunResult(3, 0, 0, 0, 0, 0, 0, 0, 0), @"D:\out");
        Assert.Equal("Згенеровано 3 · Пропущено 0 · Помилок 0", vm.SummaryText);
        Assert.False(vm.HasIssues);
    }
}
