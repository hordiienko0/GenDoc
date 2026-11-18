using System.IO;
using GenDoc.Models;
using GenDoc.Models.Enums;
using GenDoc.Services;
using GenDoc.Services.Generation;
using GenDoc.Services.Intakes;
using GenDoc.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GenDoc.Tests.Intakes;

/// <summary>Перейменування набору переносить і теку з його документами на диску, а шляхи в базі йдуть слідом.</summary>
public sealed class IntakeDocumentFolderRenameTests : IDisposable
{
    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.Today);

    private readonly TestDb _db = new();
    private readonly string _root = Path.Combine(Path.GetTempPath(), "GenDocTests", "rename-" + Guid.NewGuid().ToString("N"));
    private readonly IIntakeService _service;
    private readonly int _baseId;
    private readonly int _personId;
    private readonly int _templateId;
    private readonly Intake _intake;

    public IntakeDocumentFolderRenameTests()
    {
        Directory.CreateDirectory(_root);
        new OutputFolderService(_db.Factory).SaveDefaultAsync(_root).GetAwaiter().GetResult();

        using (var ctx = _db.Factory.CreateDbContext())
        {
            var root = new OrgNode { Name = "Курс", Depth = 0, SortOrder = 0, Path = "/" };
            ctx.OrgNodes.Add(root);
            ctx.Users.Add(new UserProfile { FullName = "Тест", PasswordHash = "x", CreatedAt = DateTime.Now });
            var template = new Template
            {
                Name = "Допуск", OriginalFileName = "d.docx", Content = new byte[] { 1 },
                UploadedAt = DateTime.Now, Kind = TemplateKind.PerRecipient
            };
            ctx.Templates.Add(template);
            ctx.SaveChanges();
            root.Path = $"/{root.Id}/";
            ctx.SaveChanges();
            _baseId = root.Id;
            _templateId = template.Id;
        }

        var services = new ServiceCollection();
        services.AddSingleton(_db.Factory);
        services.AddSingleton<IAuditLogService>(new FakeAuditLog());
        services.AddSingleton<ICurrentUserContext>(new FakeCurrentUser());
        services.AddSingleton<IUserSettingsService, UserSettingsService>();
        services.AddSingleton<GenDoc.Services.Completeness.ICompletenessService>(_ => TestServices.Completeness(_db));
        services.AddSingleton<IOutputFolderService>(new OutputFolderService(_db.Factory));
        services.AddSingleton<IIntakeService, IntakeService>();
        services.AddSingleton<ActiveIntakeState>();
        _service = services.BuildServiceProvider().GetRequiredService<IIntakeService>();

        _intake = _service.CreateAsync(new IntakeCreateRequest("Набір №1", _baseId, Today, Today.AddMonths(3)))
            .GetAwaiter().GetResult();
        _personId = AddPerson(_intake.Id, "РЕД-001");
    }

    public void Dispose()
    {
        _db.Dispose();
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    private int AddPerson(int intakeId, string serviceNumber)
    {
        using var ctx = _db.Factory.CreateDbContext();
        var all = ctx.OrgNodes.First(n => n.IntakeId == intakeId && n.Name == IntakeFolderNames.All);
        var person = new Recipient
        {
            LastName = "КОВАЛЕНКО", FirstName = "Іван", Rank = "солдат", Position = "курсант",
            ServiceNumber = serviceNumber, OrgNodeId = all.Id, IntakeId = intakeId
        };
        ctx.Recipients.Add(person);
        ctx.SaveChanges();
        return person.Id;
    }

    private int AddDoc(string relativePath, int? intakeId = null, bool onDisk = true, string? orgPath = null, int? personId = null)
    {
        if (onDisk) WriteFile(relativePath, "docx");
        using var ctx = _db.Factory.CreateDbContext();
        var doc = new GeneratedDocument
        {
            RecipientId = personId ?? _personId, TemplateId = _templateId, GeneratedAt = DateTime.Now, GeneratedByUserId = 1,
            FileName = relativePath, Version = 1, IsCurrent = true, HasContent = false,
            SourceType = DocumentSourceType.Generated, IntakeId = intakeId ?? _intake.Id, OrgPathSnapshot = orgPath
        };
        ctx.GeneratedDocuments.Add(doc);
        ctx.SaveChanges();
        return doc.Id;
    }

    private int AddGroupDoc(string relativePath, int? intakeId = null)
    {
        WriteFile(relativePath, "xlsx");
        using var ctx = _db.Factory.CreateDbContext();
        var doc = new GeneratedGroupDocument
        {
            TemplateId = _templateId, IntakeId = intakeId ?? _intake.Id, GeneratedAt = DateTime.Now,
            GeneratedByUserId = 1, FileName = relativePath, Version = 1, IsCurrent = true
        };
        ctx.GeneratedGroupDocuments.Add(doc);
        ctx.SaveChanges();
        return doc.Id;
    }

    private void WriteFile(string relativePath, string content)
    {
        var path = Path.Combine(_root, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    private string FileNameOf(int docId)
    {
        using var ctx = _db.Factory.CreateDbContext();
        return ctx.GeneratedDocuments.IgnoreQueryFilters().Single(d => d.Id == docId).FileName;
    }

    private string GroupFileNameOf(int docId)
    {
        using var ctx = _db.Factory.CreateDbContext();
        return ctx.GeneratedGroupDocuments.IgnoreQueryFilters().Single(d => d.Id == docId).FileName;
    }

    private Task<IntakeUpdateResult> RenameAsync(string newName, int? baseId = null)
        => _service.UpdateAsync(new IntakeUpdateRequest(
            _intake.Id, newName, baseId ?? _baseId, _intake.DateStart, _intake.DateEnd));

    private string Old(string rest) => Path.Combine("Набір №1", rest);
    private string New(string rest) => Path.Combine("Набір №1-А", rest);

    [Fact]
    public async Task TheWholeFolderIsRenamedAndTheStoredPathsFollowIt()
    {
        var person = Path.Combine("Допуск", "2026-11-18", "КОВАЛЕНКО Іван (РЕД-001).docx");
        var first = AddDoc(Old(person));
        var second = AddDoc(Old(person));
        var group = AddGroupDoc(Old(Path.Combine("Список", "2026-11-18.xlsx")));
        WriteFile(Old("нотатки.txt"), "свої нотатки");
        var bare = AddDoc("КОВАЛЕНКО Іван Допуск.docx");
        var shared = AddGroupDoc(Path.Combine("Спільні", "Список", "2026-11-18.xlsx"));

        var result = await RenameAsync("Набір №1-А");

        Assert.Null(result.Warning);
        Assert.False(Directory.Exists(Path.Combine(_root, "Набір №1")));
        Assert.True(File.Exists(Path.Combine(_root, New(person))));
        Assert.True(File.Exists(Path.Combine(_root, New("нотатки.txt"))));

        Assert.Equal(New(person), FileNameOf(first));
        Assert.Equal(New(person), FileNameOf(second));
        Assert.Equal(New(Path.Combine("Список", "2026-11-18.xlsx")), GroupFileNameOf(group));
        Assert.True(GenerationService.ExistsInOutputFolder(_root, FileNameOf(first)));
        Assert.True(GenerationService.ExistsInOutputFolder(_root, GroupFileNameOf(group)));

        Assert.Equal("КОВАЛЕНКО Іван Допуск.docx", FileNameOf(bare));
        Assert.Equal(Path.Combine("Спільні", "Список", "2026-11-18.xlsx"), GroupFileNameOf(shared));
    }

    [Fact]
    public async Task AFolderSharedWithAnotherIntakeGivesUpOnlyThisIntakesFiles()
    {
        var other = await _service.CreateAsync(new IntakeCreateRequest("Набір №1", _baseId, Today, Today.AddMonths(3)));
        var otherPerson = AddPerson(other.Id, "ІНШ-001");
        var mine = AddDoc(Old(Path.Combine("Допуск", "2026-11-18", "мій.docx")));
        var theirs = AddDoc(Old(Path.Combine("Допуск", "2026-11-18", "чужий.docx")), other.Id, personId: otherPerson);

        var result = await RenameAsync("Набір №1-А");

        Assert.Null(result.Warning);
        Assert.Equal(New(Path.Combine("Допуск", "2026-11-18", "мій.docx")), FileNameOf(mine));
        Assert.True(File.Exists(Path.Combine(_root, FileNameOf(mine))));
        Assert.Equal(Old(Path.Combine("Допуск", "2026-11-18", "чужий.docx")), FileNameOf(theirs));
        Assert.True(File.Exists(Path.Combine(_root, FileNameOf(theirs))));
    }

    [Fact]
    public async Task AnExistingFolderWithTheNewNameIsMergedInto()
    {
        WriteFile(New("вже тут.txt"), "було раніше");
        var doc = AddDoc(Old(Path.Combine("Допуск", "2026-11-18", "мій.docx")));

        var result = await RenameAsync("Набір №1-А");

        Assert.Null(result.Warning);
        Assert.True(File.Exists(Path.Combine(_root, New("вже тут.txt"))));
        Assert.Equal(New(Path.Combine("Допуск", "2026-11-18", "мій.docx")), FileNameOf(doc));
        Assert.True(File.Exists(Path.Combine(_root, FileNameOf(doc))));
        Assert.False(Directory.Exists(Path.Combine(_root, "Набір №1")));
    }

    [Fact]
    public async Task AFileThatWouldOverwriteAnotherStaysWhereItIs()
    {
        var rest = Path.Combine("Допуск", "2026-11-18", "мій.docx");
        WriteFile(New(rest), "інший вміст");
        var doc = AddDoc(Old(rest));

        var result = await RenameAsync("Набір №1-А");

        Assert.NotNull(result.Warning);
        Assert.Equal(Old(rest), FileNameOf(doc));
        Assert.True(File.Exists(Path.Combine(_root, Old(rest))));
        Assert.Equal("інший вміст", File.ReadAllText(Path.Combine(_root, New(rest))));
    }

    [Fact]
    public async Task AFileOpenInAnotherProgramStaysAndTheRestMoves()
    {
        var locked = AddDoc(Old(Path.Combine("Допуск", "2026-11-18", "відкритий.docx")));
        var free = AddDoc(Old(Path.Combine("Допуск", "2026-11-18", "вільний.docx")));

        IntakeUpdateResult result;
        using (new FileStream(Path.Combine(_root, FileNameOf(locked)), FileMode.Open, FileAccess.Read, FileShare.None))
            result = await RenameAsync("Набір №1-А");

        Assert.NotNull(result.Warning);
        Assert.Equal(Old(Path.Combine("Допуск", "2026-11-18", "відкритий.docx")), FileNameOf(locked));
        Assert.True(File.Exists(Path.Combine(_root, FileNameOf(locked))));
        Assert.Equal(New(Path.Combine("Допуск", "2026-11-18", "вільний.docx")), FileNameOf(free));
        Assert.True(File.Exists(Path.Combine(_root, FileNameOf(free))));
    }

    [Fact]
    public async Task WithoutAFolderOnDiskOnlyTheStoredPathsChange()
    {
        var doc = AddDoc(Old(Path.Combine("Допуск", "2026-11-18", "мій.docx")), onDisk: false);

        var result = await RenameAsync("Набір №1-А");

        Assert.Null(result.Warning);
        Assert.Equal(New(Path.Combine("Допуск", "2026-11-18", "мій.docx")), FileNameOf(doc));
        Assert.False(Directory.Exists(Path.Combine(_root, "Набір №1-А")));
    }

    [Fact]
    public async Task ChangingOnlyTheDatesLeavesTheFilesAlone()
    {
        var doc = AddDoc(Old(Path.Combine("Допуск", "2026-11-18", "мій.docx")));

        await _service.UpdateAsync(new IntakeUpdateRequest(
            _intake.Id, "Набір №1", _baseId, Today.AddDays(1), Today.AddMonths(4)));

        Assert.Equal(Old(Path.Combine("Допуск", "2026-11-18", "мій.docx")), FileNameOf(doc));
        Assert.True(File.Exists(Path.Combine(_root, FileNameOf(doc))));
    }

    [Fact]
    public async Task TheUnitPathShownInTheArchiveFollowsTheNameAndTheBaseFolder()
    {
        int otherBaseId;
        using (var ctx = _db.Factory.CreateDbContext())
        {
            var other = new OrgNode { Name = "Інший курс", Depth = 0, SortOrder = 1, Path = "/" };
            ctx.OrgNodes.Add(other);
            ctx.SaveChanges();
            other.Path = $"/{other.Id}/";
            ctx.SaveChanges();
            otherBaseId = other.Id;
        }
        var inside = AddDoc("a.docx", onDisk: false, orgPath: "Курс / Набір №1 / Всі / Придатні");
        var root = AddDoc("b.docx", onDisk: false, orgPath: "Курс / Набір №1");
        var lookalike = AddDoc("c.docx", onDisk: false, orgPath: "Курс / Набір №10 / Всі");

        await RenameAsync("Набір №1-А", otherBaseId);

        using var check = _db.Factory.CreateDbContext();
        string? PathOf(int id) => check.GeneratedDocuments.IgnoreQueryFilters().Single(d => d.Id == id).OrgPathSnapshot;
        Assert.Equal("Інший курс / Набір №1-А / Всі / Придатні", PathOf(inside));
        Assert.Equal("Інший курс / Набір №1-А", PathOf(root));
        Assert.Equal("Курс / Набір №10 / Всі", PathOf(lookalike));
    }
}
