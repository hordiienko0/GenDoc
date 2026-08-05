using System.IO;
using GenDoc.Data;
using GenDoc.Models;
using GenDoc.Services;
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
            var entities = QueryEntities(searchText, sortColumn, sortDescending);

            return entities.Select(r => new RecipientListItem(
                r.Id,
                r.LastName + " " + r.FirstName + (r.MiddleName != null ? " " + r.MiddleName : ""),
                r.Rank,
                r.Position,
                r.Unit != null ? r.Unit.Name : "—",
                FormatRoom(r.Room))).ToList();
        }

        public List<Recipient> SearchEntities(string? searchText, RecipientSortColumn sortColumn = RecipientSortColumn.FullName, bool sortDescending = false)
            => QueryEntities(searchText, sortColumn, sortDescending);

        public void LogExport(int count, string filePath)
        {
            using var db = _dbFactory.CreateDbContext();
            _auditLogService.LogExport(db, "Recipient", count, $"{count} записів → {Path.GetFileName(filePath)}");
            db.SaveChanges();
        }

        // Матеріалізуємо сутності одразу (ToList) — подальший пошук за словами
        // і форматування кімнати виконуються в пам'яті на C#, EF Core/SQLite
        // не повинен транслювати динамічне розбиття рядка на слова в SQL.
        // Спільна точка для UI-списку (Search) і експорту (SearchEntities) — обидва
        // повинні бачити однакову вибірку з урахуванням активного пошуку/сортування.
        private List<Recipient> QueryEntities(string? searchText, RecipientSortColumn sortColumn, bool sortDescending)
        {
            using var db = _dbFactory.CreateDbContext();

            var entities = db.Recipients
                .Include(r => r.Unit)
                .Include(r => r.Room)
                .ToList();

            var words = (searchText ?? string.Empty).Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (words.Length > 0)
            {
                entities = entities.Where(r => MatchesAllWords(r, words)).ToList();
            }

            IEnumerable<Recipient> sorted = sortColumn switch
            {
                RecipientSortColumn.Rank => sortDescending ? entities.OrderByDescending(r => r.Rank) : entities.OrderBy(r => r.Rank),
                RecipientSortColumn.Position => sortDescending ? entities.OrderByDescending(r => r.Position) : entities.OrderBy(r => r.Position),
                RecipientSortColumn.UnitName => sortDescending
                    ? entities.OrderByDescending(r => r.Unit != null ? r.Unit.Name : "—")
                    : entities.OrderBy(r => r.Unit != null ? r.Unit.Name : "—"),
                RecipientSortColumn.Room => sortDescending
                    ? entities.OrderByDescending(r => FormatRoom(r.Room))
                    : entities.OrderBy(r => FormatRoom(r.Room)),
                _ => sortDescending
                    ? entities.OrderByDescending(r => r.LastName, UkrainianCollation.Surname).ThenByDescending(r => r.FirstName, UkrainianCollation.Surname)
                    : entities.OrderBy(r => r.LastName, UkrainianCollation.Surname).ThenBy(r => r.FirstName, UkrainianCollation.Surname),
            };

            return sorted.ToList();
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
                RoomNumber = r.Room?.Number,

                Nationality = r.Nationality,
                Vos = r.Vos,
                CourseArrivalDate = r.CourseArrivalDate,
                MaritalStatus = r.MaritalStatus,
                RegistrationAddress = r.RegistrationAddress,
                ResidenceAddress = r.ResidenceAddress,
                Phone = r.Phone,
                Note = r.Note,
                GroupName = r.GroupName,
                NameTransliterated = r.NameTransliterated,
                ServedBefore = r.ServedBefore,
                ExtraNote = r.ExtraNote,
                CommanderContact = r.CommanderContact,
                TravelCertificateNumber = r.TravelCertificateNumber,
                TravelCertificateDate = r.TravelCertificateDate,
                FoodCertificate = r.FoodCertificate,
                IdDocumentNumber = r.IdDocumentNumber,
                MedicalBoard = r.MedicalBoard,
                MedicalBoardConclusion = r.MedicalBoardConclusion,
                OriginUnit = r.OriginUnit,
                Vehicle = r.Vehicle,

                Gender = r.Gender,
                RankAccusative = r.RankAccusative,
                FullNameAccusative = r.FullNameAccusative
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

            recipient.Nationality = TrimOrNull(model.Nationality);
            recipient.Vos = TrimOrNull(model.Vos);
            recipient.CourseArrivalDate = model.CourseArrivalDate;
            recipient.MaritalStatus = TrimOrNull(model.MaritalStatus);
            recipient.RegistrationAddress = TrimOrNull(model.RegistrationAddress);
            recipient.ResidenceAddress = TrimOrNull(model.ResidenceAddress);
            recipient.Phone = TrimOrNull(model.Phone);
            recipient.Note = TrimOrNull(model.Note);
            recipient.GroupName = TrimOrNull(model.GroupName);
            recipient.NameTransliterated = TrimOrNull(model.NameTransliterated);
            recipient.ServedBefore = TrimOrNull(model.ServedBefore);
            recipient.ExtraNote = TrimOrNull(model.ExtraNote);
            recipient.CommanderContact = TrimOrNull(model.CommanderContact);
            recipient.TravelCertificateNumber = TrimOrNull(model.TravelCertificateNumber);
            recipient.TravelCertificateDate = model.TravelCertificateDate;
            recipient.FoodCertificate = TrimOrNull(model.FoodCertificate);
            recipient.IdDocumentNumber = TrimOrNull(model.IdDocumentNumber);
            recipient.MedicalBoard = TrimOrNull(model.MedicalBoard);
            recipient.MedicalBoardConclusion = TrimOrNull(model.MedicalBoardConclusion);
            recipient.OriginUnit = TrimOrNull(model.OriginUnit);
            recipient.Vehicle = TrimOrNull(model.Vehicle);

            recipient.Gender = model.Gender;
            recipient.RankAccusative = TrimOrNull(model.RankAccusative);
            recipient.FullNameAccusative = TrimOrNull(model.FullNameAccusative);

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

        private static string? TrimOrNull(string? value)
            => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

        private static string BuildSnapshot(Recipient r)
            => string.Join(", ", new[]
            {
                $"{r.LastName} {r.FirstName} {r.MiddleName}".Trim(),
                r.Rank, r.Position, $"№{r.ServiceNumber}",
                r.Nationality, r.Vos,
                r.CourseArrivalDate?.ToString("dd.MM.yyyy"),
                r.MaritalStatus, r.RegistrationAddress, r.ResidenceAddress, r.Phone,
                r.Note, r.GroupName, r.NameTransliterated, r.ServedBefore, r.ExtraNote,
                r.CommanderContact, r.TravelCertificateNumber, r.FoodCertificate,
                r.IdDocumentNumber, r.MedicalBoard, r.MedicalBoardConclusion,
                r.OriginUnit, r.Vehicle
            }.Where(v => !string.IsNullOrWhiteSpace(v)));
    }
}
