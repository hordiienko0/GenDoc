namespace GenDoc.ViewModels.Shell
{
    // Розділ із незбереженим станом: навігація питає дозволу перед виходом.
    public interface IGuardedSection
    {
        Task<bool> TryLeaveAsync();
    }
}
