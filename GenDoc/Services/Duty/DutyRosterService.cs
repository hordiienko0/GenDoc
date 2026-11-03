using System.Globalization;
using System.Text.Json;
using GenDoc.Data;
using GenDoc.Models;
using GenDoc.Models.Enums;
using GenDoc.Services.Intakes;
using Microsoft.EntityFrameworkCore;

namespace GenDoc.Services.Duty;

public class DutyRosterService : IDutyRosterService
{
    /// <summary>Довший період не влазить на один аркуш A4 - і так рідко буває.</summary>
    public const int MaxDays = 92;

    private const int CarryOverDepth = 12;

    private readonly IDbContextFactory<AppDbContext> _dbFactory;
    private readonly IAuditLogService _auditLog;
    private readonly ICurrentUserContext _currentUser;
    private readonly IUserSettingsService _userSettings;

    public DutyRosterService(IDbContextFactory<AppDbContext> dbFactory, IAuditLogService auditLog,
        ICurrentUserContext currentUser, IUserSettingsService userSettings)
    {
        _dbFactory = dbFactory;
        _auditLog = auditLog;
        _currentUser = currentUser;
        _userSettings = userSettings;
    }

    public async Task<IReadOnlyList<DutyRosterListItem>> GetListAsync()
    {
        using var db = _dbFactory.CreateDbContext();
        var rosters = await db.DutyRosters.AsNoTracking()
            .OrderByDescending(r => r.DateFrom).ThenByDescending(r => r.Id)
            .Select(r => new { r.Id, r.DateFrom, r.DateTo, r.OrgNodeIds })
            .ToListAsync();

        var labels = await FoldersLabelsAsync(db, rosters.Select(r => r.OrgNodeIds));
        return rosters
            .Select(r => new DutyRosterListItem(r.Id, r.DateFrom, r.DateTo,
                r.OrgNodeIds is { } key && labels.TryGetValue(key, out var label) ? label : null))
            .ToList();
    }

    public async Task<IReadOnlyList<DutyFolderOption>> GetFoldersAsync()
    {
        using var db = _dbFactory.CreateDbContext();
        var nodes = await db.OrgNodes.AsNoTracking()
            .OrderBy(n => n.Depth).ThenBy(n => n.SortOrder).ThenBy(n => n.Name)
            .ToListAsync();
        var intakeByRoot = await IntakeByRootAsync(db);
        var byFolder = await PeopleByFolderAsync(db);

        return nodes
            .Select(n =>
            {
                var isRoot = intakeByRoot.TryGetValue(n.Id, out var rootOf);
                var people = byFolder.TryGetValue(n.Id, out var ids) ? ids.Order().ToList() : new List<int>();
                return new DutyFolderOption(n.Id, n.ParentId, n.Name, n.IntakeId ?? (isRoot ? rootOf : null), isRoot, people);
            })
            .ToList();
    }

    public async Task<IReadOnlyList<DutyPersonOption>> GetPeopleToAddAsync(int? intakeId, IReadOnlyCollection<int> alreadyInRoster)
    {
        using var db = _dbFactory.CreateDbContext();
        var people = await db.Recipients.AsNoTracking()
            .Where(r => intakeId == null || r.IntakeId == null || r.IntakeId == intakeId)
            .ToListAsync();

        return RosterOrdering.Apply(people.Where(r => !alreadyInRoster.Contains(r.Id)))
            .OrderBy(r => r.IntakeId is null ? 1 : 0)
            .Select(r => new DutyPersonOption(r.Id, DutyRosterText.DisplayName(r.LastName, r.FirstName, r.MiddleName),
                r.IntakeId is null ? "постійний склад" : r.Rank ?? string.Empty))
            .ToList();
    }

    public async Task<DutyRosterData> CreateAsync(DutyRosterCreateRequest request)
    {
        if (request.DateTo < request.DateFrom)
            throw new ArgumentException("Дата «по» раніша за дату «з».");
        if (request.DateTo.DayNumber - request.DateFrom.DayNumber + 1 > MaxDays)
            throw new ArgumentException($"Період довший за {MaxDays} днів - розбийте його на кілька графіків.");

        var roster = new DutyRosterData
        {
            DateFrom = request.DateFrom,
            DateTo = request.DateTo,
            UseEventExemptions = request.UseEventExemptions,
            WeekendsGreen = request.WeekendsGreen,
            Seed = Random.Shared.Next(1, 1_000_000)
        };

        var prefs = await GetUserPrefsAsync();
        roster.Heading = prefs.Heading;
        roster.Footer = prefs.Footer;
        roster.PeoplePerDay = prefs.PeoplePerDay;
        roster.DutyMarkOnly = prefs.DutyMarkOnly;
        roster.CarryOverBalance = prefs.CarryOverBalance;
        roster.Footer.FitShifts(roster.PeoplePerDay);

        using (var db = _dbFactory.CreateDbContext())
        {
            var folders = await CanonicalFoldersAsync(db, request.FolderIds);
            roster.FolderIds.AddRange(folders.Select(f => f.Id));

            var intakeByRoot = await IntakeByRootAsync(db);
            var intakes = folders
                .Select(f => f.IntakeId ?? (intakeByRoot.TryGetValue(f.Id, out var rootOf) ? rootOf : null))
                .OfType<int>()
                .Distinct()
                .ToList();
            roster.IntakeId = intakes.Count == 1 ? intakes[0] : null;

            var folderKey = FormatIds(roster.FolderIds);
            roster.FoldersLabel = await FoldersLabelAsync(db, folderKey);

            var byFolder = await PeopleByFolderAsync(db);
            var ids = folders.Where(f => byFolder.ContainsKey(f.Id)).SelectMany(f => byFolder[f.Id]).Distinct().ToList();
            var people = RosterOrdering.Apply(await db.Recipients.AsNoTracking().Where(r => ids.Contains(r.Id)).ToListAsync()).ToList();

            var template = await TemplateRosterAsync(db, folderKey);
            if (template is null)
            {
                foreach (var person in people) roster.Members.Add(Member(person));
            }
            else
            {
                await ApplyTemplateAsync(db, roster, template, people);
            }
        }

        DutyRosterPlanner.MoveExemptToBottom(roster);
        await ApplyEventExemptionsAsync(roster);
        var carryOver = await GetCarryOverAsync(roster);
        DutyRosterPlanner.Replan(roster, carryOver?.State);

        await SaveAsync(roster);

        using (var db = _dbFactory.CreateDbContext())
        {
            _auditLog.LogCreate(db, "DutyRoster", roster.Id, DutyRosterText.PeriodLabel(roster.DateFrom, roster.DateTo),
                $"Графік чергувань {DutyRosterText.PeriodLabel(roster.DateFrom, roster.DateTo)}, {roster.Members.Count} осіб");
            await db.SaveChangesAsync();
        }

        return roster;
    }

    private static DutyRosterMemberData Member(Recipient person) => new()
    {
        RecipientId = person.Id,
        DisplayName = DutyRosterText.DisplayName(person.LastName, person.FirstName, person.MiddleName)
    };

    /// <summary>Останній графік того самого складу (тих самих папок) - з нього береться склад нового.</summary>
    private static async Task<DutyRoster?> TemplateRosterAsync(AppDbContext db, string? folderKey)
        => await db.DutyRosters.AsNoTracking()
            .Include(r => r.Members)
            .Where(r => r.OrgNodeIds == folderKey)
            .OrderByDescending(r => r.DateFrom).ThenByDescending(r => r.Id)
            .FirstOrDefaultAsync();

    /// <summary>
    /// Новий графік повторює склад попереднього: порядок рядків, звільнених на весь період,
    /// вписаних і доданих вручну і тих, кого прибрали. Хто вибув з папок графіка - випадає,
    /// новачки папок - у кінець списку.
    /// </summary>
    private static async Task ApplyTemplateAsync(AppDbContext db, DutyRosterData roster, DutyRoster template, IReadOnlyList<Recipient> people)
    {
        foreach (var id in ParseIds(template.ExcludedRecipients)) roster.ExcludedRecipientIds.Add(id);

        var inFolders = people.ToDictionary(p => p.Id);
        var byHandIds = template.Members
            .Where(m => m.AddedByHand && m.RecipientId is int id && !inFolders.ContainsKey(id))
            .Select(m => m.RecipientId!.Value)
            .ToList();
        var byHand = await db.Recipients.AsNoTracking()
            .Where(r => byHandIds.Contains(r.Id))
            .ToDictionaryAsync(r => r.Id);

        var taken = new HashSet<int>();
        foreach (var member in template.Members.OrderBy(m => m.SortOrder).ThenBy(m => m.Id))
        {
            if (member.RecipientId is not int id)
            {
                roster.Members.Add(new DutyRosterMemberData { DisplayName = member.DisplayName, ExemptWholePeriod = member.ExemptWholePeriod });
                continue;
            }

            var person = inFolders.GetValueOrDefault(id) ?? byHand.GetValueOrDefault(id);
            if (person is null || !taken.Add(id)) continue;

            var data = Member(person);
            data.ExemptWholePeriod = member.ExemptWholePeriod;
            data.AddedByHand = !inFolders.ContainsKey(id);
            roster.Members.Add(data);
        }

        foreach (var person in people.Where(p => !taken.Contains(p.Id) && !roster.ExcludedRecipientIds.Contains(p.Id)))
            roster.Members.Add(Member(person));
    }

    public async Task<DutyRosterData?> LoadAsync(int id)
    {
        using var db = _dbFactory.CreateDbContext();
        var entity = await db.DutyRosters.AsNoTracking()
            .Include(r => r.Members).ThenInclude(m => m.Cells)
            .FirstOrDefaultAsync(r => r.Id == id);
        if (entity is null) return null;

        var roster = ToData(entity);
        roster.FoldersLabel = await FoldersLabelAsync(db, entity.OrgNodeIds);
        return roster;
    }

    public async Task SaveAsync(DutyRosterData roster)
    {
        using var db = _dbFactory.CreateDbContext();

        DutyRoster entity;
        if (roster.Id == 0)
        {
            entity = new DutyRoster { CreatedAt = DateTime.Now, CreatedBy = _currentUser.CurrentUserFullName };
            db.DutyRosters.Add(entity);
        }
        else
        {
            entity = await db.DutyRosters
                .Include(r => r.Members).ThenInclude(m => m.Cells)
                .FirstOrDefaultAsync(r => r.Id == roster.Id)
                ?? throw new InvalidOperationException("Графік не знайдено - можливо, його вже видалено.");

            db.DutyRosterCells.RemoveRange(entity.Members.SelectMany(m => m.Cells));
            db.DutyRosterMembers.RemoveRange(entity.Members);
            entity.UpdatedAt = DateTime.Now;
        }

        entity.DateFrom = roster.DateFrom;
        entity.DateTo = roster.DateTo;
        entity.IntakeId = roster.IntakeId;
        entity.OrgNodeIds = FormatIds(roster.FolderIds);
        entity.PeoplePerDay = roster.PeoplePerDay;
        entity.Seed = roster.Seed;
        entity.Heading = roster.Heading;
        entity.FooterJson = JsonSerializer.Serialize(roster.Footer);
        entity.ExemptDays = FormatDays(roster.ExemptDays.Where(InPeriod));
        entity.ExcludedRecipients = FormatIds(roster.ExcludedRecipientIds);
        entity.UseEventExemptions = roster.UseEventExemptions;
        entity.CarryOverBalance = roster.CarryOverBalance;
        entity.DutyMarkOnly = roster.DutyMarkOnly;
        entity.WeekendsGreen = roster.WeekendsGreen;
        entity.ShiftRotation = roster.ShiftRotation;

        for (var i = 0; i < roster.Members.Count; i++)
        {
            var source = roster.Members[i];
            var member = new DutyRosterMember
            {
                Roster = entity,
                RecipientId = source.RecipientId,
                DisplayName = source.DisplayName,
                SortOrder = i,
                ExemptWholePeriod = source.ExemptWholePeriod,
                AddedByHand = source.AddedByHand
            };

            foreach (var (day, origin) in source.Duties.Where(d => InPeriod(d.Key)))
            {
                member.Cells.Add(new DutyRosterCell
                {
                    Date = day, Kind = DutyCellKind.Duty, Source = origin,
                    Shift = source.Shifts.TryGetValue(day, out var shift) ? shift : null
                });
            }
            foreach (var (day, origin) in source.Exemptions.Where(d => InPeriod(d.Key)))
                member.Cells.Add(new DutyRosterCell { Date = day, Kind = DutyCellKind.Exempt, Source = origin });
            foreach (var day in source.Blocked.Where(InPeriod))
                member.Cells.Add(new DutyRosterCell { Date = day, Kind = DutyCellKind.Blocked, Source = DutyCellSource.Manual });
            foreach (var day in source.Canteen.Where(InPeriod))
                member.Cells.Add(new DutyRosterCell { Date = day, Kind = DutyCellKind.Canteen, Source = DutyCellSource.Manual });

            db.DutyRosterMembers.Add(member);
        }

        await db.SaveChangesAsync();
        roster.Id = entity.Id;

        bool InPeriod(DateOnly day) => day >= roster.DateFrom && day <= roster.DateTo;
    }

    public async Task DeleteAsync(int id)
    {
        using var db = _dbFactory.CreateDbContext();
        var entity = await db.DutyRosters.FirstOrDefaultAsync(r => r.Id == id);
        if (entity is null) return;

        entity.DeletedAt = DateTime.Now;
        entity.DeletedBy = _currentUser.CurrentUserFullName;
        _auditLog.LogDelete(db, "DutyRoster", id, DutyRosterText.PeriodLabel(entity.DateFrom, entity.DateTo),
            $"Графік чергувань {DutyRosterText.PeriodLabel(entity.DateFrom, entity.DateTo)}");
        await db.SaveChangesAsync();
    }

    public async Task<IReadOnlyDictionary<int, IReadOnlySet<DateOnly>>> GetEventExemptionsAsync(
        IReadOnlyCollection<int> recipientIds, DateOnly from, DateOnly to)
    {
        if (recipientIds.Count == 0) return new Dictionary<int, IReadOnlySet<DateOnly>>();

        using var db = _dbFactory.CreateDbContext();
        var ids = recipientIds.ToList();
        var events = await db.StaffEvents.AsNoTracking()
            .Where(e => ids.Contains(e.RecipientId) && e.DateStart <= to && e.DateEnd >= from)
            .ToListAsync();

        return events
            .GroupBy(e => e.RecipientId)
            .ToDictionary(
                g => g.Key,
                g => (IReadOnlySet<DateOnly>)g
                    .SelectMany(e => DutyRosterEngine.Days(e.DateStart < from ? from : e.DateStart, e.DateEnd > to ? to : e.DateEnd))
                    .ToHashSet());
    }

    public async Task ApplyEventExemptionsAsync(DutyRosterData roster)
    {
        foreach (var member in roster.Members)
        {
            foreach (var day in member.Exemptions.Where(e => e.Value == DutyCellSource.Event).Select(e => e.Key).ToList())
                member.Exemptions.Remove(day);
        }

        if (!roster.UseEventExemptions) return;

        var ids = roster.Members.Where(m => m.RecipientId is not null).Select(m => m.RecipientId!.Value).ToList();
        var byPerson = await GetEventExemptionsAsync(ids, roster.DateFrom, roster.DateTo);

        foreach (var member in roster.Members)
        {
            if (member.RecipientId is not int id || !byPerson.TryGetValue(id, out var days)) continue;
            foreach (var day in days)
                member.Exemptions.TryAdd(day, DutyCellSource.Event);
        }
    }

    public async Task<DutyCarryOver?> GetCarryOverAsync(DutyRosterData roster)
    {
        if (!roster.CarryOverBalance) return null;

        using var db = _dbFactory.CreateDbContext();
        var folderKey = FormatIds(roster.FolderIds);
        var previous = await db.DutyRosters.AsNoTracking()
            .Include(r => r.Members).ThenInclude(m => m.Cells)
            .Where(r => r.Id != roster.Id
                        && r.OrgNodeIds == folderKey
                        && r.DateFrom < roster.DateFrom)
            .OrderByDescending(r => r.DateFrom).ThenByDescending(r => r.Id)
            .Take(CarryOverDepth)
            .ToListAsync();
        if (previous.Count == 0) return null;

        var inOrder = previous.Select(ToData).OrderBy(r => r.DateFrom).ToList();
        var state = DutyRosterPlanner.Replay(inOrder, roster.DateFrom);
        if (state is null) return null;

        var last = inOrder[^1];
        return new DutyCarryOver(state, DutyRosterText.PeriodLabel(last.DateFrom, last.DateTo));
    }

    /// <summary>
    /// Налаштування курсового: його збережені, а якщо їх ще немає - з останнього графіка будь-кого
    /// (так новий курсовий стартує з того, що вже є), або типові.
    /// </summary>
    public async Task<DutyRosterPrefs> GetUserPrefsAsync()
    {
        var json = (await _userSettings.GetForCurrentUserAsync()).DutyRosterPrefsJson;
        if (!string.IsNullOrWhiteSpace(json))
        {
            try
            {
                if (JsonSerializer.Deserialize<DutyRosterPrefs>(json) is { } saved)
                {
                    if (string.IsNullOrWhiteSpace(saved.Heading)) saved.Heading = DutyRosterText.DefaultHeading;
                    saved.Footer ??= new DutyRosterFooter();
                    saved.PeoplePerDay = Math.Clamp(saved.PeoplePerDay, 1, 6);
                    return saved;
                }
            }
            catch (JsonException)
            {
            }
        }

        using var db = _dbFactory.CreateDbContext();
        var last = await db.DutyRosters.AsNoTracking()
            .OrderByDescending(r => r.CreatedAt).ThenByDescending(r => r.Id)
            .FirstOrDefaultAsync();
        if (last is null) return new DutyRosterPrefs();

        return new DutyRosterPrefs
        {
            WeekendsGreen = last.WeekendsGreen,
            DutyMarkOnly = last.DutyMarkOnly,
            UseEventExemptions = last.UseEventExemptions,
            CarryOverBalance = last.CarryOverBalance,
            PeoplePerDay = last.PeoplePerDay,
            Heading = string.IsNullOrWhiteSpace(last.Heading) ? DutyRosterText.DefaultHeading : last.Heading,
            Footer = ParseFooter(last.FooterJson)
        };
    }

    public Task RememberPrefsAsync(DutyRosterData roster)
    {
        var json = JsonSerializer.Serialize(new DutyRosterPrefs
        {
            WeekendsGreen = roster.WeekendsGreen,
            DutyMarkOnly = roster.DutyMarkOnly,
            UseEventExemptions = roster.UseEventExemptions,
            CarryOverBalance = roster.CarryOverBalance,
            PeoplePerDay = roster.PeoplePerDay,
            Heading = roster.Heading,
            Footer = roster.Footer
        });
        return _userSettings.UpdateAsync(s => s.DutyRosterPrefsJson = json);
    }

    public void LogExport(DutyRosterData roster, string filePath)
    {
        using var db = _dbFactory.CreateDbContext();
        _auditLog.LogExport(db, "DutyRoster", 1,
            $"Графік чергувань {DutyRosterText.PeriodLabel(roster.DateFrom, roster.DateTo)} → {System.IO.Path.GetFileName(filePath)}");
        db.SaveChanges();
    }

    private static DutyRosterData ToData(DutyRoster entity)
    {
        var roster = new DutyRosterData
        {
            Id = entity.Id,
            DateFrom = entity.DateFrom,
            DateTo = entity.DateTo,
            IntakeId = entity.IntakeId,
            PeoplePerDay = entity.PeoplePerDay,
            Seed = entity.Seed,
            Heading = entity.Heading,
            Footer = ParseFooter(entity.FooterJson),
            UseEventExemptions = entity.UseEventExemptions,
            CarryOverBalance = entity.CarryOverBalance,
            DutyMarkOnly = entity.DutyMarkOnly,
            WeekendsGreen = entity.WeekendsGreen,
            ShiftRotation = entity.ShiftRotation
        };

        foreach (var day in ParseDays(entity.ExemptDays)) roster.ExemptDays.Add(day);
        foreach (var id in ParseIds(entity.ExcludedRecipients)) roster.ExcludedRecipientIds.Add(id);
        roster.FolderIds.AddRange(ParseIds(entity.OrgNodeIds));

        foreach (var member in entity.Members.OrderBy(m => m.SortOrder).ThenBy(m => m.Id))
        {
            var data = new DutyRosterMemberData
            {
                RecipientId = member.RecipientId,
                DisplayName = member.DisplayName,
                ExemptWholePeriod = member.ExemptWholePeriod,
                AddedByHand = member.AddedByHand
            };
            foreach (var cell in member.Cells)
            {
                switch (cell.Kind)
                {
                    case DutyCellKind.Duty:
                        data.Duties[cell.Date] = cell.Source;
                        if (cell.Shift is int shift) data.Shifts[cell.Date] = shift;
                        break;
                    case DutyCellKind.Exempt: data.Exemptions[cell.Date] = cell.Source; break;
                    case DutyCellKind.Blocked: data.Blocked.Add(cell.Date); break;
                    case DutyCellKind.Canteen: data.Canteen.Add(cell.Date); break;
                }
            }
            roster.Members.Add(data);
        }

        return roster;
    }

    private static DutyRosterFooter ParseFooter(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new DutyRosterFooter();
        try
        {
            return JsonSerializer.Deserialize<DutyRosterFooter>(json) ?? new DutyRosterFooter();
        }
        catch (JsonException)
        {
            return new DutyRosterFooter();
        }
    }

    internal static string? FormatIds(IEnumerable<int> ids)
    {
        var text = string.Join(",", ids.Distinct().OrderBy(i => i).Select(i => i.ToString(CultureInfo.InvariantCulture)));
        return text.Length == 0 ? null : text;
    }

    internal static IEnumerable<int> ParseIds(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) yield break;
        foreach (var part in text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (int.TryParse(part, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id))
                yield return id;
        }
    }

    internal static string? FormatDays(IEnumerable<DateOnly> days)
    {
        var text = string.Join(",", days.OrderBy(d => d).Select(d => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)));
        return text.Length == 0 ? null : text;
    }

    internal static IEnumerable<DateOnly> ParseDays(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) yield break;
        foreach (var part in text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (DateOnly.TryParseExact(part, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day))
                yield return day;
        }
    }

    /// <summary>Id папки й усіх її батьків - з Path («/1/5/9/»).</summary>
    private static IEnumerable<int> PathIds(string path) => ParseIds(path.Replace('/', ','));

    /// <summary>Корінь набору → набір.</summary>
    private static async Task<Dictionary<int, int?>> IntakeByRootAsync(AppDbContext db)
        => (await db.Intakes.AsNoTracking().Select(i => new { i.Id, i.RootOrgNodeId }).ToListAsync())
            .GroupBy(i => i.RootOrgNodeId)
            .ToDictionary(g => g.Key, g => (int?)g.First().Id);

    /// <summary>
    /// Хто піде в графік з кожної папки: люди її і вкладених папок, а коренева папка набору - ще й люди
    /// набору без папки (так графік набору, як і раніше, бере весь набір).
    /// </summary>
    private static async Task<Dictionary<int, HashSet<int>>> PeopleByFolderAsync(AppDbContext db)
    {
        var people = await db.Recipients.AsNoTracking()
            .Select(r => new { r.Id, r.IntakeId, Path = r.OrgNode != null ? r.OrgNode.Path : null })
            .ToListAsync();
        var rootByIntake = await db.Intakes.AsNoTracking().ToDictionaryAsync(i => i.Id, i => i.RootOrgNodeId);

        var byFolder = new Dictionary<int, HashSet<int>>();
        foreach (var person in people)
        {
            var folders = person.Path is null ? Enumerable.Empty<int>() : PathIds(person.Path);
            if (person.IntakeId is int intakeId && rootByIntake.TryGetValue(intakeId, out var rootId))
                folders = folders.Append(rootId);

            foreach (var folderId in folders)
            {
                if (!byFolder.TryGetValue(folderId, out var ids)) byFolder[folderId] = ids = new HashSet<int>();
                ids.Add(person.Id);
            }
        }
        return byFolder;
    }

    /// <summary>
    /// Вибрані папки без видалених і без вкладених у вже вибрані - той самий склад дає той самий ключ графіка,
    /// хоч би як його відмітили.
    /// </summary>
    private static async Task<List<OrgNode>> CanonicalFoldersAsync(AppDbContext db, IReadOnlyCollection<int> folderIds)
    {
        var ids = folderIds.Distinct().ToList();
        if (ids.Count == 0) return new List<OrgNode>();

        var nodes = await db.OrgNodes.AsNoTracking().Where(n => ids.Contains(n.Id)).ToListAsync();
        var picked = nodes.Select(n => n.Id).ToHashSet();
        return nodes
            .Where(n => !PathIds(n.Path).Any(a => a != n.Id && picked.Contains(a)))
            .OrderBy(n => n.Id)
            .ToList();
    }

    private static async Task<string?> FoldersLabelAsync(AppDbContext db, string? folderKey)
        => folderKey is null ? null : (await FoldersLabelsAsync(db, new[] { folderKey })).GetValueOrDefault(folderKey);

    /// <summary>
    /// Підпис складу за ключем папок: корінь набору - «Набір №29», папка в наборі - «Набір №29 / 1 група»,
    /// решта - назва папки. Видалені папки й набори теж підписуються - старий графік лишається впізнаваним.
    /// </summary>
    private static async Task<Dictionary<string, string>> FoldersLabelsAsync(AppDbContext db, IEnumerable<string?> folderKeys)
    {
        var keys = folderKeys.OfType<string>().Distinct().ToDictionary(k => k, k => ParseIds(k).ToList());
        var ids = keys.Values.SelectMany(v => v).Distinct().ToList();
        if (ids.Count == 0) return new Dictionary<string, string>();

        var nodes = await db.OrgNodes.IgnoreQueryFilters().AsNoTracking()
            .Where(n => ids.Contains(n.Id))
            .ToDictionaryAsync(n => n.Id);
        var intakeIds = nodes.Values.Select(n => n.IntakeId).OfType<int>().Distinct().ToList();
        var intakes = await db.Intakes.IgnoreQueryFilters().AsNoTracking()
            .Where(i => intakeIds.Contains(i.Id) || ids.Contains(i.RootOrgNodeId))
            .Select(i => new { i.Id, i.RootOrgNodeId, i.Number, i.DisplayNumber })
            .ToListAsync();
        var labelById = intakes.ToDictionary(i => i.Id, i => IntakeLabel.Of(i.Number, i.DisplayNumber));
        var labelByRoot = intakes.GroupBy(i => i.RootOrgNodeId).ToDictionary(g => g.Key, g => labelById[g.First().Id]);

        string Name(OrgNode node)
        {
            if (labelByRoot.TryGetValue(node.Id, out var root)) return root;
            return node.IntakeId is int intakeId && labelById.TryGetValue(intakeId, out var intake)
                ? $"{intake} / {node.Name}"
                : node.Name;
        }

        return keys
            .Select(k => (k.Key, Label: string.Join(", ", k.Value.Where(nodes.ContainsKey).Select(id => Name(nodes[id])))))
            .Where(k => k.Label.Length > 0)
            .ToDictionary(k => k.Key, k => k.Label);
    }
}
