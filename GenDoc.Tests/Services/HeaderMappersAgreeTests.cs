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
        new object[] { "Особиста зброя (найменування, серія, номер)", ImportTargetField.Weapon, ExportFieldKey.WeaponFull },
        new object[] { "Освіта цивільна", ImportTargetField.CivilEducation, ExportFieldKey.CivilEducation },
        new object[] { "Освіта військова\n (ВОС)", ImportTargetField.MilitaryEducation, ExportFieldKey.MilitaryEducation },
        new object[] { "ВОС", ImportTargetField.Vos, ExportFieldKey.Vos },
        new object[] { "З якого часу в ЗСУ", ImportTargetField.ArmedForcesSince, ExportFieldKey.ArmedForcesSince },
        new object[] { "яким РТЦТК та СП\n призваний", ImportTargetField.ConscriptionOffice, ExportFieldKey.ConscriptionOffice },
        new object[] { "Сімейний стан\n (ПІП дружини рік народження; ім’я і рік народження дитини)",
            ImportTargetField.MaritalStatus, ExportFieldKey.MaritalStatus },
        new object[] { "ІПН", ImportTargetField.TaxNumber, ExportFieldKey.TaxNumber },
        new object[] { "Паспорт", ImportTargetField.Passport, ExportFieldKey.Passport },
        new object[] { "Місце народження", ImportTargetField.PlaceOfBirth, ExportFieldKey.PlaceOfBirth },
        new object[] { "Довідка\nВЛК № \nвід якої дати", ImportTargetField.MedicalBoard, ExportFieldKey.MedicalBoard },
        new object[] { "Наказ про присвоєння в/звання", ImportTargetField.RankOrder, ExportFieldKey.RankOrder },
        new object[] { "Військове звання", ImportTargetField.Rank, ExportFieldKey.Rank }
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
