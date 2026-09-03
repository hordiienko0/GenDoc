using System.Text.Json;

namespace GenDoc.Services.Generation
{
    public sealed record RunIssue(string Phase, string Person, string TemplateName, string Message, bool IsError = true)
    {
        public const string PhaseDocx = "docx";
        public const string PhaseXlsx = "xlsx";
        public const string PhaseDocxGroup = "docx-group";

        private static readonly JsonSerializerOptions Options = new()
        {
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
