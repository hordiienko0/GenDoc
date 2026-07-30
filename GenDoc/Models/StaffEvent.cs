using GenDoc.Models.Enums;

namespace GenDoc.Models
{
    // Період відрядження/відпустки постійного складу — визначає поточний "Стан" людини.
    public class StaffEvent
    {
        public int Id { get; set; }

        public int RecipientId { get; set; }
        public Recipient? Recipient { get; set; }

        public StaffEventKind Kind { get; set; }
        public DateOnly DateStart { get; set; }
        public DateOnly DateEnd { get; set; }
        public string? Note { get; set; }

        public DateTime CreatedAt { get; set; }
        public string? CreatedBy { get; set; }
    }
}
