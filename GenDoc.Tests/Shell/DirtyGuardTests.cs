using GenDoc.Models;
using GenDoc.Models.TemplateBuilder;
using GenDoc.Services.Templates;
using GenDoc.Tests.Infrastructure;
using GenDoc.ViewModels.Import;
using GenDoc.ViewModels.Personnel;
using GenDoc.ViewModels.Settings;
using GenDoc.ViewModels.Shell;
using GenDoc.ViewModels.Templates;
using GenDoc.ViewModels.Templates.Builder;

namespace GenDoc.Tests.Shell;

// Dirty-guard був лише в «Особовому складі»: конструктор шаблонів, майстер
// імпорту й налаштування втрачали роботу мовчки при переході в інший розділ,
// а закриття вікна не питало взагалі нікого (аудит 2026-08-28).
public class DirtyGuardTests
{
    private sealed class StubBuilderService : ITemplateBuilderService
    {
        public int SaveCalls { get; private set; }

        public IReadOnlyList<BuilderTestPerson> GetTestPeople() => Array.Empty<BuilderTestPerson>();
        public IReadOnlyList<BuilderSignatory> GetSignatories() => Array.Empty<BuilderSignatory>();
        public IReadOnlyDictionary<string, string> ResolveValues(int recipientId, IReadOnlyList<string> tags)
            => new Dictionary<string, string>();
        public byte[] BuildDocx(TemplateBuilderDocument document) => Array.Empty<byte>();
        public XlsxBuildResult BuildXlsx(TemplateBuilderDocument document) => new(Array.Empty<byte>(), 0);
        public BuilderTemplateSource? Load(int templateId, TemplateBuilderMode mode) => null;

        public int Save(int? templateId, string name, TemplateBuilderDocument document)
        {
            SaveCalls++;
            return templateId ?? 1;
        }
    }

    // ─── Розділи, що мають знати про незбережену роботу ───────────────────────

    public static IEnumerable<object[]> GuardedSections => new[]
    {
        new object[] { typeof(PersonnelViewModel) },
        new object[] { typeof(TemplatesViewModel) },
        new object[] { typeof(ImportViewModel) },
        new object[] { typeof(SettingsViewModel) }
    };

    [Theory]
    [MemberData(nameof(GuardedSections))]
    public void SectionsWithUnsavedWork_ImplementIGuardedSection(Type sectionType)
        => Assert.True(typeof(IGuardedSection).IsAssignableFrom(sectionType),
            $"{sectionType.Name} тримає незбережену роботу, але не реалізує IGuardedSection - "
            + "перехід в інший розділ мовчки її втратить.");

    // ─── Налаштування ────────────────────────────────────────────────────────

    private static SettingsViewModel NewSettings(TestDb db)
    {
        using (var ctx = db.Factory.CreateDbContext())
        {
            ctx.OrganizationSettings.Add(new OrganizationSettings
            {
                UnitNumber = "А0000",
                City = "Київ",
                CommanderRank = "полковник",
                CommanderFullName = "КОМАНДИРЕНКО Командир Командирович",
                HrOfficerFullName = "КАДРЕНКО Кадр Кадрович",
                CommanderPosition = "командир частини",
                UnitFullName = "військова частина А0000"
            });
            ctx.SaveChanges();
        }

        return new SettingsViewModel(db.Factory, new FakeAuditLog());
    }

    [Fact]
    public void Settings_AreCleanRightAfterLoading()
    {
        using var db = new TestDb();
        Assert.False(NewSettings(db).IsDirty);
    }

    [Fact]
    public void Settings_BecomeDirtyAfterAnEdit()
    {
        using var db = new TestDb();
        var vm = NewSettings(db);

        vm.City = "Львів";

        Assert.True(vm.IsDirty);
    }

    [Fact]
    public void Settings_AreCleanAgainAfterSaving()
    {
        using var db = new TestDb();
        var vm = NewSettings(db);
        vm.City = "Львів";

        // SaveCore, а не команда: команда показує підтвердження, а модальне
        // вікно в тест-хості валить процес.
        vm.SaveCore();

        Assert.False(vm.IsDirty);
    }

    // Повернення значення руками - теж «чисто»: інакше guard питав би про
    // зміни, яких уже немає.
    [Fact]
    public void Settings_AreCleanWhenTheValueIsTypedBack()
    {
        using var db = new TestDb();
        var vm = NewSettings(db);
        vm.City = "Львів";
        vm.City = "Київ";

        Assert.False(vm.IsDirty);
    }

    // ─── Конструктор шаблонів ────────────────────────────────────────────────

    [Fact]
    public void Builder_IsCleanRightAfterStartingANewTemplate()
    {
        var vm = new TemplateBuilderViewModel(new StubBuilderService());
        vm.StartNew(TemplateBuilderMode.Word);

        Assert.False(vm.IsDirty);
    }

    [Fact]
    public void Builder_BecomesDirtyWhenTheNameIsTyped()
    {
        var vm = new TemplateBuilderViewModel(new StubBuilderService());
        vm.StartNew(TemplateBuilderMode.Word);

        vm.TemplateName = "Довідка";

        Assert.True(vm.IsDirty);
    }

    [Fact]
    public void Builder_BecomesDirtyWhenABlockIsEdited()
    {
        var vm = new TemplateBuilderViewModel(new StubBuilderService());
        vm.StartNew(TemplateBuilderMode.Word);

        vm.Blocks[0].Text = "НАКАЗ";

        Assert.True(vm.IsDirty);
    }

    [Fact]
    public void Builder_IsCleanAgainAfterSaving()
    {
        var service = new StubBuilderService();
        var vm = new TemplateBuilderViewModel(service);
        vm.StartNew(TemplateBuilderMode.Word);
        vm.TemplateName = "Довідка";
        vm.Blocks[0].Text = "НАКАЗ";

        // Тут команда безпечна: конструктор показує вікно лише на помилці.
        vm.SaveCommand.Execute(null);

        Assert.Equal(1, service.SaveCalls);
        Assert.False(vm.IsDirty);
    }

    // ─── Майстер імпорту ─────────────────────────────────────────────────────

    private sealed class NullServiceProvider : IServiceProvider
    {
        public object? GetService(Type serviceType) => null;
    }

    private static ImportViewModel NewImport(TestDb db) => new(
        new GenDoc.Services.Import.ImportService(db.Factory, new FakeAuditLog()),
        new GenDoc.Services.Intakes.IntakeService(
            db.Factory, new FakeAuditLog(), new FakeCurrentUser(), new NullServiceProvider()),
        new GenDoc.Services.OrgTree.OrgTreeService(
            db.Factory, new FakeAuditLog(), new FakeCurrentUser()));

    // «Незбережене» тут - не поля картки, а сам незавершений майстер: обраний
    // файл і зіставлення колонок. Після успішного прогону майстер сам себе
    // скидає, тож і guard мовчить.
    [Fact]
    public void ImportWizard_HasNothingToLoseBeforeAFileIsChosen()
    {
        using var db = new TestDb();
        Assert.False(NewImport(db).HasPendingWork);
    }

    [Fact]
    public void ImportWizard_HasPendingWorkOnceAFileIsLoaded()
    {
        using var db = new TestDb();
        var vm = NewImport(db);

        vm.HasFile = true;

        Assert.True(vm.HasPendingWork);
    }

    // ─── Закриття вікна ──────────────────────────────────────────────────────

    // Guard спрацьовував лише на перехід МІЖ розділами. Хрестик закривав вікно
    // одразу, і незбережена картка гинула без запитання. Тест текстовий - як
    // StaticBindingScopeTests: вікно WPF у xunit не створити (STA + повний
    // граф сервісів), а сама помилка - у відсутньому обробнику.
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "GenDoc", "Views")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new DirectoryNotFoundException("Не знайдено корінь репозиторію");
    }

    [Fact]
    public void MainWindow_SubscribesToClosing()
    {
        var xaml = File.ReadAllText(Path.Combine(RepoRoot(), "GenDoc", "MainWindow.xaml"));
        Assert.Contains("Closing=", xaml);
    }

    [Fact]
    public void MainWindow_AsksTheGuardBeforeClosing()
    {
        var codeBehind = File.ReadAllText(Path.Combine(RepoRoot(), "GenDoc", "MainWindow.xaml.cs"));
        Assert.Contains(nameof(MainViewModel.TryLeaveCurrentSectionAsync), codeBehind);
    }
}
