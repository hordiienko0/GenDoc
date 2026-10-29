using CommunityToolkit.Mvvm.ComponentModel;
using GenDoc.Services.Personnel;

namespace GenDoc.ViewModels.Personnel
{
    public partial class QuestionnaireFieldViewModel : ObservableObject
    {
        public QuestionnaireFieldViewModel(QuestionnaireField field, string? value, bool isEditing)
        {
            Key = field.Key;
            Label = field.Label;
            IsMultiline = field.Kind == QuestionnaireFieldKind.MultilineText;
            Hint = field.Kind == QuestionnaireFieldKind.Date ? "дд.мм.рррр" : null;
            // Поля попередніх анкет показуємо, лише коли в них щось є, - щоб не захаращувати картку порожніми рядками.
            IsVisible = field.InCurrentForm || !string.IsNullOrWhiteSpace(value);
            this.value = value;
            this.isEditing = isEditing;
        }

        public string Key { get; }
        public string Label { get; }
        public bool IsMultiline { get; }
        public string? Hint { get; }
        public bool IsVisible { get; }

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(DisplayValue))]
        private string? value;

        public string DisplayValue => string.IsNullOrWhiteSpace(Value) ? "-" : Value;

        [ObservableProperty] private string? error;
        [ObservableProperty] private bool isEditing;
    }
}
