namespace GenDoc.Models
{
    public class GeneratedGroupDocumentRecipient
    {
        public int Id { get; set; }

        public int GeneratedGroupDocumentId { get; set; }
        public GeneratedGroupDocument? GeneratedGroupDocument { get; set; }

        public int RecipientId { get; set; }
        public Recipient? Recipient { get; set; }
    }
}
