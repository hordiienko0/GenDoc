using GenDoc.Models.Enums;

namespace GenDoc.Models
{
    public class GenerationPackageExportTemplate
    {
        public int Id { get; set; }

        public int GenerationPackageId { get; set; }
        public GenerationPackage? GenerationPackage { get; set; }

        public int ExportTemplateId { get; set; }
        public ExportTemplate? ExportTemplate { get; set; }

        public int SortOrder { get; set; }
        public FitnessFilter FitnessFilter { get; set; } = FitnessFilter.All;
    }
}
