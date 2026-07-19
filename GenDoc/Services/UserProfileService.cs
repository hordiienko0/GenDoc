using GenDoc.Data;
using GenDoc.Models;

namespace GenDoc.Services
{
    public class UserProfileService : IUserProfileService
    {
        private readonly AppDbContext _dbContext;
        private readonly ICurrentUserContext _currentUserContext;

        public UserProfileService(AppDbContext dbContext, ICurrentUserContext currentUserContext)
        {
            _dbContext = dbContext;
            _currentUserContext = currentUserContext;
        }

        public List<UserProfileListItem> GetActiveProfiles()
        {
            return _dbContext.Users
                .OrderBy(u => u.FullName)
                .Select(u => new UserProfileListItem(u.Id, u.FullName))
                .ToList();
        }

        public bool TryLogin(int userProfileId, string password, out string? errorMessage)
        {
            errorMessage = null;
            var profile = _dbContext.Users.FirstOrDefault(u => u.Id == userProfileId);

            if (profile is null)
            {
                errorMessage = "Профіль не знайдено.";
                return false;
            }

            if (!BCrypt.Net.BCrypt.Verify(password, profile.PasswordHash))
            {
                errorMessage = "Невірний пароль профілю.";
                return false;
            }

            _currentUserContext.SetCurrentUser(profile.Id, profile.FullName);
            return true;
        }

        public bool TryCreateProfile(string fullName, string password, out string? errorMessage)
        {
            errorMessage = null;

            if (string.IsNullOrWhiteSpace(fullName))
            {
                errorMessage = "Вкажіть ім'я профілю.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(password) || password.Length < 4)
            {
                errorMessage = "Пароль профілю має містити щонайменше 4 символи.";
                return false;
            }

            if (_dbContext.Users.Any(u => u.FullName == fullName))
            {
                errorMessage = "Профіль з таким іменем уже існує.";
                return false;
            }

            var profile = new UserProfile
            {
                FullName = fullName.Trim(),
                PasswordHash = BCrypt.Net.BCrypt.HashPassword(password),
                CreatedAt = DateTime.Now
            };

            _dbContext.Users.Add(profile);
            _dbContext.SaveChanges();

            _currentUserContext.SetCurrentUser(profile.Id, profile.FullName);
            return true;
        }
    }
}