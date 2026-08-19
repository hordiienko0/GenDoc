using GenDoc.Models.Common;

namespace GenDoc.Models
{
    public class OrgNode : ISoftDeletable
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;

        // Значення для підстановки в {{підрозділ}} у документах; якщо null - Name.
        public string? DocumentName { get; set; }

        public int? ParentId { get; set; }
        public OrgNode? Parent { get; set; }
        public ICollection<OrgNode> Children { get; set; } = new List<OrgNode>();

        // Materialized path: "/1/4/9/" (включає власний Id).
        public string Path { get; set; } = string.Empty;
        public int Depth { get; set; }
        public int SortOrder { get; set; }

        public int? IntakeId { get; set; }
        public Intake? Intake { get; set; }

        public bool IsActive { get; set; } = true;

        public DateTime? DeletedAt { get; set; }
        public string? DeletedBy { get; set; }

        public ICollection<Recipient> Recipients { get; set; } = new List<Recipient>();
    }
}
