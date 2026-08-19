using GenDoc.Services.Completeness;

namespace GenDoc.ViewModels.Personnel
{
    // Один рядок вкладки «Документи» картки особи - стан одного шаблону пакета.
    public class RecipientDocRowViewModel
    {
        public RecipientDocRowViewModel(RecipientDocStatus status)
        {
            TemplateId = status.TemplateId;
            TemplateName = status.TemplateName;
            DocumentId = status.DocumentId;
            HasContent = status.HasContent;
            IsStale = status.IsStale;
            MetaText = status.HasContent ? $"версія {status.Version}" : string.Empty;
        }

        public int TemplateId { get; }
        public string TemplateName { get; }
        public int? DocumentId { get; }
        public bool HasContent { get; }
        public bool IsStale { get; }
        public string MetaText { get; }

        public string StateText => !HasContent ? "Немає" : IsStale ? "Застарів" : "Є";

        // Для DataTrigger у XAML - те саме, що StateText, але стабільний ключ (не залежить від локалізації).
        public string StateKind => !HasContent ? "Missing" : IsStale ? "Stale" : "Current";

        public string ActionLabel => HasContent ? "Перегенерувати" : "Згенерувати";
    }
}
