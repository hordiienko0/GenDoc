using GenDoc.Models;
using GenDoc.Models.Enums;
using GenDoc.Services.Completeness;
using GenDoc.Tests.Infrastructure;
using GenDoc.ViewModels.Completeness;

namespace GenDoc.Tests.Completeness;

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
            SourceHash = stale ? "СТАРИЙ-ХЕШ" : null
        });
        ctx.SaveChanges();
    }

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

    [Fact]
    public async Task Matrix_ShowsLegacyDocumentWithoutIntakeId()
    {
        using var db = new TestDb();
        var (packageId, intakeId, personId, templateId) = SeedOnePersonalTemplate(db);
        AddDocument(db, personId, templateId, intakeId: null, stale: false);

        var data = await TestServices.Completeness(db).BuildAsync(intakeId, packageId);

        Assert.True(data.Docs.ContainsKey((personId, templateId, false)),
            "Документ без IntakeId (до v6) не потрапив у матрицю.");
    }

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

    private static int AddSecondPersonAndGroupTemplate(TestDb db, int packageId, int intakeId)
    {
        using var ctx = db.Factory.CreateDbContext();
        var second = TemplateFixtures.Person(2, "ФРАНКО", "Іван");
        second.FitnessCategory = "придатний";
        second.IntakeId = intakeId;
        ctx.Recipients.Add(second);

        var group = new Template
        {
            Name = "Рапорт ГРУПОВИЙ", OriginalFileName = "g.docx", Content = new byte[] { 1 },
            UploadedAt = DateTime.Now, Kind = TemplateKind.Group
        };
        ctx.Templates.Add(group);
        ctx.SaveChanges();

        ctx.GenerationPackageTemplates.Add(new GenerationPackageTemplate
        {
            GenerationPackageId = packageId, TemplateId = group.Id, SortOrder = 1,
            RequirementRegular = TemplateRequirement.Required,
            RequirementLimited = TemplateRequirement.Required
        });
        ctx.SaveChanges();
        return group.Id;
    }

    [Fact]
    public async Task Badge_CountsAMissingGroupColumnOnce_LikeTheButtonAndTheIntakeCard()
    {
        using var db = new TestDb();
        var (packageId, intakeId, _, _) = SeedOnePersonalTemplate(db);
        AddSecondPersonAndGroupTemplate(db, packageId, intakeId);

        Intake activeIntake;
        using (var ctx = db.Factory.CreateDbContext())
            activeIntake = ctx.Intakes.First(i => i.Id == intakeId);

        var service = TestServices.Completeness(db, activeIntake: activeIntake);
        var badge = await service.GetBadgeCountAsync();
        var gaps = ICompletenessService.CountGaps(await service.BuildAsync(intakeId, packageId));
        var summary = await service.GetIntakeSummaryAsync(intakeId, packageId);

        Assert.Equal(3, badge);
        Assert.Equal(3, gaps.MissingRequired);
        Assert.Equal(2, summary.IncompletePeople);
        Assert.Equal(4, summary.RequiredCells);
    }

    [Fact]
    public async Task Badge_IgnoresAGroupColumnThatCoversEveryone()
    {
        using var db = new TestDb();
        var (packageId, intakeId, personId, templateId) = SeedOnePersonalTemplate(db);
        var groupTemplateId = AddSecondPersonAndGroupTemplate(db, packageId, intakeId);
        AddDocument(db, personId, templateId, intakeId, stale: false);

        Intake activeIntake;
        using (var ctx = db.Factory.CreateDbContext())
        {
            activeIntake = ctx.Intakes.First(i => i.Id == intakeId);
            var doc = new GeneratedGroupDocument
            {
                TemplateId = groupTemplateId, IntakeId = intakeId, GeneratedAt = DateTime.Now, GeneratedByUserId = 1,
                FileName = "g.docx", Version = 1, IsCurrent = true, HasContent = true, RecipientCount = 2
            };
            foreach (var id in ctx.Recipients.Where(r => r.IntakeId == intakeId).Select(r => r.Id).ToList())
                doc.Recipients.Add(new GeneratedGroupDocumentRecipient { RecipientId = id });
            ctx.GeneratedGroupDocuments.Add(doc);
            ctx.SaveChanges();
        }

        var service = TestServices.Completeness(db, activeIntake: activeIntake);
        var badge = await service.GetBadgeCountAsync();
        var summary = await service.GetIntakeSummaryAsync(intakeId, packageId);

        Assert.Equal(1, badge);
        Assert.Equal(1, summary.IncompletePeople);
        Assert.Equal(3, summary.SatisfiedCells);
    }

    private static MatrixRowViewModel BuildRow(MatrixData data, int personId, int templateId)
    {
        var person = data.People.Single(p => p.Id == personId);
        var template = data.Templates.Single(t => t.TemplateId == templateId);
        var row = new MatrixRowViewModel(person);

        var cell = new MatrixCellViewModel(
            new NoOpCellCoordinator(), personId, templateId, row.FitnessCategory,
            ICompletenessService.Resolve(template, person.FitnessCategory), template.IsGroup);

        data.Docs.TryGetValue((personId, templateId, false), out var doc);
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
