using System.Text.Json;

namespace GenDoc.Services.Generation
{
    // Один рядок помилки запуску. Зберігається в наявній колонці
    // GenerationPackageRun.Summary у вигляді JSON-масиву — без міграції схеми.
    // Старий формат (звичайний текст з рядками «ПІБ / Шаблон: помилка») читається
    // запасним парсером у DocumentArchiveService, бо в робочих базах він уже є.
    //
    // IsError відрізняє справжній збій від інформаційної нотатки (наприклад,
    // «немає людей за фільтром» або «не заповнено теги» при успішній генерації) —
    // без цього поля DocumentArchiveService.GetRunItemsAsync показував і те, і те
    // як «помилка:», хоча лічильник ErrorCount ріс лише для справжніх збоїв.
    // Default = true навмисний: JSON, записаний до появи цього поля, не містить
    // IsError, і System.Text.Json підставляє для параметра конструктора саме це
    // значення за замовчуванням — а до цього поля в Summary були лише справжні
    // збої й ці ж нотатки (вже показані як помилки), тож "вважати помилкою" —
    // єдина сумісна поведінка для старих записів.
    public sealed record RunIssue(string Phase, string Person, string TemplateName, string Message, bool IsError = true)
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
                var parsed = JsonSerializer.Deserialize<List<RunIssue>>(raw, Options) ?? new List<RunIssue>();

                // Non-nullable параметри запису — це компіляторна, а не рантайм-гарантія:
                // System.Text.Json її не перевіряє і спокійно підставить null там, де
                // JSON-об'єкт не мав відповідного поля (наприклад, "[{}]"). Ця функція —
                // єдине місце, чия робота полягає в тому, щоб пережити будь-який вміст
                // колонки Summary (без CHECK-обмеження, редагована вручну), тож рядок
                // з такою формою вважаємо невалідним JSON і віддаємо на легасі-парсер,
                // а не падаємо з NullReferenceException у виклику.
                if (parsed.Exists(i => i.Phase is null || i.Person is null || i.TemplateName is null || i.Message is null))
                {
                    issues = new List<RunIssue>();
                    return false;
                }

                issues = parsed;
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
