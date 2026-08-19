using GenDoc.Models.Enums;
using GenDoc.Services.Templates;

namespace GenDoc.Tests;

// Теги, що реально стоять у відомостях вогневої підготовки з теки «шаблони».
// Кожен, що не потрапив у словник, класифікується як Manual - тобто колонка
// лишається порожньою (саме так «мовчав» {{курсовий_офіцер}}).
public class PlaceholderTagMapsTests
{
    [Theory]
    [InlineData("{{зброя}}", "WeaponFull")]
    [InlineData("{{зброя_назва}}", "WeaponName")]
    [InlineData("{{зброя_номер}}", "WeaponSerialNumber")]
    [InlineData("{{курсовий_офіцер}}", "CourseOfficerSignature")]
    [InlineData("{{піб_ініціали}}", "ShortName")]
    [InlineData("{{№}}", "RowNumber")]
    [InlineData("{{придатність}}", "FitnessCategory")]
    [InlineData("{{оцінка_1}}", "GradeRandom34")]
    [InlineData("{{оцінка_загальна}}", "GradeOverall34")]
    public void Classify_KnownRecipientTags_AreNotManual(string tag, string expectedField)
    {
        var (sourceType, fieldName) = PlaceholderTagMaps.Classify(tag);

        Assert.Equal(MappingSourceType.Recipient, sourceType);
        Assert.Equal(expectedField, fieldName);
    }

    // Кожне зіставлене ім'я поля мусить існувати в ExportFieldKey - інакше
    // XlsxGenerationService мовчки поверне порожньо.
    [Theory]
    [InlineData("{{зброя}}")]
    [InlineData("{{курсовий_офіцер}}")]
    [InlineData("{{оцінка_загальна}}")]
    [InlineData("{{піб_ініціали}}")]
    [InlineData("{{№}}")]
    public void Classify_MappedFieldName_IsARealExportFieldKey(string tag)
    {
        var (_, fieldName) = PlaceholderTagMaps.Classify(tag);

        Assert.True(Enum.TryParse<ExportFieldKey>(fieldName, out _),
            $"'{fieldName}' не є значенням ExportFieldKey");
    }

    // Значення, які вводить людина на кожен запуск, мусять лишитись ручними.
    [Theory]
    [InlineData("{{номер_відомості}}")]
    [InlineData("{{калібр}}")]
    [InlineData("{{кількість_патронів}}")]
    [InlineData("{{номери_вправ}}")]
    [InlineData("{{причина_інструктажу}}")]
    public void Classify_PerRunValues_StayManual(string tag)
    {
        var (sourceType, fieldName) = PlaceholderTagMaps.Classify(tag);

        Assert.Equal(MappingSourceType.Manual, sourceType);
        Assert.Null(fieldName);
    }
}
