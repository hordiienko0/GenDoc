namespace GenDoc.Services.Templates;

public record TemplateFieldOption(string FieldName, string DisplayName);

// Повний перелік полів, доступних для ручного перепризначення мітки у
// ComboBox-і мапінгу (Views/Templates). FieldName зберігається як рядок у
// TemplateFieldMapping.FieldName — узгоджено зі значеннями, які повертає
// DocumentGenerationService при побудові словника значень (Етап 3).
public static class TemplateFieldCatalog
{
    public static readonly IReadOnlyList<TemplateFieldOption> RecipientFields = new List<TemplateFieldOption>
    {
        new("FullNameFormatted", "ПІБ (ПРІЗВИЩЕ Ім'я По батькові)"),
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
        new("FoodCertificate", "Прод атестат"),
        new("IdDocumentNumber", "Номер посвідчення офіцера/військового квитка"),
        new("MedicalBoard", "ВЛК, №, дата"),
        new("MedicalBoardConclusion", "Висновок ВЛК"),
        new("OriginUnit", "З якої частини прибув"),
        new("Vehicle", "Автомобіль, номер авто"),
        new("RoomDisplay", "Кімната")
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
