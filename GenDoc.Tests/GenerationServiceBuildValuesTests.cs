using GenDoc.Models;
using GenDoc.Models.Enums;
using GenDoc.Services.Generation;

namespace GenDoc.Tests;

public class GenerationServiceBuildValuesTests
{
    [Fact]
    public void BuildValues_DateOfBirthWithNoDateFormat_UsesDefaultDdMmYyyy()
    {
        var recipient = new Recipient
        {
            LastName = "Тест", FirstName = "Тест",
            DateOfBirth = new DateOnly(1990, 3, 15)
        };
        var mappings = new List<TemplateFieldMapping>
        {
            new() { PlaceholderTag = "{{дата_народження}}", SourceType = MappingSourceType.Recipient, FieldName = "DateOfBirth", DateFormat = null }
        };

        var values = GenerationService.BuildValues(mappings, recipient, org: null, manualValues: new Dictionary<string, string>());

        Assert.Equal("15.03.1990", values["{{дата_народження}}"]);
    }

    [Fact]
    public void BuildValues_DateOfBirthWithExplicitLongFormat_UsesUkrainianLongForm()
    {
        var recipient = new Recipient
        {
            LastName = "Тест", FirstName = "Тест",
            DateOfBirth = new DateOnly(1990, 3, 15)
        };
        var mappings = new List<TemplateFieldMapping>
        {
            new() { PlaceholderTag = "{{дата_народження}}", SourceType = MappingSourceType.Recipient, FieldName = "DateOfBirth", DateFormat = DateFormatCatalog.Long }
        };

        var values = GenerationService.BuildValues(mappings, recipient, org: null, manualValues: new Dictionary<string, string>());

        Assert.Equal("15 березня 1990 року", values["{{дата_народження}}"]);
    }

    [Fact]
    public void BuildValues_TravelCertificateDateWithNoDateFormat_DefaultsToLongForm()
    {
        var recipient = new Recipient
        {
            LastName = "Тест", FirstName = "Тест",
            TravelCertificateDate = new DateOnly(2026, 8, 6)
        };
        var mappings = new List<TemplateFieldMapping>
        {
            new() { PlaceholderTag = "{{дата_посвідчення}}", SourceType = MappingSourceType.Recipient, FieldName = "TravelCertificateDate", DateFormat = null }
        };

        var values = GenerationService.BuildValues(mappings, recipient, org: null, manualValues: new Dictionary<string, string>());

        Assert.Equal("6 серпня 2026 року", values["{{дата_посвідчення}}"]);
    }

    [Fact]
    public void BuildValues_TravelCertificateDateWithExplicitIsoFormat_UsesIsoForm()
    {
        var recipient = new Recipient
        {
            LastName = "Тест", FirstName = "Тест",
            TravelCertificateDate = new DateOnly(2026, 8, 6)
        };
        var mappings = new List<TemplateFieldMapping>
        {
            new() { PlaceholderTag = "{{дата_посвідчення}}", SourceType = MappingSourceType.Recipient, FieldName = "TravelCertificateDate", DateFormat = DateFormatCatalog.YyyyMmDd }
        };

        var values = GenerationService.BuildValues(mappings, recipient, org: null, manualValues: new Dictionary<string, string>());

        Assert.Equal("2026-08-06", values["{{дата_посвідчення}}"]);
    }
}
