namespace GenDoc.ViewModels.Shell
{
    public interface IGuardedSection
    {
        Task<bool> TryLeaveAsync();
    }
}
