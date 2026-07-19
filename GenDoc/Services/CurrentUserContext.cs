namespace GenDoc.Services
{
    public class CurrentUserContext : ICurrentUserContext
    {
        public int? CurrentUserId { get; private set; }
        public string? CurrentUserFullName { get; private set; }

        public void SetCurrentUser(int userId, string fullName)
        {
            CurrentUserId = userId;
            CurrentUserFullName = fullName;
        }

        public void Clear()
        {
            CurrentUserId = null;
            CurrentUserFullName = null;
        }
    }
}
