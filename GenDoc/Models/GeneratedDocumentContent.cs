namespace GenDoc.Models
{
    // 1:1 з GeneratedDocument; НІКОЛИ не Include — тільки точковий запит байтів.
    public class GeneratedDocumentContent
    {
        public int GeneratedDocumentId { get; set; }
        public GeneratedDocument? GeneratedDocument { get; set; }

        public byte[] Content { get; set; } = Array.Empty<byte>();
    }
}
