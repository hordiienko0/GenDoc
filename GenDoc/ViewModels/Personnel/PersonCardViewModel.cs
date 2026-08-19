using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GenDoc.Services;
using GenDoc.Services.Completeness;
using GenDoc.Services.Documents;
using GenDoc.Services.Generation;
using GenDoc.Services.Intakes;
using GenDoc.Services.Personnel;
using GenDoc.ViewModels.Archive;

namespace GenDoc.ViewModels.Personnel
{
    public partial class PersonCardViewModel : ObservableObject
    {
        public static readonly string[] FitnessOptions =
            { "придатний", "обмежено придатний", "непридатний" };

        private static readonly HashSet<string> EditableFields = new()
        {
            nameof(LastName), nameof(FirstMiddle), nameof(Rank), nameof(Position),
            nameof(ServiceNumber), nameof(RoomBuilding), nameof(RoomNumber), nameof(Fitness)
        };

        private readonly IPersonnelService _personnelService;
        private readonly ICompletenessService _completenessService;
        private readonly IDocumentArchiveService _archiveService;
        private readonly IGenerationService _generationService;
        private readonly IIntakeService _intakeService;
        private readonly IDialogService _dialogService;
        private readonly Services.Generation.IManualTagFormBuilder _manualTagFormBuilder;

        /// <summary>Ключ, під яким запам'ятовуються минулі значення саме для
        /// цього місця - щоб вони не змішувалися з іншими екранами.</summary>
        private const string ManualTagContextKey = "person-card-regenerate";
        private string _snapshot = string.Empty;
        private bool _documentsLoaded;
        private int? _packageId;

        private record CleanValues(
            string LastName, string FirstMiddle, string Rank, string Position, string ServiceNumber,
            string? RoomBuilding, string? RoomNumber, string? Fitness);
        private CleanValues _clean = null!;

        public PersonCardViewModel(
            IPersonnelService personnelService, ICompletenessService completenessService,
            IDocumentArchiveService archiveService, IGenerationService generationService,
            IIntakeService intakeService, IDialogService dialogService,
            Services.Generation.IManualTagFormBuilder manualTagFormBuilder,
            PersonEditModel model, string unitDisplay)
        {
            _manualTagFormBuilder = manualTagFormBuilder;
            _personnelService = personnelService;
            _completenessService = completenessService;
            _archiveService = archiveService;
            _generationService = generationService;
            _intakeService = intakeService;
            _dialogService = dialogService;
            Id = model.Id;
            OrgNodeId = model.OrgNodeId;
            IntakeId = model.IntakeId;
            UnitDisplay = unitDisplay;

            lastName = model.LastName;
            firstMiddle = string.Join(' ', new[] { model.FirstName, model.MiddleName }
                .Where(p => !string.IsNullOrWhiteSpace(p)));
            rank = model.Rank;
            position = model.Position;
            serviceNumber = model.ServiceNumber;
            roomBuilding = model.RoomBuilding;
            roomNumber = model.RoomNumber;
            fitness = model.FitnessCategory;

            HeaderName = Id == 0 ? "Нова особа" : BuildShortName(model.LastName, model.FirstName, model.MiddleName);
            HeaderSub = string.Join(" · ", new[] { model.Rank, model.Position }.Where(p => !string.IsNullOrWhiteSpace(p)));

            // Нова особа - картка одразу відкривається в режимі редагування,
            // бо переглядати ще нічого.
            isEditing = IsNew;

            TakeSnapshot();
        }

        public event Action<int>? Saved;
        public event Action? CloseRequested;

        public int Id { get; private set; }
        public int OrgNodeId { get; }
        public int? IntakeId { get; }
        public bool IsNew => Id == 0;
        public string UnitDisplay { get; }
        public string HeaderName { get; }
        public string HeaderSub { get; }

        public IReadOnlyList<string> FitnessList => FitnessOptions;

        [ObservableProperty] private string lastName;
        [ObservableProperty] private string firstMiddle;
        [ObservableProperty] private string rank;
        [ObservableProperty] private string position;
        [ObservableProperty] private string serviceNumber;
        [ObservableProperty] private string? roomBuilding;
        [ObservableProperty] private string? roomNumber;
        [ObservableProperty] private string? fitness;

        [ObservableProperty] private string? lastNameError;
        [ObservableProperty] private string? serviceNumberError;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(IsDataTab))]
        [NotifyPropertyChangedFor(nameof(IsDocumentsTab))]
        private int selectedTabIndex;

        public bool IsDataTab => SelectedTabIndex == 0;
        public bool IsDocumentsTab => SelectedTabIndex == 1;

        public ObservableCollection<RecipientDocRowViewModel> DocumentRows { get; } = new();

        // Групові відомості пакета (1.4): не серед персональних рядків, а в підвалі.
        public ObservableCollection<GroupDocumentRowViewModel> GroupDocumentRows { get; } = new();
        [ObservableProperty] private bool hasGroupDocuments;

        [ObservableProperty] private bool documentsLoading;
        [ObservableProperty] private bool hasMissingDocuments;
        [ObservableProperty] private string? documentsFooterNote;
        [ObservableProperty] private string? documentsEmptyNote;

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(SaveClickCommand))]
        private bool isDirty;

        [ObservableProperty] private bool isEditing;

        protected override void OnPropertyChanged(PropertyChangedEventArgs e)
        {
            base.OnPropertyChanged(e);
            if (e.PropertyName is string name && EditableFields.Contains(name))
                IsDirty = CurrentState() != _snapshot;
        }

        partial void OnSelectedTabIndexChanged(int value)
        {
            if (value == 1) _ = LoadDocumentsAsync();
        }

        private async Task LoadDocumentsAsync()
        {
            if (_documentsLoaded || IsNew) return;
            _documentsLoaded = true;
            await RefreshDocumentsAsync();
        }

        private async Task RefreshDocumentsAsync()
        {
            if (IsNew) return;
            DocumentsLoading = true;
            try
            {
                var packageId = await _completenessService.GetDefaultPackageIdAsync();
                _packageId = packageId;
                if (packageId is null)
                {
                    DocumentRows.Clear();
                    HasMissingDocuments = false;
                    DocumentsFooterNote = null;
                    DocumentsEmptyNote = "Пакет генерації за замовчуванням не налаштовано.";
                    return;
                }

                var statuses = await _completenessService.GetRecipientStatusAsync(Id, packageId.Value);
                DocumentRows.Clear();
                foreach (var status in statuses) DocumentRows.Add(new RecipientDocRowViewModel(status));

                HasMissingDocuments = statuses.Any(s => !s.HasContent);
                DocumentsEmptyNote = statuses.Count == 0 ? "У пакеті немає шаблонів." : null;
                DocumentsFooterNote = await BuildDocumentsFooterNoteAsync(packageId.Value);

                var groupDocs = await _completenessService.GetPackageGroupDocumentsAsync(packageId.Value, IntakeId);
                GroupDocumentRows.Clear();
                foreach (var g in groupDocs) GroupDocumentRows.Add(new GroupDocumentRowViewModel(g));
                HasGroupDocuments = GroupDocumentRows.Count > 0;
            }
            finally
            {
                DocumentsLoading = false;
            }
        }

        private async Task<string> BuildDocumentsFooterNoteAsync(int packageId)
        {
            var packageName = _generationService.GetPackages().FirstOrDefault(p => p.Id == packageId).Name ?? "-";

            if (IntakeId is int intakeId)
            {
                var intake = await _intakeService.GetByIdAsync(intakeId);
                if (intake is not null) return $"Пакет: {packageName} · Набір: {intake.DisplayNumber}";
            }

            return $"Пакет: {packageName}";
        }

        [RelayCommand]
        private async Task OpenDocumentAsync(RecipientDocRowViewModel? row)
        {
            if (row?.DocumentId is not int documentId) return;
            var result = await _archiveService.OpenAsync(documentId);
            if (!result.Success)
                MessageBox.Show(result.ErrorMessage, "Відкриття документа",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        [RelayCommand]
        private async Task OpenGroupDocumentAsync(GroupDocumentRowViewModel? row)
        {
            if (row?.GroupDocumentId is not int id) return;
            var result = await _archiveService.OpenGroupAsync(id);
            if (!result.Success)
                MessageBox.Show(result.ErrorMessage, "Відкриття відомості", MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        [RelayCommand]
        private async Task GenerateDocumentAsync(RecipientDocRowViewModel? row)
        {
            if (row is null) return;

            var manualValues = await CollectManualValuesAsync(new[] { row.TemplateId });
            if (manualValues is null) return;

            var result = await _completenessService.GenerateForPairAsync(Id, row.TemplateId, manualValues);
            if (!result.Success)
            {
                MessageBox.Show(result.ErrorMessage ?? "Не вдалося згенерувати документ.", "Помилка",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }

            await RefreshDocumentsAsync();
        }

        [RelayCommand]
        private async Task GenerateMissingAsync()
        {
            if (_packageId is not int packageId) return;

            var missingTemplateIds = DocumentRows.Where(r => !r.HasContent).Select(r => r.TemplateId).ToList();
            if (missingTemplateIds.Count == 0) return;

            var manualValues = await CollectManualValuesAsync(missingTemplateIds);
            if (manualValues is null) return;

            var (generated, _, errors) = await _completenessService.GenerateMissingForRecipientAsync(Id, packageId, manualValues);
            if (errors.Count > 0)
            {
                MessageBox.Show(string.Join("\n", errors), $"Згенеровано {generated}, помилок {errors.Count}",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }

            await RefreshDocumentsAsync();
        }

        // Повертає null, якщо оператор скасував діалог ручних міток - виклик генерації тоді пропускається.
        private async Task<Dictionary<string, string>?> CollectManualValuesAsync(IReadOnlyList<int> templateIds)
        {
            var manualTags = await _archiveService.GetManualTagsAsync(templateIds);
            if (manualTags.Count == 0) return new Dictionary<string, string>();

            // Та сама форма, що в генерації: дати пікером, тексти з минулого разу.
            var form = await _manualTagFormBuilder.BuildAsync(manualTags, ManualTagContextKey);
            var dialog = new ManualValuesDialogViewModel(form);
            if (_dialogService.ShowDialog(dialog, Application.Current.MainWindow) != true) return null;

            await _manualTagFormBuilder.SaveAsync(ManualTagContextKey, form);
            return dialog.GetValues();
        }

        private string CurrentState()
            => string.Join("|", LastName, FirstMiddle, Rank, Position, ServiceNumber,
                RoomBuilding, RoomNumber, Fitness);

        private void TakeSnapshot()
        {
            _snapshot = CurrentState();
            _clean = new CleanValues(LastName, FirstMiddle, Rank, Position, ServiceNumber, RoomBuilding, RoomNumber, Fitness);
            IsDirty = false;
        }

        private void RevertToClean()
        {
            LastName = _clean.LastName;
            FirstMiddle = _clean.FirstMiddle;
            Rank = _clean.Rank;
            Position = _clean.Position;
            ServiceNumber = _clean.ServiceNumber;
            RoomBuilding = _clean.RoomBuilding;
            RoomNumber = _clean.RoomNumber;
            Fitness = _clean.Fitness;
            LastNameError = null;
            ServiceNumberError = null;
        }

        public async Task<bool> SaveAsync()
        {
            LastNameError = null;
            ServiceNumberError = null;

            var parts = (FirstMiddle ?? string.Empty).Trim()
                .Split(' ', StringSplitOptions.RemoveEmptyEntries);

            var model = new PersonEditModel
            {
                Id = Id,
                LastName = LastName?.Trim() ?? string.Empty,
                FirstName = parts.Length > 0 ? parts[0] : string.Empty,
                MiddleName = parts.Length > 1 ? string.Join(' ', parts.Skip(1)) : null,
                Rank = Rank?.Trim() ?? string.Empty,
                Position = Position?.Trim() ?? string.Empty,
                ServiceNumber = ServiceNumber?.Trim() ?? string.Empty,
                RoomBuilding = RoomBuilding,
                RoomNumber = RoomNumber,
                FitnessCategory = Fitness,
                OrgNodeId = OrgNodeId,
                IntakeId = IntakeId
            };

            var result = await _personnelService.SaveAsync(model);
            if (!result.Success)
            {
                LastNameError = result.Errors.GetValueOrDefault("LastName");
                ServiceNumberError = result.Errors.GetValueOrDefault("ServiceNumber");
                return false;
            }

            Id = result.Id;
            TakeSnapshot();
            IsEditing = false;
            Saved?.Invoke(result.Id);
            return true;
        }

        [RelayCommand(CanExecute = nameof(IsDirty))]
        private Task SaveClickAsync() => SaveAsync();

        [RelayCommand]
        private void SetTab(string index)
        {
            if (int.TryParse(index, out var i)) SelectedTabIndex = i;
        }

        [RelayCommand]
        private void Edit() => IsEditing = true;

        // У режимі редагування - відкат незбережених змін без закриття картки.
        // Поза режимом редагування (не має статись, кнопка ховається) - закрити картку.
        [RelayCommand]
        private void Cancel()
        {
            if (IsEditing)
            {
                RevertToClean();
                IsEditing = false;
                return;
            }

            CloseRequested?.Invoke();
        }

        public static string BuildShortName(string lastName, string? firstName, string? middleName)
        {
            var initials = new[] { firstName, middleName }
                .Where(p => !string.IsNullOrWhiteSpace(p))
                .Select(p => $"{char.ToUpperInvariant(p![0])}.");
            var suffix = string.Join("", initials);
            var last = lastName.ToUpper(System.Globalization.CultureInfo.GetCultureInfo("uk-UA"));
            return suffix.Length > 0 ? $"{last} {suffix}" : last;
        }
    }
}
