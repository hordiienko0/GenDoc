using GenDoc.Models.Common;
using GenDoc.Models.Enums;

namespace GenDoc.Models
{
    public class Template : ISoftDeletable
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;

        // Коротка назва для заголовків колонок матриці комплектності.
        public string? ShortName { get; set; }
        public string OriginalFileName { get; set; } = string.Empty;
        public byte[] Content { get; set; } = Array.Empty<byte>();
        public DateTime UploadedAt { get; set; }

        // PerRecipient - документ на людину (як завжди); Group - один документ
        // на весь список, з повторюваним блоком {{#…}}/{{/…}}. Визначається
        // автоматично при завантаженні, користувач може перевизначити вручну.
        public TemplateKind Kind { get; set; } = TemplateKind.PerRecipient;

        /// <summary>Для кого шаблон: набори чи постійний склад. Замовчування
        /// Intake - шаблони, заведені до появи поділу, лишаються там, де були.</summary>
        public TemplateAudience Audience { get; set; } = TemplateAudience.Intake;

        // Джерело шаблону, зібраного конструктором: JSON блоків (TemplateBuilderDocument).
        // Заповнене - шаблон відкривається в конструкторі; NULL - шаблон завантажений
        // файлом, конструктор його не чіпає (розібрати довільний .docx назад у блоки
        // надійно неможливо, тому джерело зберігаємо поруч із байтами Content).
        public string? BuilderJson { get; set; }

        public DateTime? DeletedAt { get; set; }
        public string? DeletedBy { get; set; }

        public ICollection<TemplateFieldMapping> FieldMappings { get; set; } = new List<TemplateFieldMapping>();
        public ICollection<GeneratedDocument> GeneratedDocuments { get; set; } = new List<GeneratedDocument>();
    }
}