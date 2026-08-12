using System.Text.Json;
using System.Text.Json.Serialization;
using GenDoc.Models.TemplateBuilder;

namespace GenDoc.Services.Templates
{
    /// <summary>Серіалізація документа конструктора в Template.BuilderJson і назад.
    /// Enum'и пишемо рядками: JSON лежить у базі роками, а числа в ньому перетворять
    /// будь-яку зміну порядку TemplateBlockKind на мовчазне псування шаблонів.</summary>
    public static class TemplateBuilderJson
    {
        private static readonly JsonSerializerOptions Options = new()
        {
            Converters = { new JsonStringEnumConverter() },
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        public static string Serialize(TemplateBuilderDocument document)
            => JsonSerializer.Serialize(document, Options);

        /// <summary>null — якщо JSON порожній або зіпсований: шаблон тоді просто
        /// вважається завантаженим файлом, а не падає разом з екраном.</summary>
        public static TemplateBuilderDocument? Deserialize(string? json)
        {
            if (string.IsNullOrWhiteSpace(json)) return null;

            try
            {
                return JsonSerializer.Deserialize<TemplateBuilderDocument>(json, Options);
            }
            catch (JsonException)
            {
                return null;
            }
        }
    }
}
