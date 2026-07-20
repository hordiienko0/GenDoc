using GenDoc.Data;
using GenDoc.Models;
using Microsoft.EntityFrameworkCore;

namespace GenDoc.Services.Recipients
{
    public class RecipientService : IRecipientService
    {
        private readonly IDbContextFactory<AppDbContext> _dbFactory;
        private readonly IAuditLogService _auditLogService;
        private readonly ICurrentUserContext _currentUserContext;

        public RecipientService(
            IDbContextFactory<AppDbContext> dbFactory,
            IAuditLogService auditLogService,
            ICurrentUserContext currentUserContext)
        {
            _dbFactory = dbFactory;
            _auditLogService = auditLogService;
            _currentUserContext = currentUserContext;
        }

        public List<RecipientListItem> Search(string? searchText, RecipientSortColumn sortColumn = RecipientSortColumn.FullName, bool sortDescending = false)
        {
            using var db = _dbFactory.CreateDbContext();

            // Матеріалізуємо сутності одразу (ToList) — подальший пошук за словами
            // і форматування кімнати виконуються в пам'яті на C#, EF Core/SQLite
            // не повинен транслювати динамічне розбиття рядка на слова в SQL.
            var entities = db.Recipients
                .Include(r => r.Unit)
                .Include(r => r.Room)
                .ToList();

            var words = (searchText ?? string.Empty).Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (words.Length > 0)
            {
                entities = entities.Where(r => MatchesAllWords(r, words)).ToList();
            }

            var items = entities.Select(r => new RecipientListItem(
                r.Id,
                r.LastName + " " + r.FirstName + (r.MiddleName != null ? " " + r.MiddleName : ""),
                r.Rank,
                r.Position,
                r.Unit != null ? r.Unit.Name : "—",
                FormatRoom(r.Room)));

            items = sortColumn switch
            {
                RecipientSortColumn.Rank => sortDescending ? items.OrderByDescending(i => i.Rank) : items.OrderBy(i => i.Rank),
                RecipientSortColumn.Position => sortDescending ? items.OrderByDescending(i => i.Position) : items.OrderBy(i => i.Position),
                RecipientSortColumn.UnitName => sortDescending ? items.OrderByDescending(i => i.UnitName) : items.OrderBy(i => i.UnitName),
                RecipientSortColumn.Room => sortDescending ? items.OrderByDescending(i => i.RoomDisplay) : items.OrderBy(i => i.RoomDisplay),
                _ => sortDescending ? items.OrderByDescending(i => i.FullName) : items.OrderBy(i => i.FullName),
            };

            return items.ToList();
        }

        public RoomOccupancyInfo? GetRoomOccupancy(string? building, string? number, int excludeRecipientId)
        {
            if (string.IsNullOrWhiteSpace(building) || string.IsNullOrWhiteSpace(number)) return null;

            using var db = _dbFactory.CreateDbContext();
            var b = building.Trim();
            var n = number.Trim();

            var room = db.Rooms.FirstOrDefault(r => r.Building == b && r.Number == n);
            if (room is null) return null;

            var occupantCount = db.Recipients.Count(r => r.RoomId == room.Id && r.Id != excludeRecipientId);
            return new RoomOccupancyInfo(occupantCount, room.Capacity);
        }

        private static bool MatchesAllWords(Recipient r, string[] words)
        {
            var haystack = string.Join(' ', new[]
            {
                r.LastName, r.FirstName, r.MiddleName, r.Rank, r.Position, r.ServiceNumber, r.Unit?.Name
            }).ToLowerInvariant();

            return words.All(w => haystack.Contains(w.ToLowerInvariant()));
        }

        private static string FormatRoom(Room? room)
        {
            if (room is null || string.IsNullOrWhiteSpace(room.Number)) return "—";

            var building = room.Building?.Trim();
            if (string.IsNullOrEmpty(building)) return room.Number;

            var buildingLetter = building[^1..].ToUpperInvariant();
            return $"{room.Number} ({buildingLetter})";
        }

        public RecipientEditModel? GetForEdit(int id)
        {
            using var db = _dbFactory.CreateDbContext();
            var r = db.Recipients.Include(x => x.Unit).Include(x => x.Room).FirstOrDefault(x => x.Id == id);
            if (r is null) return null;

            return new RecipientEditModel
            {
                Id = r.Id,
                LastName = r.LastName,
                FirstName = r.FirstName,
                MiddleName = r.MiddleName,
                Rank = r.Rank,
                Position = r.Position,
                ServiceNumber = r.ServiceNumber,
                DateOfBirth = r.DateOfBirth,
                UnitName = r.Unit?.Name,
                RoomBuilding = r.Room?.Building,
                RoomNumber = r.Room?.Number
            };
        }

        public List<string> GetUnitNames()
        {
            using var db = _dbFactory.CreateDbContext();
            return db.Units.OrderBy(u => u.Name).Select(u => u.Name).ToList();
        }

        public List<string> GetRoomBuildings()
        {
            using var db = _dbFactory.CreateDbContext();
            return db.Rooms.Select(r => r.Building).Distinct().OrderBy(b => b).ToList();
        }

        public List<string> GetRanks()
        {
            using var db = _dbFactory.CreateDbContext();
            return db.Recipients.Select(r => r.Rank).Distinct().OrderBy(r => r).ToList();
        }

        public string? FindDuplicateServiceNumberOwner(string? serviceNumber, int excludeRecipientId)
        {
            if (string.IsNullOrWhiteSpace(serviceNumber)) return null;

            using var db = _dbFactory.CreateDbContext();
            var number = serviceNumber.Trim();
            var duplicate = db.Recipients.FirstOrDefault(r => r.ServiceNumber == number && r.Id != excludeRecipientId);
            return duplicate is null ? null : $"{duplicate.LastName} {duplicate.FirstName}".Trim();
        }

        public void Save(RecipientEditModel model, out string? errorMessage)
        {
            errorMessage = null;
            using var db = _dbFactory.CreateDbContext();

            var serviceNumber = model.ServiceNumber.Trim();
            var duplicate = db.Recipients.FirstOrDefault(r => r.ServiceNumber == serviceNumber && r.Id != model.Id);
            if (duplicate is not null)
            {
                var duplicateName = $"{duplicate.LastName} {duplicate.FirstName}".Trim();
                errorMessage = $"Особовий номер {serviceNumber} вже використовується: {duplicateName}";
                return;
            }

            var isNew = model.Id == 0;
            var recipient = isNew ? new Recipient() : db.Recipients.First(r => r.Id == model.Id);
            var oldSnapshot = isNew ? null : BuildSnapshot(recipient);

            recipient.LastName = model.LastName.Trim();
            recipient.FirstName = model.FirstName.Trim();
            recipient.MiddleName = string.IsNullOrWhiteSpace(model.MiddleName) ? null : model.MiddleName.Trim();
            recipient.Rank = model.Rank.Trim();
            recipient.Position = model.Position.Trim();
            recipient.ServiceNumber = model.ServiceNumber.Trim();
            recipient.DateOfBirth = model.DateOfBirth;
            recipient.UnitId = ResolveUnitId(db, model.UnitName);
            recipient.RoomId = ResolveRoomId(db, model.RoomBuilding, model.RoomNumber);

            if (isNew) db.Recipients.Add(recipient);
            db.SaveChanges();

            var newSnapshot = BuildSnapshot(recipient);
            if (isNew)
                _auditLogService.LogCreate(db, "Recipient", recipient.Id, newSnapshot);
            else
                _auditLogService.LogUpdate(db, "Recipient", recipient.Id, oldSnapshot, newSnapshot);

            db.SaveChanges();
        }

        public void Delete(int id)
        {
            using var db = _dbFactory.CreateDbContext();
            var recipient = db.Recipients.First(r => r.Id == id);
            var snapshot = BuildSnapshot(recipient);

            recipient.DeletedAt = DateTime.Now;
            recipient.DeletedBy = _currentUserContext.CurrentUserFullName;

            _auditLogService.LogDelete(db, "Recipient", recipient.Id, snapshot);
            db.SaveChanges();
        }

        private static int? ResolveUnitId(AppDbContext db, string? unitName)
        {
            if (string.IsNullOrWhiteSpace(unitName)) return null;
            var name = unitName.Trim();

            var existing = db.Units.FirstOrDefault(u => u.Name == name);
            if (existing is not null) return existing.Id;

            var unit = new Unit { Name = name };
            db.Units.Add(unit);
            db.SaveChanges();
            return unit.Id;
        }

        private static int? ResolveRoomId(AppDbContext db, string? building, string? number)
        {
            if (string.IsNullOrWhiteSpace(building) || string.IsNullOrWhiteSpace(number)) return null;
            var b = building.Trim();
            var n = number.Trim();

            var existing = db.Rooms.FirstOrDefault(r => r.Building == b && r.Number == n);
            if (existing is not null) return existing.Id;

            var room = new Room { Building = b, Number = n, Capacity = 1 };
            db.Rooms.Add(room);
            db.SaveChanges();
            return room.Id;
        }

        private static string BuildSnapshot(Recipient r)
            => $"{r.LastName} {r.FirstName} {r.MiddleName}, {r.Rank}, {r.Position}, №{r.ServiceNumber}".Trim();
    }
}
