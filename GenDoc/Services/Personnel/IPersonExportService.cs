namespace GenDoc.Services.Personnel
{
    /// <summary>
    /// Що користувач обрав минулого разу: колонки в їхньому порядку, сортування і чи розкладав на аркуші.
    /// </summary>
    public sealed class PersonExportPrefs
    {
        public List<string> Fields { get; set; } = new();
        public PersonExportSort Sort { get; set; } = PersonExportSort.Alphabet;

        /// <summary>Вивантаження окремими аркушами, а не одним списком.</summary>
        public bool BySheets { get; set; }

        /// <summary>Які аркуші були відмічені.</summary>
        public List<PersonExportSheetKind> Sheets { get; set; } = PersonExportSheets.Kinds.ToList();
    }

    public interface IPersonExportService
    {
        /// <summary>Усі люди папки разом із вкладеними; «лише ця папка» відсіюється вже фільтром.</summary>
        Task<IReadOnlyList<PersonExportRow>> LoadAsync(int nodeId);

        /// <summary>
        /// Усе дерево з людьми - для вивантаження аркушами, де папки відмічають галочками,
        /// як у графіку чергувань і вечірній перевірці.
        /// </summary>
        Task<PersonExportTree> LoadTreeAsync();

        Task<ExportResult> SaveXlsxAsync(
            IReadOnlyList<PersonExportRow> rows,
            IReadOnlyList<PersonExportField> fields,
            string filePath,
            string scopeTitle);

        /// <summary>Одна книга з кількома аркушами; колонки на всіх однакові, нумерація на кожному своя.</summary>
        Task<ExportResult> SaveXlsxAsync(
            IReadOnlyList<PersonExportSheet> sheets,
            IReadOnlyList<PersonExportField> fields,
            string filePath,
            string scopeTitle);

        /// <summary>Запис у журнал про копіювання таблиці в буфер обміну - це теж вивантаження персональних даних.</summary>
        void LogClipboardExport(int count, IReadOnlyList<PersonExportField> fields, string scopeTitle);

        Task<PersonExportPrefs> LoadPrefsAsync();
        Task SavePrefsAsync(PersonExportPrefs prefs);
    }
}
