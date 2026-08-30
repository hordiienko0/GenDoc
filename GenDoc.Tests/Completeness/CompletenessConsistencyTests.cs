using GenDoc.Models;
using GenDoc.Models.Enums;
using GenDoc.Services.Completeness;
using GenDoc.Tests.Infrastructure;
using GenDoc.ViewModels.Completeness;

namespace GenDoc.Tests.Completeness;

/// <summary>
/// Аудит 2026-08-28 знайшов чотири розбіжності, які виявились проявами одного:
/// «порахувати готовність» написано в чотирьох файлах по-різному.
///
/// Прийняте визначення: документ зараховано до готовності тоді й лише тоді,
/// коли він НАЯВНИЙ І НЕ ЗАСТАРІЛИЙ. «Пакет повний» не можна стверджувати,
/// поки частина документів прострочена.
/// </summary>
public class CompletenessConsistencyTests
{
    private static (int PackageId, int IntakeId, int PersonId, int TemplateId) SeedOnePersonalTemplate(
        TestDb db, TemplateRequirement regular = TemplateRequirement.Required,
        TemplateRequirement limited = TemplateRequirement.Required,
        string fitness = "придатний")
    {
        using var ctx = db.Factory.CreateDbContext();
        ctx.Users.Add(new UserProfile { FullName = "Тест", PasswordHash = "x", CreatedAt = DateTime.Now });
        ctx.AppSettings.Add(new AppSettings { DetectStaleDocuments = true });

        var intake = new Intake { Number = 1, DisplayNumber = "Набір №1" };
        ctx.Intakes.Add(intake);

        var person = TemplateFixtures.Person(1, "ШЕВЧЕНКО", "Тарас");
        person.FitnessCategory = fitness;
        ctx.Recipients.Add(person);

        var template = new Template
        {
            Name = "Акт", OriginalFileName = "a.docx", Content = new byte[] { 1 },
            UploadedAt = DateTime.Now, Kind = TemplateKind.PerRecipient
        };
        ctx.Templates.Add(template);
        ctx.SaveChanges();

        person.IntakeId = intake.Id;

        // «Застарілість» рахується тільки для шаблонів, у яких є мапінг: без
        // нього ComputeSourceHash не з чим порівнювати й стан завжди «свіжий».
        ctx.TemplateFieldMappings.Add(new TemplateFieldMapping
        {
            TemplateId = template.Id, PlaceholderTag = "{{піб}}",
            SourceType = MappingSourceType.Recipient, FieldName = "FullName"
        });

        var package = new GenerationPackage { Name = "П" };
        package.Templates.Add(new GenerationPackageTemplate
        {
            TemplateId = template.Id, SortOrder = 0,
            RequirementRegular = regular, RequirementLimited = limited
        });
        ctx.GenerationPackages.Add(package);
        ctx.SaveChanges();

        return (package.Id, intake.Id, person.Id, template.Id);
    }

    private static void AddDocument(TestDb db, int personId, int templateId, int? intakeId, bool stale)
    {
        using var ctx = db.Factory.CreateDbContext();
        ctx.GeneratedDocuments.Add(new GeneratedDocument
        {
            RecipientId = personId, TemplateId = templateId, IntakeId = intakeId,
            FileName = "a.docx", GeneratedAt = DateTime.Now, GeneratedByUserId = 1,
            Version = 1, IsCurrent = true, HasContent = true,
            // Хеш, що завідомо не збігається → документ вважається застарілим.
            SourceHash = stale ? "СТАРИЙ-ХЕШ" : null
        });
        ctx.SaveChanges();
    }

    // C5: категорія порівнюється через єдину точку (FitnessCategoryHelper),
    // а не ordinal-рядком - інакше «Придатний» з великої мовчки ставав
    // «обмежено придатним» у матриці й «придатним» у фільтрах генерації.
    [Theory]
    [InlineData("придатний")]
    [InlineData("Придатний")]
    [InlineData("ПРИДАТНИЙ")]
    [InlineData(null)]
    public void Resolve_TreatsFitCategoryCaseInsensitively(string? fitness)
    {
        var template = new MatrixTemplateInfo(
            1, 1, "Акт", null, 0,
            RequirementRegular: TemplateRequirement.Required,
            RequirementLimited: TemplateRequirement.NotApplicable);

        Assert.Equal(TemplateRequirement.Required, ICompletenessService.Resolve(template, fitness));
    }

    [Fact]
    public void Resolve_LimitedCategoryStillUsesLimitedRequirement()
    {
        var template = new MatrixTemplateInfo(
            1, 1, "Акт", null, 0,
            RequirementRegular: TemplateRequirement.Required,
            RequirementLimited: TemplateRequirement.NotApplicable);

        Assert.Equal(TemplateRequirement.NotApplicable,
            ICompletenessService.Resolve(template, "обмежено придатний"));
    }

    // C4: документ, згенерований до v6, має IntakeId = NULL (backfill робився лише
    // для GenerationPackageRuns). Матриця фільтрувала за IntakeId і показувала «-»,
    // хоча картка особи й архів документ бачили, а «Згенерувати все, чого бракує»
    // створювала дублі-версії.
    [Fact]
    public async Task Matrix_ShowsLegacyDocumentWithoutIntakeId()
    {
        using var db = new TestDb();
        var (packageId, intakeId, personId, templateId) = SeedOnePersonalTemplate(db);
        AddDocument(db, personId, templateId, intakeId: null, stale: false);

        var data = await TestServices.Completeness(db).BuildAsync(intakeId, packageId);

        Assert.True(data.Docs.ContainsKey((personId, templateId)),
            "Документ без IntakeId (до v6) не потрапив у матрицю.");
    }

    // C2: зведення по набору не зараховувало застарілий документ, а рядок матриці
    // зараховував. Через це один екран показував «Пакет повний», а другий - 0 %.
    [Fact]
    public async Task StaleDocument_CountsAsNotReady_InBothSummaryAndMatrixRow()
    {
        using var db = new TestDb();
        var (packageId, intakeId, personId, templateId) = SeedOnePersonalTemplate(db);
        AddDocument(db, personId, templateId, intakeId, stale: true);

        var service = TestServices.Completeness(db);
        var summary = await service.GetIntakeSummaryAsync(intakeId, packageId);
        var data = await service.BuildAsync(intakeId, packageId);

        Assert.Equal(0, summary.SatisfiedCells);
        Assert.Equal(1, summary.RequiredCells);

        var row = BuildRow(data, personId, templateId);

        Assert.Equal(1, row.RequiredTotal);
        Assert.Equal(0, row.RequiredPresent);
    }

    [Fact]
    public async Task FreshDocument_CountsAsReady_InBothSummaryAndMatrixRow()
    {
        using var db = new TestDb();
        var (packageId, intakeId, personId, templateId) = SeedOnePersonalTemplate(db);
        AddDocument(db, personId, templateId, intakeId, stale: false);

        var service = TestServices.Completeness(db);
        var summary = await service.GetIntakeSummaryAsync(intakeId, packageId);
        var data = await service.BuildAsync(intakeId, packageId);

        Assert.Equal(1, summary.SatisfiedCells);

        Assert.Equal(1, BuildRow(data, personId, templateId).RequiredPresent);
    }

    // C1: картка особи не резолвила вимогу за категорією, тож шаблон, позначений
    // «не потрібен» для обмежено придатних, вважався бракуючим - і «Сформувати
    // повний пакет» його ГЕНЕРУВАЛА, хоча в матриці його не було видно.
    [Fact]
    public async Task RecipientStatus_MarksRequirementPerFitnessCategory()
    {
        using var db = new TestDb();
        var (packageId, _, personId, templateId) = SeedOnePersonalTemplate(
            db,
            regular: TemplateRequirement.Required,
            limited: TemplateRequirement.NotApplicable,
            fitness: "обмежено придатний");

        var statuses = await TestServices.Completeness(db).GetRecipientStatusAsync(personId, packageId);

        var status = Assert.Single(statuses);
        Assert.Equal(templateId, status.TemplateId);
        Assert.Equal(TemplateRequirement.NotApplicable, status.Requirement);
    }

    [Fact]
    public async Task GenerateMissingForRecipient_SkipsTemplatesNotApplicableToThisPerson()
    {
        using var db = new TestDb();
        var (packageId, _, personId, _) = SeedOnePersonalTemplate(
            db,
            regular: TemplateRequirement.Required,
            limited: TemplateRequirement.NotApplicable,
            fitness: "обмежено придатний");

        var result = await TestServices.Completeness(db)
            .GenerateMissingForRecipientAsync(personId, packageId, new Dictionary<string, string>());

        Assert.Equal(0, result.Generated);
    }

    // C3: бейдж навігації мусить дорівнювати рівно тому, на що є кнопки на екрані:
    // «бракує обов'язкових персональних» + «застарілих обов'язкових». Раніше він
    // рахував і застарілі «н/п» (яких матриця не показує), і групові шаблони
    // (яких кнопка не чіпає), тож число не можна було звести до нуля ніколи.
    [Fact]
    public async Task Badge_DoesNotCountStaleDocumentsOfNotApplicableTemplates()
    {
        using var db = new TestDb();
        var (packageId, intakeId, personId, templateId) = SeedOnePersonalTemplate(
            db,
            regular: TemplateRequirement.NotApplicable,
            limited: TemplateRequirement.NotApplicable,
            fitness: "придатний");
        AddDocument(db, personId, templateId, intakeId, stale: true);

        Intake activeIntake;
        using (var ctx = db.Factory.CreateDbContext())
            activeIntake = ctx.Intakes.First(i => i.Id == intakeId);

        var badge = await TestServices.Completeness(db, activeIntake: activeIntake).GetBadgeCountAsync();

        Assert.Equal(0, badge);
    }

    // Друга половина того ж дефекту: бейдж рахував і ГРУПОВІ шаблони, яких кнопка
    // «Згенерувати все, чого бракує» не чіпає, тож число не зводилось до нуля.
    [Fact]
    public async Task Badge_CountsOnlyWhatTheScreenButtonsCanFix()
    {
        using var db = new TestDb();
        var (packageId, intakeId, personId, _) = SeedOnePersonalTemplate(db);

        int groupTemplateId;
        using (var ctx = db.Factory.CreateDbContext())
        {
            var group = new Template
            {
                Name = "Рапорт ГРУПОВИЙ", OriginalFileName = "g.docx", Content = new byte[] { 1 },
                UploadedAt = DateTime.Now, Kind = TemplateKind.Group
            };
            ctx.Templates.Add(group);
            ctx.SaveChanges();
            groupTemplateId = group.Id;

            ctx.GenerationPackageTemplates.Add(new GenerationPackageTemplate
            {
                GenerationPackageId = packageId, TemplateId = groupTemplateId, SortOrder = 1,
                RequirementRegular = TemplateRequirement.Required,
                RequirementLimited = TemplateRequirement.Required
            });
            ctx.SaveChanges();
        }

        Intake activeIntake;
        using (var ctx = db.Factory.CreateDbContext())
            activeIntake = ctx.Intakes.First(i => i.Id == intakeId);

        var badge = await TestServices.Completeness(db, activeIntake: activeIntake).GetBadgeCountAsync();

        // Персональний шаблон бракує - це 1. Груповий у бейдж не входить.
        Assert.Equal(1, badge);
        Assert.True(personId > 0);
    }

    // Збирає рядок матриці рівно так, як це робить CompletenessViewModel.
    private static MatrixRowViewModel BuildRow(MatrixData data, int personId, int templateId)
    {
        var person = data.People.Single(p => p.Id == personId);
        var template = data.Templates.Single(t => t.TemplateId == templateId);
        var row = new MatrixRowViewModel(person);

        var cell = new MatrixCellViewModel(
            new NoOpCellCoordinator(), personId, templateId, row.FitnessCategory,
            ICompletenessService.Resolve(template, person.FitnessCategory), template.IsGroup);

        data.Docs.TryGetValue((personId, templateId), out var doc);
        cell.Initialize(doc);

        row.Cells.Add(cell);
        row.RecomputeReadiness();
        return row;
    }

    private sealed class NoOpCellCoordinator : ICellActionCoordinator
    {
        public Task OpenAsync(MatrixCellViewModel cell) => Task.CompletedTask;
        public Task GenerateAsync(MatrixCellViewModel cell) => Task.CompletedTask;
        public Task RegenerateAsync(MatrixCellViewModel cell) => Task.CompletedTask;
        public Task HistoryAsync(MatrixCellViewModel cell) => Task.CompletedTask;
        public Task SaveAsAsync(MatrixCellViewModel cell) => Task.CompletedTask;
    }
}
