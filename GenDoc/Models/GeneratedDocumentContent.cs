namespace GenDoc.Models
{
    public class GeneratedDocumentContent
    {
        public int GeneratedDocumentId { get; set; }
        public GeneratedDocument? GeneratedDocument { get; set; }

        public byte[] Content { get; set; } = Array.Empty<byte>();
    }
}
