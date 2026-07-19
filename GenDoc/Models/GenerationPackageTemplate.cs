namespace GenDoc.Models
{
    public class GenerationPackageTemplate
    {
        public int Id { get; set; }

        public int GenerationPackageId { get; set; }
        public GenerationPackage? GenerationPackage { get; set; }

        public int TemplateId { get; set; }
        public Template? Template { get; set; }

        public int SortOrder { get; set; }
    }
}
