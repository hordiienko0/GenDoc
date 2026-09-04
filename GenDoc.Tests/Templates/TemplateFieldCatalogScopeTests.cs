using GenDoc.Services.Templates;

namespace GenDoc.Tests.Templates;

public class TemplateFieldCatalogScopeTests
{
    [Fact]
    public void Every_auto_recognised_recipient_field_is_offered_in_the_catalog()
    {
        var offered = TemplateFieldCatalog.RecipientFields.Select(f => f.FieldName).ToHashSet(StringComparer.Ordinal);

        var missing = PlaceholderTagMaps.RecipientTagMap.Values
            .Distinct()
            .Where(field => !offered.Contains(field))
            .OrderBy(field => field, StringComparer.Ordinal)
            .ToList();

        Assert.True(missing.Count == 0,
            $"Поля з RecipientTagMap відсутні в TemplateFieldCatalog.RecipientFields: {string.Join(", ", missing)}");
    }

    [Fact]
    public void Every_auto_recognised_organization_field_is_offered_in_the_catalog()
    {
        var offered = TemplateFieldCatalog.OrganizationFields.Select(f => f.FieldName).ToHashSet(StringComparer.Ordinal);

        var missing = PlaceholderTagMaps.OrganizationTagMap.Values
            .Distinct()
            .Where(field => !offered.Contains(field))
            .ToList();

        Assert.Empty(missing);
    }

    [Fact]
    public void Catalog_field_names_are_unique_and_labelled()
    {
        var all = TemplateFieldCatalog.RecipientFields.Concat(TemplateFieldCatalog.OrganizationFields).ToList();

        Assert.Equal(all.Count, all.Select(f => f.FieldName).Distinct(StringComparer.Ordinal).Count());
        Assert.All(all, f => Assert.False(string.IsNullOrWhiteSpace(f.DisplayName)));
    }

    [Theory]
    [InlineData("RowNumber", "№ з/п (рядок відомості)")]
    [InlineData("CourseOfficerSignature", "Підпис курсового офіцера")]
    public void Catalog_labels_follow_the_agreed_wording(string fieldName, string expectedLabel)
    {
        var option = Assert.Single(TemplateFieldCatalog.RecipientFields, f => f.FieldName == fieldName);

        Assert.Equal(expectedLabel, option.DisplayName);
    }
}
