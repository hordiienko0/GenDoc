using GenDoc.Models.Common;
using GenDoc.Models.Enums;

namespace GenDoc.Models
{
    public class Intake : ISoftDeletable
    {
        public int Id { get; set; }

        public int Number { get; set; }
        public string DisplayNumber { get; set; } = string.Empty;

        public DateOnly DateStart { get; set; }
        public DateOnly DateEnd { get; set; }

        public IntakeStatus Status { get; set; } = IntakeStatus.Planned;

        // Кореневий вузол гілки набору в дереві підрозділів.
        public int RootOrgNodeId { get; set; }

        public DateOnly? DateClosed { get; set; }
        public string? ClosedBy { get; set; }

        // Статус виставлено вручну (закриття/повторне відкриття) — автоперехід за датами його не чіпає.
        public bool StatusIsPinned { get; set; }

        public int? DefaultPackageId { get; set; }

        public DateTime? DeletedAt { get; set; }
        public string? DeletedBy { get; set; }
    }
}
