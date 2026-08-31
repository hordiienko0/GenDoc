using GenDoc.Data;
using GenDoc.Models;
using Microsoft.EntityFrameworkCore;

namespace GenDoc.Services;

public class UserProfileService : IUserProfileService
{
    private readonly IDbContextFactory<AppDbContext> _dbFactory;
    private readonly ICurrentUserContext _currentUserContext;

    public UserProfileService(IDbContextFactory<AppDbContext> dbFactory, ICurrentUserContext currentUserContext)
    {
        _dbFactory = dbFactory;
        _currentUserContext = currentUserContext;
    }

    public List<UserProfileListItem> GetActiveProfiles()
    {
        using var db = _dbFactory.CreateDbContext();
        return db.Users
            .OrderBy(u => u.FullName)
            .Select(u => new UserProfileListItem(u.Id, u.FullName))
            .ToList();
    }

    public bool TryLogin(int userProfileId, string password, out string? errorMessage)
    {
        errorMessage = null;
        using var db = _dbFactory.CreateDbContext();
        var profile = db.Users.FirstOrDefault(u => u.Id == userProfileId);

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
        using var db = _dbFactory.CreateDbContext();

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

        if (db.Users.Any(u => u.FullName == fullName))
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

        db.Users.Add(profile);
        db.SaveChanges();

        EnsureCourseOfficerCard(db, profile.FullName);

        _currentUserContext.SetCurrentUser(profile.Id, profile.FullName);
        return true;
    }

    /// <summary>
    /// Новий профіль - це новий курсовий офіцер, тож він одразу з'являється в
    /// постійному складі з відповідною ознакою. Без цього він не міг би ні
    /// підписати документ, ні потрапити в поле «Курсовий офіцер» у генерації,
    /// доки хтось не завів би йому картку руками (рішення користувача
    /// 2026-08-31).
    ///
    /// Якщо людина з таким ПІБ у постійному складі вже є - її картка лишається
    /// як є, додається лише ознака курсового: дублі в цьому списку гірші за
    /// незаповнені поля.
    /// </summary>
    private static void EnsureCourseOfficerCard(AppDbContext db, string fullName)
    {
        var name = FullNameParser.Split(fullName);
        if (name.LastName.Length == 0) return;

        // Постійний склад - це люди поза наборами (IntakeId is null).
        var existing = db.Recipients
            .Where(r => r.IntakeId == null)
            .AsEnumerable()
            .FirstOrDefault(r =>
                UkrainianCollation.IgnoreCase.Equals(r.LastName, name.LastName)
                && UkrainianCollation.IgnoreCase.Equals(r.FirstName, name.FirstName)
                && UkrainianCollation.IgnoreCase.Equals(r.MiddleName ?? string.Empty, name.MiddleName ?? string.Empty));

        if (existing is not null)
        {
            if (existing.IsCourseOfficer) return;
            existing.IsCourseOfficer = true;
            db.SaveChanges();
            return;
        }

        db.Recipients.Add(new Recipient
        {
            LastName = name.LastName,
            FirstName = name.FirstName,
            MiddleName = string.IsNullOrEmpty(name.MiddleName) ? null : name.MiddleName,
            // Звання й посаду курсовий заповнить сам у «Постійному складі»:
            // з імені профілю їх не вивести, а вигадувати не можна - вони
            // друкуються в документах.
            Rank = string.Empty,
            Position = string.Empty,
            ServiceNumber = string.Empty,
            IsCourseOfficer = true,
            IntakeId = null,
            OrgNodeId = null
        });
        db.SaveChanges();
    }
}