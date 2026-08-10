using System.Text.Json;

namespace GenDoc.Services.Generation
{
    // Один рядок помилки запуску. Зберігається в наявній колонці
    // GenerationPackageRun.Summary у вигляді JSON-масиву — без міграції схеми.
    // Старий формат (звичайний текст з рядками «ПІБ / Шаблон: помилка») читається
    // запасним парсером у DocumentArchiveService, бо в робочих базах він уже є.
    public sealed record RunIssue(string Phase, string Person, string TemplateName, string Message)
    {
        public const string PhaseDocx = "docx";
        public const string PhaseXlsx = "xlsx";
        public const string PhaseDocxGroup = "docx-group";

        private static readonly JsonSerializerOptions Options = new()
        {
            // Кирилиця має лишатись читабельною і в самій базі.
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };

        public static string? Serialize(IReadOnlyList<RunIssue> issues)
            => issues.Count == 0 ? null : JsonSerializer.Serialize(issues, Options);

        public static bool TryDeserialize(string? raw, out List<RunIssue> issues)
        {
            issues = new List<RunIssue>();
            if (string.IsNullOrWhiteSpace(raw)) return false;
            if (raw.TrimStart()[0] != '[') return false;

            try
            {
                issues = JsonSerializer.Deserialize<List<RunIssue>>(raw, Options) ?? new List<RunIssue>();
                return true;
            }
            catch (JsonException)
            {
                issues = new List<RunIssue>();
                return false;
            }
        }
    }
}
