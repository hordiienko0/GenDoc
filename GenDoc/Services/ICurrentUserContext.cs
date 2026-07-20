namespace GenDoc.Services
{
    public interface ICurrentUserContext
    {
        int? CurrentUserId { get; }
        string? CurrentUserFullName { get; }
        void SetCurrentUser(int userId, string fullName);
        void Clear();
    }
}
