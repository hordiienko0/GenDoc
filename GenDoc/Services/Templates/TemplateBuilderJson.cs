using System.Text.Json;
using System.Text.Json.Serialization;
using GenDoc.Models.TemplateBuilder;

namespace GenDoc.Services.Templates
{
    public static class TemplateBuilderJson
    {
        private static readonly JsonSerializerOptions Options = new()
        {
            Converters = { new JsonStringEnumConverter() },
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        public static string Serialize(TemplateBuilderDocument document)
            => JsonSerializer.Serialize(document, Options);

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
