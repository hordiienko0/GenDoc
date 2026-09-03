using System.Windows;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GenDoc.Models.Enums;

namespace GenDoc.ViewModels.Completeness
{
    public enum MatrixCellState { Present, PresentStale, MissingRequired, MissingOptional, NotApplicable, RosterUnknown }

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

        public bool IsGroupColumn { get; }

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

        public bool IsPresent => State is MatrixCellState.Present or MatrixCellState.PresentStale;

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
        public Visibility OpenMenuVisibility => Show(CanOpen);
        public Visibility RegenerateMenuVisibility => Show(CanRegenerate);
        public Visibility HistoryMenuVisibility => Show(CanHistory);
        public Visibility SaveAsMenuVisibility => Show(CanSaveAs);
        public Visibility GenerateMenuVisibility => Show(CanGenerate);

        private static Visibility Show(bool can) => can ? Visibility.Visible : Visibility.Collapsed;

        public bool HasMenu => CanOpen || CanRegenerate || CanHistory || CanSaveAs || CanGenerate;

        private static Brush Res(string key)
            => Application.Current.Resources[key] as Brush ?? Brushes.Transparent;

        public bool CanOpen => (IsPresent || IsRosterUnknown) && HasContent;
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
            OnPropertyChanged(nameof(OpenMenuVisibility));
            OnPropertyChanged(nameof(RegenerateMenuVisibility));
            OnPropertyChanged(nameof(HistoryMenuVisibility));
            OnPropertyChanged(nameof(SaveAsMenuVisibility));
            OnPropertyChanged(nameof(GenerateMenuVisibility));
            OnPropertyChanged(nameof(HasMenu));
        }
    }

    public interface ICellActionCoordinator
    {
        Task OpenAsync(MatrixCellViewModel cell);
        Task GenerateAsync(MatrixCellViewModel cell);
        Task RegenerateAsync(MatrixCellViewModel cell);
        Task HistoryAsync(MatrixCellViewModel cell);
        Task SaveAsAsync(MatrixCellViewModel cell);
    }
}
