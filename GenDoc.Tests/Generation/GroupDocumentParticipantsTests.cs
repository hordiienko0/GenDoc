using DocumentFormat.OpenXml.Packaging;
using GenDoc.Models;
using GenDoc.Models.Enums;
using GenDoc.Services.Generation;
using GenDoc.Services.Templates;
using GenDoc.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace GenDoc.Tests.Generation;

public class GroupDocumentParticipantsTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), $"gendoc-part-{Guid.NewGuid():N}");
    public void Dispose() { if (Directory.Exists(_folder)) Directory.Delete(_folder, true); }
    private static readonly IProgress<string> NoProgress = new Progress<string>(_ => { });

    private static (int PackageId, List<int> PeopleIds) SeedGroupDocxPackage(TestDb db, int peopleCount)
    {
        using var ctx = db.Factory.CreateDbContext();
        ctx.Users.Add(new UserProfile { FullName = "Тест", PasswordHash = "x", CreatedAt = DateTime.Now });
        ctx.OrganizationSettings.Add(new OrganizationSettings
        {
            UnitNumber = "А1234", City = "Львів", CommanderRank = "полковник", CommanderFullName = "І. ПЕТРЕНКО",
            CommanderPosition = "начальник", HrOfficerFullName = "К. КАДРОВ", UnitFullName = "Коледж"
        });

        var bytes = TemplateFixtures.Bytes(TemplateFixtures.RaportGroupDocx);
        var template = new Template
        {
            Name = "Рапорт котлове ГРУПОВИЙ", OriginalFileName = "group.docx",
            Content = bytes, UploadedAt = DateTime.Now, Kind = TemplateKind.Group
        };
        ctx.Templates.Add(template);
        var people = TemplateFixtures.Roster(peopleCount);
        ctx.Recipients.AddRange(people);
        ctx.SaveChanges();

        using (var stream = new MemoryStream(bytes))
        using (var doc = WordprocessingDocument.Open(stream, false))
        {
            var scan = TemplateService.ScanPlaceholders(doc);
            foreach (var (tag, insideBlock) in scan.Tags)
            {
                var (sourceType, fieldName) = PlaceholderTagMaps.Classify(tag);
                ctx.TemplateFieldMappings.Add(new TemplateFieldMapping
                {
                    TemplateId = template.Id, PlaceholderTag = tag,
                    SourceType = sourceType, FieldName = fieldName,
                    IsInsideRepeatingBlock = insideBlock
                });
            }
        }

        var package = new GenerationPackage { Name = "Груповий" };
        package.Templates.Add(new GenerationPackageTemplate { TemplateId = template.Id, SortOrder = 0 });
        ctx.GenerationPackages.Add(package);
        ctx.SaveChanges();
        return (package.Id, people.Select(p => p.Id).ToList());
    }

    private RunResult Run(TestDb db, int packageId, IReadOnlyList<int>? onlyIds = null, bool regenerate = false)
        => TestServices.Generation(db).RunPackage(
            packageId, _folder, new Dictionary<string, string>(), regenerate,
            onlyIds is null
                ? RosterSelection.Everyone
                : new RosterSelection(false, onlyIds, FitnessFilter.All, false, Array.Empty<GenDoc.Services.RankCategory>(), Array.Empty<string>()),
            NoProgress);

    [Fact]
    public void GroupDocx_RecordsParticipantsOfTheRoster()
    {
        using var db = new TestDb();
        var (packageId, peopleIds) = SeedGroupDocxPackage(db, 3);

        var result = Run(db, packageId);

        Assert.Equal(1, result.DocxGroupGenerated);
        using var check = db.Factory.CreateDbContext();
        var doc = check.GeneratedGroupDocuments.Include(g => g.Recipients).Single();
        Assert.Equal(peopleIds.OrderBy(i => i),
            doc.Recipients.Select(r => r.RecipientId).OrderBy(i => i));
    }

    [Fact]
    public void Regenerate_NewVersionHasItsOwnRoster_OldKeepsIts()
    {
        using var db = new TestDb();
        var (packageId, peopleIds) = SeedGroupDocxPackage(db, 3);

        Run(db, packageId);
        Run(db, packageId, onlyIds: peopleIds.Take(2).ToList(), regenerate: true);

        using var check = db.Factory.CreateDbContext();
        var docs = check.GeneratedGroupDocuments.Include(g => g.Recipients)
            .IgnoreQueryFilters().OrderBy(g => g.Version).ToList();
        Assert.Equal(2, docs.Count);
        Assert.Equal(3, docs[0].Recipients.Count);
        Assert.False(docs[0].IsCurrent);
        Assert.Equal(2, docs[1].Recipients.Count);
        Assert.True(docs[1].IsCurrent);
    }
}
