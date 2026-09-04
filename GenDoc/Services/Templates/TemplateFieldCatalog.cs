namespace GenDoc.Services.Templates;

public record TemplateFieldOption(string FieldName, string DisplayName);

public static class TemplateFieldCatalog
{
    public static readonly IReadOnlyList<TemplateFieldOption> RecipientFields = new List<TemplateFieldOption>
    {
        new("FullNameFormatted", "ПІБ (ПРІЗВИЩЕ Ім'я По батькові)"),
        new("ShortName", "ПІБ скорочено (ПРІЗВИЩЕ І.П.)"),
        new("LastName", "Прізвище"),
        new("FirstName", "Ім'я"),
        new("MiddleName", "По батькові"),
        new("Rank", "Звання"),
        new("Position", "Посада"),
        new("UnitName", "Підрозділ"),
        new("ServiceNumber", "Особовий номер"),
        new("DateOfBirth", "Дата народження"),
        new("Nationality", "Національність"),
        new("Vos", "ВОС на який навчається"),
        new("CourseArrivalDate", "З якого часу прибув на курси П та ПК"),
        new("MaritalStatus", "Сімейний стан"),
        new("RegistrationAddress", "Адреса реєстрації"),
        new("ResidenceAddress", "Адреса фактичного проживання"),
        new("Phone", "Телефон"),
        new("Note", "Примітка"),
        new("GroupName", "Група"),
        new("NameTransliterated", "ПІБ на іноземній мові"),
        new("ServedBefore", "Служив/не служив"),
        new("ExtraNote", "Примітка (2)"),
        new("CommanderContact", "Командир (ПІП та телефон)"),
        new("TravelCertificateNumber", "№ посвідчення про відрядження"),
        new("TravelCertificateDate", "Дата посвідчення про відрядження"),
        new("FoodCertificate", "Прод атестат"),
        new("IdDocumentNumber", "Номер посвідчення офіцера/військового квитка"),
        new("MedicalBoard", "ВЛК, №, дата"),
        new("MedicalBoardConclusion", "Висновок ВЛК"),
        new("FitnessCategory", "Категорія придатності"),
        new("OriginUnit", "З якої частини прибув"),
        new("Vehicle", "Автомобіль, номер авто"),
        new("IsCourseOfficer", "Курсовий офіцер (Так/Ні)"),
        new("WeaponName", "Зброя: найменування"),
        new("WeaponSerialNumber", "Зброя: серія та номер"),
        new("WeaponFull", "Зброя: повний рядок"),
        new("RoomDisplay", "Кімната"),
        new("RankAccusative", "Звання (знахідний відмінок)"),
        new("FullNameAccusative", "ПІБ (знахідний відмінок)"),
        new("ArrivedVerb", "«прибув/прибула» (узгодження за родом)"),
        new("SuchPronoun", "«таким/такою» (узгодження за родом)"),
        new("RowNumber", "№ з/п (рядок відомості)"),
        new("GradeRandom34", "Оцінка (3 або 4, стабільна)"),
        new("GradeOverall34", "Загальна оцінка (середнє по рядку)"),
        new("CourseOfficerSignature", "Підпис курсового офіцера")
    };

    public static readonly IReadOnlyList<TemplateFieldOption> OrganizationFields = new List<TemplateFieldOption>
    {
        new("UnitNumber", "Номер військової частини"),
        new("City", "Місто"),
        new("CommanderRank", "Звання командира"),
        new("CommanderFullName", "ПІБ командира"),
        new("CommanderPosition", "Посада командира"),
        new("HrOfficerFullName", "ПІБ начальника служби персоналу"),
        new("UnitFullName", "Повна назва частини/закладу")
    };
}
