namespace GenDoc.Models
{
    // Учасник групового документа: хто був у складі відомості/групового рапорту
    // на момент генерації (2026-08-20). Кожна версія документа має власний склад.
    public class GeneratedGroupDocumentRecipient
    {
        public int Id { get; set; }

        public int GeneratedGroupDocumentId { get; set; }
        public GeneratedGroupDocument? GeneratedGroupDocument { get; set; }

        public int RecipientId { get; set; }
        public Recipient? Recipient { get; set; }
    }
}
