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

        public List<RecipientListItem> Search(string? searchText)
        {
            using var db = _dbFactory.CreateDbContext();
            var query = db.Recipients.Include(r => r.Unit).Include(r => r.Room).AsQueryable();

            if (!string.IsNullOrWhiteSpace(searchText))
            {
                var term = searchText.Trim();
                query = query.Where(r =>
                    r.LastName.Contains(term) ||
                    r.FirstName.Contains(term) ||
                    (r.MiddleName != null && r.MiddleName.Contains(term)) ||
                    r.ServiceNumber.Contains(term) ||
                    r.Rank.Contains(term) ||
                    r.Position.Contains(term));
            }

            var entities = query.OrderBy(r => r.LastName).ThenBy(r => r.FirstName).ToList();

            return entities
                .Select(r => new RecipientListItem(
                    r.Id,
                    r.LastName + " " + r.FirstName + (r.MiddleName != null ? " " + r.MiddleName : ""),
                    r.Rank,
                    r.Position,
                    r.Unit != null ? r.Unit.Name : "—",
                    FormatRoom(r.Room)))
                .ToList();
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

        public void Save(RecipientEditModel model)
        {
            using var db = _dbFactory.CreateDbContext();
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
