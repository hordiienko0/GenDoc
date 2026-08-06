using GenDoc.Models;
using GenDoc.Models.Enums;
using GenDoc.Services.Generation;
using GenDoc.ViewModels.Generation;

namespace GenDoc.Tests;

public class GenerationViewModelDocumentDateTests
{
    [Fact]
    public void ApplyDocumentDate_ExplicitDate_SetsReservedDateTagFormatted()
    {
        var manualValues = new Dictionary<string, string>();

        GenerationViewModel.ApplyDocumentDate(manualValues, new DateTime(2026, 8, 6));

        Assert.Equal("06.08.2026", manualValues["{{дата}}"]);
    }

    [Fact]
    public void ApplyDocumentDate_NullDate_FallsBackToToday()
    {
        var manualValues = new Dictionary<string, string>();

        GenerationViewModel.ApplyDocumentDate(manualValues, null);

        Assert.Equal(DateTime.Today.ToString("dd.MM.yyyy"), manualValues["{{дата}}"]);
    }

    [Fact]
    public void ApplyDocumentDate_OverwritesExistingManualDateEntry()
    {
        var manualValues = new Dictionary<string, string> { ["{{дата}}"] = "01.01.2000" };

        GenerationViewModel.ApplyDocumentDate(manualValues, new DateTime(2026, 8, 6));

        Assert.Equal("06.08.2026", manualValues["{{дата}}"]);
    }

    [Fact]
    public void ApplyDocumentDate_ValueReachesBuildValuesForManualDateTag()
    {
        var manualValues = new Dictionary<string, string>();
        GenerationViewModel.ApplyDocumentDate(manualValues, new DateTime(2026, 8, 6));

        var recipient = new Recipient { LastName = "Тест", FirstName = "Тест" };
        var mappings = new List<TemplateFieldMapping>
        {
            new() { PlaceholderTag = "{{дата}}", SourceType = MappingSourceType.Manual }
        };

        var values = GenerationService.BuildValues(mappings, recipient, org: null, manualValues);

        Assert.Equal("06.08.2026", values["{{дата}}"]);
    }
}
