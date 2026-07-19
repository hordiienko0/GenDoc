namespace GenDoc.Models
{
    public class SchemaVersion
    {
        public int Id { get; set; }
        public int Version { get; set; }
        public DateTime AppliedAt { get; set; }
        public string? Description { get; set; }
    }
}
