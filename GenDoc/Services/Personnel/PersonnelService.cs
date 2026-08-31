using GenDoc.Data;
using GenDoc.Models;
using GenDoc.Services.Intakes;
using Microsoft.EntityFrameworkCore;

namespace GenDoc.Services.Personnel
{
    public class PersonnelService : IPersonnelService
    {
        private readonly IDbContextFactory<AppDbContext> _dbFactory;
        private readonly IAuditLogService _auditLogService;
        private readonly ICurrentUserContext _currentUserContext;

        public PersonnelService(
            IDbContextFactory<AppDbContext> dbFactory,
            IAuditLogService auditLogService,
            ICurrentUserContext currentUserContext)
        {
            _dbFactory = dbFactory;
            _auditLogService = auditLogService;
            _currentUserContext = currentUserContext;
        }

        public async Task<List<PersonListItem>> QueryByNodeAsync(int nodeId, bool includeDescendants)
        {
            using var db = _dbFactory.CreateDbContext();
            var path = await db.OrgNodes.Where(n => n.Id == nodeId).Select(n => n.Path).FirstOrDefaultAsync();
            if (path is null) return new List<PersonListItem>();

            var query = includeDescendants
                ? db.Recipients.Where(r => r.OrgNode != null && r.OrgNode.Path.StartsWith(path))
                : db.Recipients.Where(r => r.OrgNodeId == nodeId);

            var entities = await query.Include(r => r.Room).AsNoTracking().ToListAsync();

            return entities
                .OrderBy(r => r.LastName).ThenBy(r => r.FirstName)
                .Select(r => new PersonListItem(
                    r.Id, r.LastName, r.FirstName, r.MiddleName, r.Rank, r.Position,
                    r.FitnessCategory, FormatRoom(r.Room), r.OrgNodeId!.Value, r.IntakeId))
                .ToList();
        }

        public async Task<PersonEditModel?> GetForEditAsync(int id)
        {
            using var db = _dbFactory.CreateDbContext();
            var r = await db.Recipients.Include(x => x.Room).AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
            if (r is null || r.OrgNodeId is null) return null;

            return new PersonEditModel
            {
                Id = r.Id,
                LastName = r.LastName,
                FirstName = r.FirstName,
                MiddleName = r.MiddleName,
                Rank = r.Rank,
                Position = r.Position,
                ServiceNumber = r.ServiceNumber,
                RoomBuilding = r.Room?.Building,
                RoomNumber = r.Room?.Number,
                FitnessCategory = r.FitnessCategory,
                OrgNodeId = r.OrgNodeId.Value,
                IntakeId = r.IntakeId
            };
        }

        public async Task<PersonSaveResult> SaveAsync(PersonEditModel model)
        {
            var errors = new Dictionary<string, string>();

            if (string.IsNullOrWhiteSpace(model.LastName))
                errors["LastName"] = "Прізвище обов'язкове";

            var serviceNumber = model.ServiceNumber.Trim();
            if (serviceNumber.Length > 0 && serviceNumber.Any(char.IsWhiteSpace))
                errors["ServiceNumber"] = "Особовий номер не може містити пробіли";

            using var db = _dbFactory.CreateDbContext();

            if (serviceNumber.Length > 0 && !errors.ContainsKey("ServiceNumber"))
            {
                var duplicate = await db.Recipients
                    .FirstOrDefaultAsync(r => r.ServiceNumber == serviceNumber && r.Id != model.Id);
                if (duplicate is not null)
                    errors["ServiceNumber"] = $"Номер вже використовується: {duplicate.LastName} {duplicate.FirstName}".Trim();
            }

            if (errors.Count > 0) return new PersonSaveResult(false, model.Id, errors);

            var isNew = model.Id == 0;
            var recipient = isNew ? new Recipient() : await db.Recipients.FirstAsync(r => r.Id == model.Id);
            var oldSnapshot = isNew ? null : BuildSnapshot(recipient);

            recipient.LastName = model.LastName.Trim();
            recipient.FirstName = model.FirstName.Trim();
            recipient.MiddleName = string.IsNullOrWhiteSpace(model.MiddleName) ? null : model.MiddleName.Trim();
            recipient.Rank = model.Rank.Trim();
            recipient.Position = model.Position.Trim();
            recipient.ServiceNumber = serviceNumber;

            var previousFitness = recipient.FitnessCategory;
            recipient.FitnessCategory = string.IsNullOrWhiteSpace(model.FitnessCategory) ? null : model.FitnessCategory;
            recipient.RoomId = await ResolveRoomIdAsync(db, model.RoomBuilding, model.RoomNumber);

            if (isNew)
            {
                recipient.OrgNodeId = model.OrgNodeId;
                recipient.IntakeId = model.IntakeId;
                db.Recipients.Add(recipient);
            }

            // Категорія придатності визначає папку в дереві набору, тож зміна
            // категорії - це переїзд. Раніше розкладав лише імпорт, і людина,
            // якій змінили придатність у картці, лишалася в старій папці:
            // дерево показувало одне, картка - інше.
            //
            // Рухаємо ЛИШЕ на зміну категорії. Інакше кожне збереження (правка
            // телефону, кімнати) висмикувало б людину з папки, куди курсовий
            // переставив її руками через «Перемістити до…».
            var fitnessChanged = !UkrainianCollation.IgnoreCase.Equals(
                previousFitness ?? string.Empty, recipient.FitnessCategory ?? string.Empty);

            if (recipient.IntakeId is int intakeId && (isNew || fitnessChanged))
            {
                var folderId = await IntakeFitnessFolders.ResolveAsync(db, intakeId, recipient.FitnessCategory);
                if (folderId is int id) recipient.OrgNodeId = id;
            }

            await db.SaveChangesAsync();

            var newSnapshot = BuildSnapshot(recipient);
            if (isNew)
                _auditLogService.LogCreate(db, "Recipient", recipient.Id, newSnapshot);
            else
                _auditLogService.LogUpdate(db, "Recipient", recipient.Id, oldSnapshot, newSnapshot);
            await db.SaveChangesAsync();

            return new PersonSaveResult(true, recipient.Id, errors);
        }

        public async Task MoveManyAsync(IReadOnlyList<int> recipientIds, int targetNodeId, string sourceBranchName)
        {
            using var db = _dbFactory.CreateDbContext();
            var target = await db.OrgNodes.FirstAsync(n => n.Id == targetNodeId);
            var recipients = await db.Recipients.Where(r => recipientIds.Contains(r.Id)).ToListAsync();

            async Task ApplyAsync()
            {
                foreach (var r in recipients)
                {
                    r.OrgNodeId = target.Id;
                    r.IntakeId = target.IntakeId;
                    _auditLogService.Log(db, "Переміщено", "Recipient", r.Id,
                        sourceBranchName, target.Name, $"{r.LastName} {r.FirstName}".Trim());
                }

                _auditLogService.Log(db, "Переміщено (масово)", "Recipient", 0, null, null,
                    $"Переміщено {recipients.Count} осіб: {sourceBranchName} → {target.Name}");
                await db.SaveChangesAsync();
            }

            if (db.Database.CurrentTransaction is null)
            {
                using var tx = await db.Database.BeginTransactionAsync();
                await ApplyAsync();
                await tx.CommitAsync();
            }
            else
            {
                await ApplyAsync();
            }
        }

        public async Task DeleteManyAsync(IReadOnlyList<int> recipientIds)
        {
            using var db = _dbFactory.CreateDbContext();
            var recipients = await db.Recipients.Where(r => recipientIds.Contains(r.Id)).ToListAsync();

            async Task ApplyAsync()
            {
                foreach (var r in recipients)
                {
                    var snapshot = BuildSnapshot(r);
                    r.DeletedAt = DateTime.Now;
                    r.DeletedBy = _currentUserContext.CurrentUserFullName;
                    _auditLogService.LogDelete(db, "Recipient", r.Id, snapshot);
                }

                await db.SaveChangesAsync();
            }

            if (db.Database.CurrentTransaction is null)
            {
                using var tx = await db.Database.BeginTransactionAsync();
                await ApplyAsync();
                await tx.CommitAsync();
            }
            else
            {
                await ApplyAsync();
            }
        }

        private static async Task<int?> ResolveRoomIdAsync(AppDbContext db, string? building, string? number)
        {
            if (string.IsNullOrWhiteSpace(building) || string.IsNullOrWhiteSpace(number)) return null;
            var b = building.Trim();
            var n = number.Trim();

            // Порівняння в пам'яті, не запитом: SQLite вважав би «Корпус А» і
            // «корпус а» різними кімнатами й плодив дублі (аудит 2026-08-28).
            var existing = (await db.Rooms.ToListAsync())
                .FirstOrDefault(r => UkrainianCollation.IgnoreCase.Equals(r.Building, b)
                                  && UkrainianCollation.IgnoreCase.Equals(r.Number, n));
            if (existing is not null) return existing.Id;

            var room = new Room { Building = b, Number = n, Capacity = 1 };
            db.Rooms.Add(room);
            await db.SaveChangesAsync();
            return room.Id;
        }

        private static string FormatRoom(Room? room)
        {
            if (room is null || string.IsNullOrWhiteSpace(room.Number)) return "-";
            var building = room.Building?.Trim();
            if (string.IsNullOrEmpty(building)) return room.Number;
            return $"{room.Number} ({building[^1..].ToUpperInvariant()})";
        }

        private static string BuildSnapshot(Recipient r)
            => string.Join(", ", new[]
            {
                $"{r.LastName} {r.FirstName} {r.MiddleName}".Trim(),
                r.Rank, r.Position, $"№{r.ServiceNumber}", r.FitnessCategory
            }.Where(v => !string.IsNullOrWhiteSpace(v)));
    }
}
