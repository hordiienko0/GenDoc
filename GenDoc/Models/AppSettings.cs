namespace GenDoc.Models
{
    public class AppSettings
    {
        public int Id { get; set; }

        public int BackupIntervalMinutes { get; set; } = 60;
        public int AutoLockMinutes { get; set; } = 15;

        public bool WatermarkEnabled { get; set; } = false;
        public string? WatermarkText { get; set; }

        public string? IntakeNumberTemplate { get; set; }

        public string? ExportFileNameTemplate { get; set; }

        public string? DefaultOutputFolder { get; set; }

        public int? MaxDocumentSizeKb { get; set; }

        public bool? DetectStaleDocuments { get; set; }

        public int? DefaultGenerationPackageId { get; set; }

        public string? LastManualValuesJson { get; set; }

        public string? LastSignerByTemplateJson { get; set; }
    }
}
