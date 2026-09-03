namespace GenDoc.Services.Documents
{
    public interface IWatermarkService
    {
        byte[] Apply(byte[] content, string fileName);
    }

    public class NoOpWatermarkService : IWatermarkService
    {
        public byte[] Apply(byte[] content, string fileName) => content;
    }
}
