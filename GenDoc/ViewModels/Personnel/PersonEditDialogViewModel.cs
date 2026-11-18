using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace GenDoc.ViewModels.Personnel
{
    // Окреме вікно замість бічної панелі - і для нової особи, і для редагування наявної. Усередині та сама картка
    // (PersonCardViewModel) з її перевірками й збереженням. «Зберегти й додати ще» (лише для нових) підставляє
    // чисту картку - щоб вносити набір підряд.
    public partial class PersonEditDialogViewModel : DialogViewModelBase
    {
        private readonly Func<PersonCardViewModel>? _newCard;

        private PersonEditDialogViewModel(PersonCardViewModel card, Func<PersonCardViewModel>? newCard, string unitDisplay)
        {
            _newCard = newCard;
            this.card = card;
            UnitDisplay = unitDisplay;

            if (IsAddMode)
            {
                WindowTitle = "Додавання особи";
                Heading = "Нова особа";
                PersonLine = null;
                Hint = "Обов'язкове лише прізвище - решту можна дописати пізніше в картці особи.";
                ShortcutsHint = "Ctrl+Enter - зберегти · Ctrl+Shift+Enter - зберегти й додати ще · Esc - закрити · Ctrl + коліщатко миші - масштаб";
            }
            else
            {
                WindowTitle = $"Редагування - {card.HeaderName}";
                Heading = "Редагування особи";
                PersonLine = string.Join(" · ", new[] { card.HeaderName, card.HeaderSub }.Where(p => !string.IsNullOrWhiteSpace(p)));
                Hint = "Підрозділ змінюється через «Перемістити до…» у списку.";
                ShortcutsHint = "Ctrl+Enter - зберегти · Esc - закрити · Ctrl + коліщатко миші - масштаб";
            }
        }

        public static PersonEditDialogViewModel ForNew(Func<PersonCardViewModel> newCard, string unitDisplay)
            => new(newCard(), newCard, unitDisplay);

        public static PersonEditDialogViewModel ForExisting(PersonCardViewModel card, string unitDisplay)
            => new(card, null, unitDisplay);

        public bool IsAddMode => _newCard is not null;

        public string WindowTitle { get; }
        public string Heading { get; }
        public string? PersonLine { get; }
        public string Hint { get; }
        public string ShortcutsHint { get; }
        public string UnitDisplay { get; }

        [ObservableProperty] private PersonCardViewModel card;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(AddedNote))]
        private int savedCount;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(AddedNote))]
        private string? lastSavedName;

        public string? AddedNote => !IsAddMode || SavedCount == 0
            ? null
            : $"✓ Збережено: {LastSavedName}  ·  за цей раз додано {PersonnelViewModel.RecordsText(SavedCount)}";

        // Чиста картка після «Зберегти й додати ще» - вікно ставить курсор у «Прізвище».
        public event Action? CardReplaced;

        // Не пройшла перевірка - вікно прокручує до першої помилки.
        public event Action? SaveFailed;

        private bool _busy;

        public bool NeedsCloseConfirmation => _busy || Card.IsDirty;

        private async Task<bool> SaveCurrentAsync()
        {
            if (_busy) return false;
            _busy = true;
            try
            {
                // Без змін у наявній особі зберігати нічого - просто закриваємо.
                if (!Card.IsNew && !Card.IsDirty) return true;

                if (!await Card.SaveAsync())
                {
                    SaveFailed?.Invoke();
                    return false;
                }
                SavedCount++;
                LastSavedName = Card.HeaderName;
                return true;
            }
            finally
            {
                _busy = false;
            }
        }

        [RelayCommand]
        private async Task SaveAsync()
        {
            if (await SaveCurrentAsync()) CloseDialog(SavedCount > 0);
        }

        [RelayCommand(CanExecute = nameof(IsAddMode))]
        private async Task SaveAndNextAsync()
        {
            if (_newCard is null || !await SaveCurrentAsync()) return;
            Card = _newCard();
            CardReplaced?.Invoke();
        }

        [RelayCommand]
        private void Cancel() => CloseDialog(SavedCount > 0);

        // Хрестик / Esc / «Скасувати» з незбереженими змінами: ті самі три варіанти, що й при виході з картки.
        public async Task<bool> ConfirmCloseAsync(Window owner)
        {
            if (_busy) return false;
            if (!Card.IsDirty) return true;

            var result = MessageBox.Show(owner,
                IsAddMode ? "Зберегти введені дані?" : "Зберегти зміни?",
                IsAddMode ? "Незбережені дані" : "Незбережені зміни",
                MessageBoxButton.YesNoCancel, MessageBoxImage.Question);

            return result switch
            {
                MessageBoxResult.Yes => await SaveCurrentAsync(),
                MessageBoxResult.No => true,
                _ => false
            };
        }
    }
}
