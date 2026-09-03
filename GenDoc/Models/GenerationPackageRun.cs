namespace GenDoc.Models
{
    public class GenerationPackageRun
    {
        public int Id { get; set; }

        public int? GenerationPackageId { get; set; }
        public GenerationPackage? GenerationPackage { get; set; }

        public DateTime RunAt { get; set; }

        public int RunByUserId { get; set; }
        public UserProfile? RunByUser { get; set; }

        public int GeneratedCount { get; set; }
        public int SkippedCount { get; set; }
        public int ErrorCount { get; set; }

        public string? Summary { get; set; }

        public int? IntakeId { get; set; }
        public string? BranchName { get; set; }
    }
}
