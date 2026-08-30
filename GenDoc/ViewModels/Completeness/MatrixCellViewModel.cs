using System.Windows;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GenDoc.Models.Enums;

namespace GenDoc.ViewModels.Completeness
{
    public enum MatrixCellState { Present, PresentStale, MissingRequired, MissingOptional, NotApplicable, RosterUnknown }

    // Клітинка сама несе команди - динамічні шаблони колонок не бачать
    // DataContext екрана; ElementName у згенерованому в коді XAML не працює.
    public partial class MatrixCellViewModel : ObservableObject
    {
        private readonly ICellActionCoordinator _coordinator;

        public MatrixCellViewModel(
            ICellActionCoordinator coordinator, int recipientId, int templateId, string fitnessCategory,
            TemplateRequirement requirement, bool isGroupColumn = false)
        {
            IsGroupColumn = isGroupColumn;
            _coordinator = coordinator;
            RecipientId = recipientId;
            TemplateId = templateId;
            FitnessCategory = fitnessCategory;
            Requirement = requirement;
        }

        public int RecipientId { get; }
        public int TemplateId { get; }
        public string FitnessCategory { get; }

        // Групова колонка (v25): DocumentId - це GeneratedGroupDocument, дії обмежені «Відкрити».
        public bool IsGroupColumn { get; }

        // Обов'язковість резолвиться при побудові рядка і не залежить від наявності документа -
        // «n з m» рахує лише Required, незалежно від State.
        public TemplateRequirement Requirement { get; }

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(IsPresent))]
        [NotifyPropertyChangedFor(nameof(IsStale))]
        [NotifyPropertyChangedFor(nameof(IsMissingRequired))]
        [NotifyPropertyChangedFor(nameof(IsMissingOptional))]
        [NotifyPropertyChangedFor(nameof(IsNotApplicable))]
        [NotifyPropertyChangedFor(nameof(ToolTipText))]
        private MatrixCellState state;

        [ObservableProperty]
        private int? documentId;

        [ObservableProperty]
        private int version;

        [ObservableProperty]
        private bool hasContent;

        [ObservableProperty]
        private DocumentSourceType sourceType;

        // «Документ узагалі є?» - для показу меню, версії, посилання на файл.
        public bool IsPresent => State is MatrixCellState.Present or MatrixCellState.PresentStale;

        // «Зараховано до готовності?» - НАЯВНИЙ І НЕ ЗАСТАРІЛИЙ. Саме це число
        // йде в «N з M» і в «Пакет повний». Раніше рядок рахував застарілий як
        // наявний, а зведення по набору - ні, і два екрани про той самий набір
        // суперечили один одному: «Пакет повний» поруч із «Перегенерувати
        // застарілі (4)» і 60 % на картці набору (аудит 2026-08-28).
        public bool IsSatisfied => State == MatrixCellState.Present;
        public bool IsStale => State == MatrixCellState.PresentStale;
        public bool IsMissingRequired => State == MatrixCellState.MissingRequired;
        public bool IsMissingOptional => State == MatrixCellState.MissingOptional;
        public bool IsNotApplicable => State == MatrixCellState.NotApplicable;
        public bool IsRosterUnknown => State == MatrixCellState.RosterUnknown;

        public string VersionText => Version > 1 ? $"в.{Version}" : string.Empty;

        public string ToolTipText => State switch
        {
            MatrixCellState.Present when !HasContent => "файл не збережено",
            MatrixCellState.PresentStale => "Дані людини змінилися після генерації - перегенеруйте",
            MatrixCellState.MissingOptional => "Опційний для цієї категорії - не згенеровано",
            MatrixCellState.NotApplicable => $"Не потрібен для категорії \"{FitnessCategory}\"",
            MatrixCellState.RosterUnknown => "Склад не записано (згенеровано до оновлення) - перегенеруйте відомість, щоб бачити учасників",
            _ => string.Empty
        };

        // Кольори читаються з App.Resources за ключем (жодного хардкоду hex) - обчислюються тут,
        // а не в XAML-шаблоні клітинки, бо StaticResource усередині XamlReader.Parse-фрагмента
        // не гарантовано резолвиться (немає контексту резолюції ресурсів на момент парсингу).
        public Brush Background => State switch
        {
            MatrixCellState.Present => Res("SuccessSoftBrush"),
            MatrixCellState.PresentStale => Res("WarningSoftBrush"),
            MatrixCellState.NotApplicable => Res("BackgroundBrush"),
            _ => Res("PanelBrush")
        };

        public Brush Foreground => State switch
        {
            MatrixCellState.Present => HasContent ? Res("SuccessBrush") : Res("TextSecondaryBrush"),
            MatrixCellState.PresentStale => Res("WarningBrush"),
            _ => Res("TextSecondaryBrush")
        };

        public Brush VersionBrush => Res("AccentBrush");

        public string Glyph => State switch
        {
            MatrixCellState.Present => "✓",
            MatrixCellState.PresentStale => "!",
            MatrixCellState.MissingRequired => "-",
            MatrixCellState.MissingOptional => "(-)",
            MatrixCellState.RosterUnknown => "?",
            _ => string.Empty
        };

        public bool ShowDashedBorder => IsMissingOptional;
        public Brush DashedBorderBrush => ShowDashedBorder ? Res("WarningBrush") : Brushes.Transparent;

        public Visibility VersionVisibility => string.IsNullOrEmpty(VersionText) ? Visibility.Collapsed : Visibility.Visible;
        public Visibility PresentMenuVisibility => IsPresent || IsRosterUnknown ? Visibility.Visible : Visibility.Collapsed;
        public Visibility GenerateMenuVisibility => CanGenerate ? Visibility.Visible : Visibility.Collapsed;

        // Меню не відкривається взагалі, коли в ньому не було б жодного пункту:
        // «Не потрібен» і групова клітинка без документа (людина не в складі -
        // персональних дій нема, групові живуть у «Генерації» та «Архів → Групові»).
        public bool HasMenu => !IsNotApplicable && !(IsGroupColumn && DocumentId is null);

        private static Brush Res(string key)
            => Application.Current.Resources[key] as Brush ?? Brushes.Transparent;

        public bool CanOpen => (IsPresent || IsRosterUnknown) && HasContent;
        // Групові: лише «Відкрити» - генерація, історія і збереження живуть в «Архів → Групові» та «Генерації».
        public bool CanSaveAs => IsPresent && HasContent && !IsGroupColumn;
        public bool CanRegenerate => IsPresent && SourceType == DocumentSourceType.Generated && !IsGroupColumn;
        public bool CanHistory => IsPresent && !IsGroupColumn;
        public bool CanGenerate => (IsMissingRequired || IsMissingOptional) && !IsGroupColumn;

        [RelayCommand(CanExecute = nameof(CanOpen))]
        private Task OpenAsync() => _coordinator.OpenAsync(this);

        [RelayCommand(CanExecute = nameof(CanGenerate))]
        private Task GenerateAsync() => _coordinator.GenerateAsync(this);

        [RelayCommand(CanExecute = nameof(CanRegenerate))]
        private Task RegenerateAsync() => _coordinator.RegenerateAsync(this);

        [RelayCommand(CanExecute = nameof(CanHistory))]
        private Task HistoryAsync() => _coordinator.HistoryAsync(this);

        [RelayCommand(CanExecute = nameof(CanSaveAs))]
        private Task SaveAsAsync() => _coordinator.SaveAsAsync(this);

        // Викликається рівно раз при побудові рядка (не в getter - віртуалізація смикає getter-и багаторазово).
        public void Initialize(Services.Completeness.MatrixDocDto? doc)
        {
            if (Requirement == TemplateRequirement.NotApplicable)
            {
                State = MatrixCellState.NotApplicable;
                DocumentId = null;
                Version = 0;
                HasContent = false;
                SourceType = DocumentSourceType.Generated;
            }
            else if (doc is not null)
            {
                State = doc.RosterUnknown
                    ? MatrixCellState.RosterUnknown
                    : doc.IsStale ? MatrixCellState.PresentStale : MatrixCellState.Present;
                DocumentId = doc.Id;
                Version = doc.Version;
                HasContent = doc.HasContent;
                SourceType = doc.SourceType;
            }
            else
            {
                State = Requirement == TemplateRequirement.Required
                    ? MatrixCellState.MissingRequired
                    : MatrixCellState.MissingOptional;
                DocumentId = null;
                Version = 0;
                HasContent = false;
                SourceType = DocumentSourceType.Generated;
            }

            RaiseCommandStates();
        }

        private void RaiseCommandStates()
        {
            OpenCommand.NotifyCanExecuteChanged();
            GenerateCommand.NotifyCanExecuteChanged();
            RegenerateCommand.NotifyCanExecuteChanged();
            HistoryCommand.NotifyCanExecuteChanged();
            SaveAsCommand.NotifyCanExecuteChanged();
            OnPropertyChanged(nameof(CanOpen));
            OnPropertyChanged(nameof(CanSaveAs));
            OnPropertyChanged(nameof(CanRegenerate));
            OnPropertyChanged(nameof(CanHistory));
            OnPropertyChanged(nameof(CanGenerate));
            OnPropertyChanged(nameof(VersionText));
            OnPropertyChanged(nameof(Background));
            OnPropertyChanged(nameof(Foreground));
            OnPropertyChanged(nameof(Glyph));
            OnPropertyChanged(nameof(ShowDashedBorder));
            OnPropertyChanged(nameof(DashedBorderBrush));
            OnPropertyChanged(nameof(ToolTipText));
            OnPropertyChanged(nameof(VersionVisibility));
            OnPropertyChanged(nameof(PresentMenuVisibility));
            OnPropertyChanged(nameof(GenerateMenuVisibility));
            OnPropertyChanged(nameof(HasMenu));
        }
    }

    // Реалізується CompletenessViewModel - клітинка делегує дії координатору.
    public interface ICellActionCoordinator
    {
        Task OpenAsync(MatrixCellViewModel cell);
        Task GenerateAsync(MatrixCellViewModel cell);
        Task RegenerateAsync(MatrixCellViewModel cell);
        Task HistoryAsync(MatrixCellViewModel cell);
        Task SaveAsAsync(MatrixCellViewModel cell);
    }
}
