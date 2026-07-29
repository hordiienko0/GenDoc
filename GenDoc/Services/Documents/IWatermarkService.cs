namespace GenDoc.Services.Documents
{
    // Шов для майбутнього водяного знака на експорті; зараз NoOp.
    public interface IWatermarkService
    {
        byte[] Apply(byte[] content, string fileName);
    }

    public class NoOpWatermarkService : IWatermarkService
    {
        public byte[] Apply(byte[] content, string fileName) => content;
    }
}
