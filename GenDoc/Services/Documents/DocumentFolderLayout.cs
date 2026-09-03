using System.Globalization;
using System.IO;

namespace GenDoc.Services.Documents
{
    public record DocumentPlacement(IReadOnlyList<string> Folders, string FileName)
    {
        public string RelativePath(string extension)
            => Path.Combine(Folders.Append(FileName + extension).ToArray());
    }

    public static class DocumentFolderLayout
    {
        public const string PermanentStaffFolder = "Постійний склад";

        public const string SharedFolder = "Спільні";

        private const string FallbackFolder = "Без назви";

        public static string RunStamp(DateTime now, bool dateFolderAlreadyUsed)
            => dateFolderAlreadyUsed
                ? now.ToString("yyyy-MM-dd HH-mm", CultureInfo.InvariantCulture)
                : now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        public static DocumentPlacement ForPerson(
            string? intakeName, string? templateName, string? runStamp,
            string? personName, string? serviceNumber = null)
        {
            var person = SanitizeFileStem(personName);

            if (!string.IsNullOrWhiteSpace(serviceNumber))
                person = $"{person} ({Sanitize(serviceNumber)})";

            return new DocumentPlacement(
                new[] { IntakeFolder(intakeName), Sanitize(templateName), Sanitize(runStamp) },
                person);
        }

        public static DocumentPlacement ForGroup(
            IEnumerable<string?> memberIntakeNames, string? templateName, string? runStamp)
        {
            var distinct = memberIntakeNames
                .Select(n => string.IsNullOrWhiteSpace(n) ? null : n.Trim())
                .Distinct(StringComparer.Ordinal)
                .Take(2)
                .ToList();

            var uniform = distinct.Count == 1 ? distinct[0] : null;

            return new DocumentPlacement(
                new[] { uniform is null ? SharedFolder : Sanitize(uniform), Sanitize(templateName) },
                Sanitize(runStamp));
        }

        private static string IntakeFolder(string? intakeName)
            => string.IsNullOrWhiteSpace(intakeName) ? PermanentStaffFolder : Sanitize(intakeName);

        internal static string Sanitize(string? name)
        {
            var cleaned = StripInvalid(name).TrimEnd('.', ' ');
            return cleaned.Length == 0 ? FallbackFolder : cleaned;
        }

        internal static string SanitizeFileStem(string? name)
        {
            var cleaned = StripInvalid(name);
            return cleaned.Length == 0 ? FallbackFolder : cleaned;
        }

        private static string StripInvalid(string? name)
        {
            var value = (name ?? string.Empty).Trim();
            if (value.Length == 0) return string.Empty;

            var invalid = Path.GetInvalidFileNameChars();
            var cleaned = new string(value.Select(c => invalid.Contains(c) ? ' ' : c).ToArray());

            return string.Join(" ", cleaned.Split(' ', StringSplitOptions.RemoveEmptyEntries));
        }
    }
}
