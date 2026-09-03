namespace GenDoc.Models
{
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
