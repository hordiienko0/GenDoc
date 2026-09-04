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
        private readonly IOutputFolderService _outputFolderService;

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
            IOutputFolderService outputFolderService,
            PersonEditModel model, string unitDisplay)
        {
            _manualTagFormBuilder = manualTagFormBuilder;
            _outputFolderService = outputFolderService;
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

        public string HeaderName => BuildHeaderName(Id, LastName, FirstMiddle);
        public string HeaderSub => BuildHeaderSub(Rank, Position);

        internal static string BuildHeaderName(int id, string? lastName, string? firstMiddle)
        {
            if (id == 0) return "Нова особа";

            var parts = (firstMiddle ?? string.Empty).Trim()
                .Split(' ', StringSplitOptions.RemoveEmptyEntries);

            return BuildShortName(
                lastName ?? string.Empty,
                parts.Length > 0 ? parts[0] : null,
                parts.Length > 1 ? string.Join(' ', parts.Skip(1)) : null);
        }

        internal static string BuildHeaderSub(string? rank, string? position) =>
            string.Join(" · ", new[] { rank, position }.Where(p => !string.IsNullOrWhiteSpace(p)));

        private void RefreshHeader()
        {
            OnPropertyChanged(nameof(HeaderName));
            OnPropertyChanged(nameof(HeaderSub));
        }

        public IReadOnlyList<string> FitnessList => FitnessOptions;

        [ObservableProperty] private string lastName;
        [ObservableProperty] private string firstMiddle;
        [ObservableProperty] private string rank;
        [ObservableProperty] private string position;
        [ObservableProperty] private string serviceNumber;
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(RoomDisplay))]
        private string? roomBuilding;
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(RoomDisplay))]
        private string? roomNumber;

        public string RoomDisplay => FormatRoom(RoomBuilding, RoomNumber);

        internal static string FormatRoom(string? building, string? number)
        {
            var b = building?.Trim();
            var n = number?.Trim();
            if (string.IsNullOrEmpty(b) && string.IsNullOrEmpty(n)) return "-";
            if (string.IsNullOrEmpty(b)) return n!;
            if (string.IsNullOrEmpty(n)) return b;
            return $"{b} / {n}";
        }
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
                var packageId = IntakeId is int intakeId
                    ? await _completenessService.GetDefaultPackageIdAsync(intakeId)
                    : await _completenessService.GetDefaultPackageIdAsync();
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

                DocumentsEmptyNote = statuses.Count == 0 ? "У пакеті немає шаблонів." : null;
                DocumentsFooterNote = await BuildDocumentsFooterNoteAsync(packageId.Value);

                var groupDocs = await _completenessService.GetPackageGroupDocumentsAsync(packageId.Value, IntakeId, Id);
                GroupDocumentRows.Clear();
                foreach (var g in groupDocs) GroupDocumentRows.Add(new GroupDocumentRowViewModel(g));
                HasGroupDocuments = GroupDocumentRows.Count > 0;

                HasMissingDocuments =
                    statuses.Any(s => s.Requirement == Models.Enums.TemplateRequirement.Required && !s.HasContent)
                    || groupDocs.Any(g => !g.IsParticipant);
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
        private async Task PrintDocumentAsync(RecipientDocRowViewModel? row)
        {
            if (row?.DocumentId is not int documentId) return;
            try
            {
                var result = await _archiveService.PrintAsync(documentId);
                if (!result.Success)
                    MessageBox.Show(result.ErrorMessage, "Друк", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            catch (System.ComponentModel.Win32Exception)
            {
                MessageBox.Show("Не вдалося надрукувати: немає програми для цього типу файлу.", "Друк",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        [RelayCommand]
        private async Task GenerateAnyDocumentAsync()
        {
            if (IsNew) return;
            var dialog = new GenerateDocumentsDialogViewModel(
                _generationService, _completenessService, _archiveService, _manualTagFormBuilder,
                _dialogService, _outputFolderService,
                new[] { (Id, HeaderName) });
            _dialogService.ShowDialog(dialog, Application.Current.MainWindow);
            await RefreshDocumentsAsync();
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

        private async Task<Dictionary<string, string>?> CollectManualValuesAsync(IReadOnlyList<int> templateIds)
        {
            var manualTags = await _archiveService.GetManualTagsAsync(templateIds);
            if (manualTags.Count == 0) return new Dictionary<string, string>();

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
            RefreshHeader();
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
