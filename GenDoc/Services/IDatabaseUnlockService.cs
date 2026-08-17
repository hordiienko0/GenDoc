namespace GenDoc.Services
{
    public interface IDatabaseUnlockService
    {
        bool TryUnlock(string password, out string? errorMessage);
    }
}
