namespace GenDoc.Models
{
    public class AppSettings
    {
        public int Id { get; set; }

        public int BackupIntervalMinutes { get; set; } = 60;
        public int AutoLockMinutes { get; set; } = 15;

        public bool WatermarkEnabled { get; set; } = false;
        public string? WatermarkText { get; set; }
    }
}
