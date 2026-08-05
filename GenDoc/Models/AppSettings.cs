namespace GenDoc.Models
{
    public class AppSettings
    {
        public int Id { get; set; }

        public int BackupIntervalMinutes { get; set; } = 60;
        public int AutoLockMinutes { get; set; } = 15;

        public bool WatermarkEnabled { get; set; } = false;
        public string? WatermarkText { get; set; }

        // Шаблон назви набору; {n} — порядковий номер.
        public string? IntakeNumberTemplate { get; set; }

        // Шаблон імені файлу при експорті з архіву; {ПІБ}, {Шаблон}, {Дата}.
        public string? ExportFileNameTemplate { get; set; }

        // Ліміт розміру завантажуваної версії документа, КБ.
        public int? MaxDocumentSizeKb { get; set; }

        // Показувати стан «застарів» у матриці комплектності (null = увімкнено).
        public bool? DetectStaleDocuments { get; set; }

        // Пакет за замовчуванням для матриці комплектності й бейджа.
        public int? DefaultGenerationPackageId { get; set; }

        // JSON-словник тег → останнє введене значення для ручних міток
        // (форма «в догонку» та оформлення відрядження/відпустки), щоб не вводити повторно.
        public string? LastManualValuesJson { get; set; }

        // JSON-словник contextKey ("pkg:{id}"/"tpl:{id}") → RecipientId останнього
        // обраного підписанта рапорту — щоб не обирати заново щоразу.
        public string? LastSignerByTemplateJson { get; set; }
    }
}
