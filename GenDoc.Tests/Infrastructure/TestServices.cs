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

    public static GenDoc.Services.Staff.StaffService Staff(TestDb db) => new(
        db.Factory,
        Completeness(db),
        new FakeAuditLog(),
        new FakeCurrentUser());

    public static CompletenessService Completeness(TestDb db) => new(
        db.Factory,
        new FakeAuditLog(),
        new FakeCurrentUser(),
        new DocumentGenerationService(),
        new DocumentHashService(),
        new NoOpWatermarkService(),
        new FakeIntakeAccessor());
}
