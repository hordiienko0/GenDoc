namespace GenDoc.Models
{
    // Пер-профільний стан (v26): мій набір, останній пакет, фільтр «Мої» в архіві,
    // дати/підписант «з минулого разу». Глобальні AppSettings лишаються fallback-ом.
    public class UserSettings
    {
        public int Id { get; set; }
        public int UserProfileId { get; set; }
        public UserProfile? UserProfile { get; set; }
        public int? ActiveIntakeId { get; set; }
        public int? LastPackageId { get; set; }
        public bool ArchiveMineOnly { get; set; }
        public string? LastManualValuesJson { get; set; }
        public string? LastSignerByTemplateJson { get; set; }
    }
}
