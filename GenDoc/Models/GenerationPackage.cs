using GenDoc.Models.Common;

namespace GenDoc.Models
{
    public class GenerationPackage : ISoftDeletable
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string? Description { get; set; }

        public DateTime? DeletedAt { get; set; }
        public string? DeletedBy { get; set; }

        public ICollection<GenerationPackageTemplate> Templates { get; set; } = new List<GenerationPackageTemplate>();
        public ICollection<GenerationPackageExportTemplate> ExportTemplates { get; set; } = new List<GenerationPackageExportTemplate>();
        public ICollection<GenerationPackageRun> Runs { get; set; } = new List<GenerationPackageRun>();
    }
}
