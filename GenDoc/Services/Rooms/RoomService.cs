using GenDoc.Data;
using GenDoc.Models;
using Microsoft.EntityFrameworkCore;

namespace GenDoc.Services.Rooms
{
    public class RoomService : IRoomService
    {
        private readonly IDbContextFactory<AppDbContext> _dbFactory;
        private readonly IAuditLogService _auditLogService;
        private readonly ICurrentUserContext _currentUserContext;

        public RoomService(
            IDbContextFactory<AppDbContext> dbFactory,
            IAuditLogService auditLogService,
            ICurrentUserContext currentUserContext)
        {
            _dbFactory = dbFactory;
            _auditLogService = auditLogService;
            _currentUserContext = currentUserContext;
        }

        public List<RoomOverview> GetAll()
        {
            using var db = _dbFactory.CreateDbContext();

            var rooms = db.Rooms
                .Include(r => r.Occupants)
                .OrderBy(r => r.Building).ThenBy(r => r.Number)
                .ToList();

            return rooms.Select(r => new RoomOverview(
                r.Id, r.Building, r.Number, r.Capacity, r.Note,
                r.Occupants
                    .OrderBy(o => o.LastName).ThenBy(o => o.FirstName)
                    .Select(o => new RoomOccupantSummary(o.Id, NameFormatter.ShortName(o.LastName, o.FirstName, o.MiddleName)))
                    .ToList())).ToList();
        }

        public (bool Success, string? ErrorMessage) Create(string building, string number, int capacity, string? note)
        {
            using var db = _dbFactory.CreateDbContext();
            var b = building.Trim();
            var n = number.Trim();

            if (db.Rooms.Any(r => r.Building == b && r.Number == n))
                return (false, $"Кімната {b} №{n} вже існує.");

            var room = new Room
            {
                Building = b,
                Number = n,
                Capacity = capacity,
                Note = string.IsNullOrWhiteSpace(note) ? null : note.Trim(),
                CreatedAt = DateTime.Now,
                CreatedBy = _currentUserContext.CurrentUserFullName
            };

            db.Rooms.Add(room);
            db.SaveChanges();

            _auditLogService.LogCreate(db, "Room", room.Id, BuildSnapshot(room));
            db.SaveChanges();

            return (true, null);
        }

        public (bool Success, string? ErrorMessage) Update(int id, string building, string number, int capacity, string? note)
        {
            using var db = _dbFactory.CreateDbContext();
            var room = db.Rooms.First(r => r.Id == id);

            var b = building.Trim();
            var n = number.Trim();

            if (db.Rooms.Any(r => r.Id != id && r.Building == b && r.Number == n))
                return (false, $"Кімната {b} №{n} вже існує.");

            var oldSnapshot = BuildSnapshot(room);

            room.Building = b;
            room.Number = n;
            room.Capacity = capacity;
            room.Note = string.IsNullOrWhiteSpace(note) ? null : note.Trim();

            db.SaveChanges();

            var newSnapshot = BuildSnapshot(room);
            if (oldSnapshot != newSnapshot)
            {
                _auditLogService.LogUpdate(db, "Room", room.Id, oldSnapshot, newSnapshot);
                db.SaveChanges();
            }

            return (true, null);
        }

        public void Delete(int id)
        {
            using var db = _dbFactory.CreateDbContext();
            using var transaction = db.Database.BeginTransaction();

            var room = db.Rooms.Include(r => r.Occupants).First(r => r.Id == id);
            var snapshot = BuildSnapshot(room);
            var roomDisplay = $"{room.Building} №{room.Number}";

            foreach (var occupant in room.Occupants.ToList())
            {
                occupant.RoomId = null;
                _auditLogService.LogUpdate(db, "Recipient", occupant.Id,
                    $"Кімната: {roomDisplay}", "Кімната: -", "Знято з розміщення (видалено кімнату)");
            }

            room.DeletedAt = DateTime.Now;
            room.DeletedBy = _currentUserContext.CurrentUserFullName;

            _auditLogService.LogDelete(db, "Room", room.Id, snapshot);

            db.SaveChanges();
            transaction.Commit();
        }

        public List<RoomOccupantDto> GetOccupants(int roomId)
        {
            using var db = _dbFactory.CreateDbContext();

            var rows = db.Recipients
                .Where(r => r.RoomId == roomId)
                .Include(r => r.Unit)
                .OrderBy(r => r.LastName).ThenBy(r => r.FirstName)
                .Select(r => new
                {
                    r.Id,
                    r.LastName,
                    r.FirstName,
                    r.MiddleName,
                    r.Rank,
                    r.Position,
                    UnitName = r.Unit != null ? r.Unit.Name : "-",
                    r.ServiceNumber
                })
                .ToList();

            return rows.Select(r => new RoomOccupantDto(
                r.Id,
                NameFormatter.FullName(r.LastName, r.FirstName, r.MiddleName),
                NameFormatter.ShortName(r.LastName, r.FirstName, r.MiddleName),
                r.Rank, r.Position, r.UnitName, r.ServiceNumber)).ToList();
        }

        public List<UnassignedRecipientDto> SearchUnassigned(string? searchText, int take = 8)
        {
            using var db = _dbFactory.CreateDbContext();

            var rows = db.Recipients
                .Where(r => r.RoomId == null)
                .Include(r => r.Unit)
                .ToList();

            if (!string.IsNullOrWhiteSpace(searchText))
            {
                var text = searchText.Trim();
                rows = rows.Where(r =>
                    NameFormatter.FullName(r.LastName, r.FirstName, r.MiddleName).Contains(text, StringComparison.OrdinalIgnoreCase) ||
                    r.ServiceNumber.Contains(text, StringComparison.OrdinalIgnoreCase)).ToList();
            }

            return rows
                .OrderBy(r => r.LastName).ThenBy(r => r.FirstName)
                .Take(take)
                .Select(r => new UnassignedRecipientDto(
                    r.Id, NameFormatter.FullName(r.LastName, r.FirstName, r.MiddleName), r.Rank, r.Unit != null ? r.Unit.Name : "-"))
                .ToList();
        }

        public void AssignOccupant(int recipientId, int roomId)
        {
            using var db = _dbFactory.CreateDbContext();
            var recipient = db.Recipients.First(r => r.Id == recipientId);
            var room = db.Rooms.First(r => r.Id == roomId);

            recipient.RoomId = roomId;
            db.SaveChanges();

            var fullName = NameFormatter.FullName(recipient.LastName, recipient.FirstName, recipient.MiddleName);
            _auditLogService.LogUpdate(db, "Розміщення", recipient.Id, "-", $"{room.Building} №{room.Number}", $"Поселено: {fullName}");
            db.SaveChanges();
        }

        public void UnassignOccupant(int recipientId)
        {
            using var db = _dbFactory.CreateDbContext();
            var recipient = db.Recipients.Include(r => r.Room).First(r => r.Id == recipientId);
            var oldValue = recipient.Room is not null ? $"{recipient.Room.Building} №{recipient.Room.Number}" : "-";

            recipient.RoomId = null;
            db.SaveChanges();

            var fullName = NameFormatter.FullName(recipient.LastName, recipient.FirstName, recipient.MiddleName);
            _auditLogService.LogUpdate(db, "Розміщення", recipient.Id, oldValue, "-", $"Виселено: {fullName}");
            db.SaveChanges();
        }

        private static string BuildSnapshot(Room r)
            => string.Join(", ", new[] { $"{r.Building} №{r.Number}", $"місць: {r.Capacity}", r.Note }
                .Where(v => !string.IsNullOrWhiteSpace(v)));
    }
}
