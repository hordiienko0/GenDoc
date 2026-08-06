using GenDoc.Models.Enums;
using GenDoc.ViewModels.Templates;

namespace GenDoc.Tests;

public class DocxMappingRowViewModelTests
{
    [Fact]
    public void IsDateField_KnownDateFieldName_ReturnsTrue()
    {
        var row = new DocxMappingRowViewModel(1, "{{дата_народження}}", MappingSourceType.Recipient, "DateOfBirth", dateFormat: null);

        Assert.True(row.IsDateField);
    }

    [Fact]
    public void IsDateField_NonDateFieldName_ReturnsFalse()
    {
        var row = new DocxMappingRowViewModel(1, "{{прізвище}}", MappingSourceType.Recipient, "LastName", dateFormat: null);

        Assert.False(row.IsDateField);
    }

    [Fact]
    public void IsDateField_UpdatesWhenSelectedFieldNameChangesToADateField()
    {
        var row = new DocxMappingRowViewModel(1, "{{тег}}", MappingSourceType.Recipient, "LastName", dateFormat: null);
        Assert.False(row.IsDateField);

        row.SelectedFieldName = "TravelCertificateDate";

        Assert.True(row.IsDateField);
    }

    [Fact]
    public void Constructor_PreservesSuppliedDateFormat()
    {
        var row = new DocxMappingRowViewModel(1, "{{дата_народження}}", MappingSourceType.Recipient, "DateOfBirth", dateFormat: "yyyy-MM-dd");

        Assert.Equal("yyyy-MM-dd", row.SelectedDateFormat);
    }
}
