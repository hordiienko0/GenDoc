using GenDoc.Models.Common;
using GenDoc.Models.Enums;
using System.ComponentModel.DataAnnotations.Schema;

namespace GenDoc.Models
{
    public class Recipient : ISoftDeletable
    {
        public int Id { get; set; }

        public string LastName { get; set; } = string.Empty;
        public string FirstName { get; set; } = string.Empty;
        public string? MiddleName { get; set; }

        public string Rank { get; set; } = string.Empty;
        public string Position { get; set; } = string.Empty;
        public string ServiceNumber { get; set; } = string.Empty;
        public DateOnly? DateOfBirth { get; set; }

        public int? UnitId { get; set; }
        public Unit? Unit { get; set; }

        public int? RoomId { get; set; }
        public Room? Room { get; set; }

        // Одна людина може мати кілька одиниць зброї (автомат + пістолет) -
        // тому зв'язок один-до-багатьох, власник - Weapon.RecipientId.
        public ICollection<Weapon> Weapons { get; set; } = new List<Weapon>();

        // Окрема структурована модель авто (марка + номер) - на відміну від
        // вільнотекстового поля Vehicle нижче (анкетні дані, лишене для сумісності).
        // Поки не заповнюється імпортом і не використовується - консолідація потім.
        public int? AssignedVehicleId { get; set; }
        public Vehicle? AssignedVehicle { get; set; }

        public int? OrgNodeId { get; set; }
        public OrgNode? OrgNode { get; set; }

        public int? IntakeId { get; set; }

        // придатний · обмежено придатний · непридатний
        public string? FitnessCategory { get; set; }

        // Анкетні дані (прикомандировані) - Б.1
        public string? Nationality { get; set; }
        public string? Vos { get; set; }
        public DateOnly? CourseArrivalDate { get; set; }
        public string? MaritalStatus { get; set; }
        public string? RegistrationAddress { get; set; }
        public string? ResidenceAddress { get; set; }
        public string? Phone { get; set; }
        public string? Note { get; set; }
        public string? GroupName { get; set; }
        public string? NameTransliterated { get; set; }
        public string? ServedBefore { get; set; }
        public string? ExtraNote { get; set; }
        public string? CommanderContact { get; set; }
        public string? TravelCertificateNumber { get; set; }
        public string? FoodCertificate { get; set; }
        public string? IdDocumentNumber { get; set; }
        public string? MedicalBoard { get; set; }
        public string? MedicalBoardConclusion { get; set; }
        public string? OriginUnit { get; set; }
        public string? Vehicle { get; set; }

        // Постійний склад: курсовий офіцер (не звання/посада - окрема ознака,
        // впливає на фільтр списку й доступна для мапінгу плейсхолдерів шаблонів).
        public bool IsCourseOfficer { get; set; }

        // Стать - для узгодження форм (прибув/прибула, таким/такою) і відмінювання,
        // коли по батькові відсутнє або не дає однозначної відповіді.
        public Gender? Gender { get; set; }

        // Уточнення відмінків: якщо задано, використовується замість
        // автоматичного відмінювання (Services/UkrainianGrammar.cs).
        public string? RankAccusative { get; set; }
        public string? FullNameAccusative { get; set; }

        // Дата посвідчення про відрядження - джерело для {{дата_посвідчення}} (UkrainianDate.Long).
        public DateOnly? TravelCertificateDate { get; set; }

        public DateTime? DeletedAt { get; set; }
        public string? DeletedBy { get; set; }

        public ICollection<GeneratedDocument> GeneratedDocuments { get; set; } = new List<GeneratedDocument>();

        [NotMapped]
        public string FullName => string.Join(' ', new[] { LastName, FirstName, MiddleName }
            .Where(p => !string.IsNullOrWhiteSpace(p)));
    }
}
