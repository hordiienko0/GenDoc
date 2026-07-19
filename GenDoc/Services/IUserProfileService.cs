namespace GenDoc.Services
{
    public interface IUserProfileService
    {
        List<UserProfileListItem> GetActiveProfiles();
        bool TryLogin(int userProfileId, string password, out string? errorMessage);
        bool TryCreateProfile(string fullName, string password, out string? errorMessage);
    }
}
