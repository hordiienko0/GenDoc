namespace GenDoc.Models
{
    public class GeneratedGroupDocumentContent
    {
        public int GeneratedGroupDocumentId { get; set; }
        public GeneratedGroupDocument? GeneratedGroupDocument { get; set; }

        public byte[] Content { get; set; } = Array.Empty<byte>();
    }
}
