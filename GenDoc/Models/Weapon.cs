using GenDoc.Models.Common;

namespace GenDoc.Models
{
    // Одна людина може мати кілька одиниць (автомат + пістолет) — тому
    // власник тут (RecipientId), а не навпаки.
    public class Weapon : ISoftDeletable
    {
        public int Id { get; set; }

        public int RecipientId { get; set; }
        public Recipient? Recipient { get; set; }

        // Найменування — напр. "АК-74"
        public string Name { get; set; } = string.Empty;
        public string SerialNumber { get; set; } = string.Empty;

        // Сирий вихідний фрагмент, з якого розпарсено цю одиницю — щоб нічого
        // не губилось при кривому парсингу вихідного рядка з імпорту.
        public string? RawText { get; set; }

        public DateOnly? IssuedAt { get; set; }
        public string? Note { get; set; }

        public DateTime? DeletedAt { get; set; }
        public string? DeletedBy { get; set; }
    }
}
