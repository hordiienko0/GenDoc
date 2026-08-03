namespace GenDoc.Models
{
    // 1:1 з GeneratedGroupDocument; НІКОЛИ не Include — тільки точковий запит байтів.
    public class GeneratedGroupDocumentContent
    {
        public int GeneratedGroupDocumentId { get; set; }
        public GeneratedGroupDocument? GeneratedGroupDocument { get; set; }

        public byte[] Content { get; set; } = Array.Empty<byte>();
    }
}
