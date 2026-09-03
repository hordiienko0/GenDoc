namespace GenDoc.Services
{
    public interface IDatabaseUnlockService
    {
        bool DatabaseExists { get; }

        string DatabasePath { get; }

        bool TryUnlock(string password, out string? errorMessage);
    }
}
