using GenDoc.ViewModels.Personnel;

namespace GenDoc.Tests.Personnel;

public class GenerateDocumentsDialogViewModelTests
{
    [Fact]
    public void OrderTemplates_DefaultPackageFirst_ThenOthers_NoGroup()
    {
        var all = new List<(int Id, string Name)> { (1, "Довідка"), (2, "Рапорт"), (3, "Допуск") };
        var packageIds = new List<int> { 2 };

        var ordered = GenerateDocumentsDialogViewModel.OrderTemplates(all, packageIds);

        Assert.Equal(new[] { 2, 1, 3 }, ordered.Select(t => t.Id));
        Assert.Equal("Типовий пакет", ordered[0].Group);
        Assert.Equal("Інші шаблони", ordered[1].Group);
    }

    [Fact]
    public void RecipientsSummary_OneOrMany()
    {
        Assert.Equal("ШЕВЧЕНКО Т.Г.", GenerateDocumentsDialogViewModel.BuildRecipientsSummary(new[] { "ШЕВЧЕНКО Т.Г." }));
        Assert.Equal("ШЕВЧЕНКО Т.Г. та ще 2", GenerateDocumentsDialogViewModel.BuildRecipientsSummary(new[] { "ШЕВЧЕНКО Т.Г.", "А", "Б" }));
    }
}
