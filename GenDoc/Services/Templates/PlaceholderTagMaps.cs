using GenDoc.Models.Enums;

namespace GenDoc.Services.Templates
{
    // Спільний словник {{тег}} → (джерело, поле) для DOCX (TemplateService) і XLSX
    // (ExportTemplateService) пайплайнів — щоб той самий тег класифікувався однаково
    // в обох.
    public static class PlaceholderTagMaps
    {
        public static readonly IReadOnlyDictionary<string, string> RecipientTagMap = new Dictionary<string, string>
        {
            ["піб"] = "FullNameFormatted",
            ["піб_скорочено"] = "ShortName",
            ["звання"] = "Rank",
            ["посада"] = "Position",
            ["підрозділ"] = "UnitName",
            ["особовий_номер"] = "ServiceNumber",
            ["номер"] = "RowNumber",
            ["дата_народження"] = "DateOfBirth",
            ["національність"] = "Nationality",
            ["вос"] = "Vos",
            ["сімейний_стан"] = "MaritalStatus",
            ["адреса_реєстрації"] = "RegistrationAddress",
            ["адреса_проживання"] = "ResidenceAddress",
            ["телефон"] = "Phone",
            ["примітка"] = "Note",
            ["група"] = "GroupName",
            ["піб_іноземною"] = "NameTransliterated",
            ["служив"] = "ServedBefore",
            ["автомобіль"] = "Vehicle",
            ["номер_посвідчення"] = "TravelCertificateNumber",
            ["прод_атестат"] = "FoodCertificate",
            ["дата_посвідчення"] = "TravelCertificateDate",
            ["звання_зв"] = "RankAccusative",
            ["піб_зв"] = "FullNameAccusative",
            ["прибув"] = "ArrivedVerb",
            ["таким"] = "SuchPronoun",

            // Теги, що реально трапляються у відомостях вогневої підготовки
            // (Роздавально-здавальна, Допуск Додаток 5, Залік Додаток 8). Без них
            // тег падав у Manual — тобто колонка лишалась порожньою або її щоразу
            // доводилось заповнювати руками.
            ["№"] = "RowNumber",
            ["піб_ініціали"] = "ShortName",
            ["кімната"] = "RoomDisplay",
            ["придатність"] = "FitnessCategory",

            ["зброя"] = "WeaponFull",
            ["зброя_назва"] = "WeaponName",
            ["зброя_номер"] = "WeaponSerialNumber",

            // XLSX-only: обидва обробляються окремо у XlsxGenerationService за
            // назвою поля (загальна оцінка рахується по сусідніх колонках рядка,
            // підпис курсового офіцера — дані поза поточним ростером).
            ["курсовий_офіцер"] = "CourseOfficerSignature",
            ["оцінка"] = "GradeRandom34",
            ["оцінка_1"] = "GradeRandom34",
            ["оцінка_2"] = "GradeRandom34",
            ["оцінка_3"] = "GradeRandom34",
            ["оцінка_4"] = "GradeRandom34",
            ["оцінка_загальна"] = "GradeOverall34"
        };

        public static readonly IReadOnlyDictionary<string, string> OrganizationTagMap = new Dictionary<string, string>
        {
            ["номер_вч"] = "UnitNumber",
            ["назва_вч"] = "UnitNumber",
            ["місто"] = "City",
            ["звання_командира"] = "CommanderRank",
            ["піб_командира"] = "CommanderFullName",
            ["посада_командира"] = "CommanderPosition",
            ["піб_кадровика"] = "HrOfficerFullName"
        };

        public static (MappingSourceType SourceType, string? FieldName) Classify(string tagWithBraces)
        {
            var inner = tagWithBraces.Trim('{', '}').Trim().ToLowerInvariant();

            if (RecipientTagMap.TryGetValue(inner, out var recipientField))
                return (MappingSourceType.Recipient, recipientField);

            if (OrganizationTagMap.TryGetValue(inner, out var organizationField))
                return (MappingSourceType.Organization, organizationField);

            return (MappingSourceType.Manual, null);
        }
    }
}
