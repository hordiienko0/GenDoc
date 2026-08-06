using GenDoc.Models;
using GenDoc.Models.Enums;
using GenDoc.Services.Generation;

namespace GenDoc.Tests;

public class GenerationServiceCourseOfficerFieldTests
{
    [Fact]
    public void BuildValues_IsCourseOfficerTrue_RendersTak()
    {
        var recipient = new Recipient { LastName = "Тест", FirstName = "Тест", IsCourseOfficer = true };
        var mappings = new List<TemplateFieldMapping>
        {
            new() { PlaceholderTag = "{{курсовий_офіцер}}", SourceType = MappingSourceType.Recipient, FieldName = "IsCourseOfficer" }
        };

        var values = GenerationService.BuildValues(mappings, recipient, org: null, manualValues: new Dictionary<string, string>());

        Assert.Equal("Так", values["{{курсовий_офіцер}}"]);
    }

    [Fact]
    public void BuildValues_IsCourseOfficerFalse_RendersNi()
    {
        var recipient = new Recipient { LastName = "Тест", FirstName = "Тест", IsCourseOfficer = false };
        var mappings = new List<TemplateFieldMapping>
        {
            new() { PlaceholderTag = "{{курсовий_офіцер}}", SourceType = MappingSourceType.Recipient, FieldName = "IsCourseOfficer" }
        };

        var values = GenerationService.BuildValues(mappings, recipient, org: null, manualValues: new Dictionary<string, string>());

        Assert.Equal("Ні", values["{{курсовий_офіцер}}"]);
    }
}
