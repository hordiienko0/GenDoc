using GenDoc.Services.Completeness;
using GenDoc.Services.Documents;
using GenDoc.Services.Generation;

namespace GenDoc.Tests.Infrastructure;

// Складає сервіси під тестом з TestDb і заглушок з Fakes.cs, щоб кожен файл
// тестів архіву/генерації не дублював власний BuildService.
public static class TestServices
{
    public static DocumentArchiveService Archive(TestDb db) => new(
        db.Factory,
        new FakeAuditLog(),
        new FakeCurrentUser(),
        new FakeTempFiles(),
        new NoOpWatermarkService(),
        new DocumentGenerationService(),
        new DocumentHashService());

    public static GenerationService Generation(TestDb db) => new(
        db.Factory,
        new DocumentGenerationService(),
        new XlsxGenerationService(),
        new FakeAuditLog(),
        new FakeCurrentUser(),
        new DocumentHashService());

    public static GenDoc.Services.Staff.StaffService Staff(TestDb db, int? userId = null) => new(
        db.Factory,
        Completeness(db, userId),
        new FakeAuditLog(),
        new FakeCurrentUser(),
        UserSettings(db, userId));

    public static CompletenessService Completeness(
        TestDb db, int? userId = null, GenDoc.Models.Intake? activeIntake = null) => new(
        db.Factory,
        new FakeAuditLog(),
        new FakeCurrentUser(),
        new DocumentGenerationService(),
        new DocumentHashService(),
        new NoOpWatermarkService(),
        activeIntake is null ? new FakeIntakeAccessor() : new FakeIntakeAccessor(activeIntake),
        UserSettings(db, userId));

    public static GenDoc.Services.Generation.ManualTagFormBuilder ManualTagForm(
        TestDb db, GenDoc.Services.Staff.IStaffService staffService, GenDoc.Services.ICurrentUserContext currentUser,
        GenDoc.Services.Completeness.IIntakeServiceAccessor intakeAccessor, int? userId = null) => new(
        db.Factory, staffService, currentUser, intakeAccessor, UserSettings(db, userId));

    private sealed class FixedUserContext : GenDoc.Services.ICurrentUserContext
    {
        public int? CurrentUserId { get; private set; }
        public string? CurrentUserFullName { get; private set; }
        public FixedUserContext(int? id) { CurrentUserId = id; CurrentUserFullName = id?.ToString(); }
        public void SetCurrentUser(int userId, string fullName) { CurrentUserId = userId; CurrentUserFullName = fullName; }
        public void Clear() { CurrentUserId = null; CurrentUserFullName = null; }
    }

    public static GenDoc.Services.UserSettingsService UserSettings(TestDb db, int? userId) =>
        new(db.Factory, new FixedUserContext(userId));
}
