using GenDoc.Models.Enums;

namespace GenDoc.Services.Templates
{
    public record PaletteField(string Tag, MappingSourceType SourceType);

    public record PaletteGroup(string Title, MappingSourceType SourceType, IReadOnlyList<PaletteField> Fields);

    /// <summary>
    /// Палітра полів для конструктора шаблонів. Будується з PlaceholderTagMaps, а не
    /// з власного переліку: тег, якого нема в мапах, мовчки класифікується як Manual —
    /// саме так колись «мовчав» {{курсовий_офіцер}}. Коли палітра і є мапи, розійтися
    /// вони не можуть за побудовою.
    /// </summary>
    public static class TemplateFieldPalette
    {
        public const string RecipientGroupTitle = "Про людину";
        public const string OrganizationGroupTitle = "Про частину";
        public const string ManualGroupTitle = "Вручну при генерації";

        /// <summary>Теги, які свідомо заповнює оператор на генерації. У мапах їх нема
        /// й бути не повинно — вони приходять з наказів, а не з бази.</summary>
        private static readonly string[] ManualTags =
        {
            "номер_наказу",
            "дата_наказу",
            "вид_зброї",
            "серійний_номер"
        };

        public static IReadOnlyList<PaletteGroup> Build()
        {
            return new[]
            {
                Group(RecipientGroupTitle, MappingSourceType.Recipient,
                    PlaceholderTagMaps.RecipientTagMap.Keys),
                Group(OrganizationGroupTitle, MappingSourceType.Organization,
                    PlaceholderTagMaps.OrganizationTagMap.Keys),
                Group(ManualGroupTitle, MappingSourceType.Manual, ManualTags)
            };
        }

        private static PaletteGroup Group(string title, MappingSourceType sourceType, IEnumerable<string> tags)
        {
            var fields = tags
                .Select(Wrap)
                .Distinct()
                .OrderBy(tag => tag, StringComparer.Ordinal)
                .Select(tag => new PaletteField(tag, sourceType))
                .ToList();

            return new PaletteGroup(title, sourceType, fields);
        }

        public static string Wrap(string tagName) => $"{{{{{tagName}}}}}";
    }
}
