using GenDoc.Models.Enums;
using GenDoc.Services;
using GenDoc.Services.Import;

namespace GenDoc.Tests.Services;

public class HeaderMappersAgreeTests
{
    private static ImportTargetField Import(string header)
    {
        var noteAssigned = false;
        return ImportService.AutoMapHeader(header, ref noteAssigned);
    }

    private static ExportFieldKey Export(string header)
        => ExportTemplateService.AutoMapHeaderForTests(header);

    public static IEnumerable<object[]> SharedHeaders => new[]
    {
        new object[] { "Командир (ПІБ та телефон)", ImportTargetField.CommanderContact, ExportFieldKey.CommanderContact },
        new object[] { "Командир підрозділу (ПІБ, телефон)", ImportTargetField.CommanderContact, ExportFieldKey.CommanderContact },
        new object[] { "ПІБ командира", ImportTargetField.CommanderContact, ExportFieldKey.CommanderContact },
        new object[] { "ПІБ", ImportTargetField.FullName, ExportFieldKey.FullNameFormatted },
        new object[] { "ПІБ на іноземній мові", ImportTargetField.NameTransliterated, ExportFieldKey.NameTransliterated },
        new object[] { "Прізвище", ImportTargetField.LastName, ExportFieldKey.LastName },
        new object[] { "По батькові", ImportTargetField.MiddleName, ExportFieldKey.MiddleName },
        new object[] { "Звання", ImportTargetField.Rank, ExportFieldKey.Rank },
        new object[] { "Телефон", ImportTargetField.Phone, ExportFieldKey.Phone },
        new object[] { "Особовий номер", ImportTargetField.ServiceNumber, ExportFieldKey.ServiceNumber },
        new object[] { "Дата народження", ImportTargetField.DateOfBirth, ExportFieldKey.DateOfBirth },
        new object[] { "Висновок ВЛК", ImportTargetField.MedicalBoardConclusion, ExportFieldKey.MedicalBoardConclusion },
        new object[] { "Категорія придатності", ImportTargetField.Fitness, ExportFieldKey.FitnessCategory },
        new object[] { "Особиста зброя (найменування, серія, номер)", ImportTargetField.Weapon, ExportFieldKey.WeaponFull }
    };

    [Theory]
    [MemberData(nameof(SharedHeaders))]
    public void BothMappersReadTheSameHeaderTheSameWay(
        string header, ImportTargetField importField, ExportFieldKey exportField)
    {
        Assert.Equal(importField, Import(header));
        Assert.Equal(exportField, Export(header));
    }

    [Theory]
    [InlineData("По-батькові")]
    [InlineData("по-батькові")]
    public void HyphenatedPatronymicHeaderStillMaps(string header)
    {
        Assert.Equal(ImportTargetField.MiddleName, Import(header));
        Assert.Equal(ExportFieldKey.MiddleName, Export(header));
    }
}
