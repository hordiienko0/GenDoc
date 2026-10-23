using GenDoc.Models.Common;
using GenDoc.Models.Enums;

namespace GenDoc.Models
{
    /// <summary>Графік чергувань на період: люди, наряди «н» і звільнення (зелене).</summary>
    public class DutyRoster : ISoftDeletable
    {
        public int Id { get; set; }

        public DateOnly DateFrom { get; set; }
        public DateOnly DateTo { get; set; }

        public int? IntakeId { get; set; }
        public int? OrgNodeId { get; set; }

        public int PeoplePerDay { get; set; } = 3;
        public int Seed { get; set; }

        /// <summary>Рядки шапки без рядка періоду - той будується з дат.</summary>
        public string Heading { get; set; } = string.Empty;
        public string? FooterJson { get; set; }

        /// <summary>Цілі дні без наряду (зелений стовпчик), ISO-дати через кому.</summary>
        public string? ExemptDays { get; set; }

        /// <summary>Кого прибрали з графіка (Id через кому) - наступний графік набору їх теж не бере.</summary>
        public string? ExcludedRecipients { get; set; }

        public bool UseEventExemptions { get; set; } = true;
        public bool CarryOverBalance { get; set; } = true;

        /// <summary>Лише «н» у клітинках; типово - номер зміни римськими (І, ІІ, ІІІ за порядком у списку дня).</summary>
        public bool DutyMarkOnly { get; set; }

        /// <summary>Субота й неділя одразу зелені (звільнені цілим днем).</summary>
        public bool WeekendsExempt { get; set; } = true;

        public DateTime CreatedAt { get; set; }
        public string? CreatedBy { get; set; }
        public DateTime? UpdatedAt { get; set; }

        public DateTime? DeletedAt { get; set; }
        public string? DeletedBy { get; set; }

        public ICollection<DutyRosterMember> Members { get; set; } = new List<DutyRosterMember>();
    }

    public class DutyRosterMember
    {
        public int Id { get; set; }

        public int RosterId { get; set; }
        public DutyRoster? Roster { get; set; }

        /// <summary>Порожній у вписаного вручну.</summary>
        public int? RecipientId { get; set; }

        /// <summary>Знімок ПІБ «ПРІЗВИЩЕ І.Б.» - старий графік друкується так, як був.</summary>
        public string DisplayName { get; set; } = string.Empty;

        public int SortOrder { get; set; }
        public bool ExemptWholePeriod { get; set; }

        public ICollection<DutyRosterCell> Cells { get; set; } = new List<DutyRosterCell>();
    }

    public class DutyRosterCell
    {
        public int MemberId { get; set; }
        public DutyRosterMember? Member { get; set; }

        public DateOnly Date { get; set; }
        public DutyCellKind Kind { get; set; }
        public DutyCellSource Source { get; set; }
    }
}
