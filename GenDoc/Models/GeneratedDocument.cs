namespace GenDoc.Models
{
    public class GeneratedDocument
    {
        public int Id { get; set; }

        public int RecipientId { get; set; }
        public Recipient? Recipient { get; set; }

        public int TemplateId { get; set; }
        public Template? Template { get; set; }

        public DateTime GeneratedAt { get; set; }

        public int GeneratedByUserId { get; set; }
        public UserProfile? GeneratedByUser { get; set; }

        public string OutputFileName { get; set; } = string.Empty;
    }
}
