using GenDoc.Models;
using GenDoc.Models.Enums;
using GenDoc.Services.Generation;

namespace GenDoc.Tests.Generation;

public class DocxRecipientFieldTests
{
    private static Recipient Person(int id = 7) => new() { Id = id, LastName = "Тест", FirstName = "Тест" };

    private static TemplateFieldMapping Recipient(string tag, string fieldName) => new()
    {
        PlaceholderTag = tag, SourceType = MappingSourceType.Recipient, FieldName = fieldName
    };

    private static readonly Dictionary<string, string> NoManual = new();

    [Fact]
    public void BuildValues_CourseOfficerSignature_UsesTheSignatureOfTheRun()
    {
        var mappings = new List<TemplateFieldMapping> { Recipient("{{курсовий_офіцер}}", "CourseOfficerSignature") };

        var values = GenerationService.BuildValues(mappings, Person(), org: null, manualValues: NoManual,
            courseOfficerSignature: "Курсовий офіцер капітан КОВАЛЬЧУК В.П.");

        Assert.Equal("Курсовий офіцер капітан КОВАЛЬЧУК В.П.", values["{{курсовий_офіцер}}"]);
    }

    [Fact]
    public void BuildValues_CourseOfficerSignature_WithoutSignature_IsEmpty()
    {
        var mappings = new List<TemplateFieldMapping> { Recipient("{{курсовий_офіцер}}", "CourseOfficerSignature") };

        var values = GenerationService.BuildValues(mappings, Person(), org: null, manualValues: NoManual);

        Assert.Equal(string.Empty, values["{{курсовий_офіцер}}"]);
    }

    [Fact]
    public void BuildValues_GradeRandom34_IsStablePerPersonAndTagSlot()
    {
        var mappings = new List<TemplateFieldMapping>
        {
            Recipient("{{оцінка_1}}", "GradeRandom34"),
            Recipient("{{оцінка_2}}", "GradeRandom34"),
            Recipient("{{оцінка}}", "GradeRandom34")
        };

        var first = GenerationService.BuildValues(mappings, Person(37), org: null, manualValues: NoManual);
        var second = GenerationService.BuildValues(mappings, Person(37), org: null, manualValues: NoManual);

        Assert.Equal(first, second);
        Assert.Equal(XlsxGenerationService.ComputeGradeRandom34(37, 1).ToString(), first["{{оцінка_1}}"]);
        Assert.Equal(XlsxGenerationService.ComputeGradeRandom34(37, 2).ToString(), first["{{оцінка_2}}"]);
        Assert.Equal(XlsxGenerationService.ComputeGradeRandom34(37, 0).ToString(), first["{{оцінка}}"]);
        Assert.All(first.Values, v => Assert.Contains(v, new[] { "3", "4" }));
    }

    [Fact]
    public void BuildValues_GradeOverall34_IsRoundedAverageOfTheRandomGrades()
    {
        var mappings = new List<TemplateFieldMapping>
        {
            Recipient("{{оцінка_1}}", "GradeRandom34"),
            Recipient("{{оцінка_2}}", "GradeRandom34"),
            Recipient("{{оцінка_3}}", "GradeRandom34"),
            Recipient("{{оцінка_загальна}}", "GradeOverall34")
        };

        var values = GenerationService.BuildValues(mappings, Person(12), org: null, manualValues: NoManual);

        var expected = Math.Round(
            new[] { 1, 2, 3 }.Average(slot => XlsxGenerationService.ComputeGradeRandom34(12, slot)),
            MidpointRounding.AwayFromZero);
        Assert.Equal(expected.ToString(), values["{{оцінка_загальна}}"]);
    }

    [Fact]
    public void BuildValues_GradeOverall34_WithoutRandomGrades_IsEmpty()
    {
        var mappings = new List<TemplateFieldMapping> { Recipient("{{оцінка_загальна}}", "GradeOverall34") };

        var values = GenerationService.BuildValues(mappings, Person(), org: null, manualValues: NoManual);

        Assert.Equal(string.Empty, values["{{оцінка_загальна}}"]);
    }

    [Fact]
    public void BuildValues_RowNumber_TakesTheManualValueForItsTag()
    {
        var mappings = new List<TemplateFieldMapping> { Recipient("{{номер}}", "RowNumber") };
        var manual = new Dictionary<string, string> { ["{{номер}}"] = "14" };

        var values = GenerationService.BuildValues(mappings, Person(), org: null, manualValues: manual);

        Assert.Equal("14", values["{{номер}}"]);
    }

    [Fact]
    public void BuildValues_RowNumber_WithoutManualValue_IsEmpty()
    {
        var mappings = new List<TemplateFieldMapping> { Recipient("{{номер}}", "RowNumber") };

        var values = GenerationService.BuildValues(mappings, Person(), org: null, manualValues: NoManual);

        Assert.Equal(string.Empty, values["{{номер}}"]);
    }

    [Fact]
    public void ComputeGradeOverall34_SharedHelper_MatchesRoundedAverage()
    {
        var columns = new[] { 3, 5, 8 };

        var overall = XlsxGenerationService.ComputeGradeOverall34(recipientId: 9, columns);

        var expected = (int)Math.Round(columns.Average(c => XlsxGenerationService.ComputeGradeRandom34(9, c)), MidpointRounding.AwayFromZero);
        Assert.Equal(expected, overall);
        Assert.Null(XlsxGenerationService.ComputeGradeOverall34(9, Array.Empty<int>()));
    }
}
