using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GenDoc.Services.Personnel;

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
        private string _snapshot = string.Empty;
        private bool _documentsLoaded;

        public PersonCardViewModel(IPersonnelService personnelService, PersonEditModel model, string unitDisplay)
        {
            _personnelService = personnelService;
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

        public ObservableCollection<PersonDocumentItem> Documents { get; } = new();

        [ObservableProperty]
        private bool hasDocuments;

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(SaveClickCommand))]
        private bool isDirty;

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

            var docs = await _personnelService.GetDocumentsAsync(Id);
            Documents.Clear();
            foreach (var doc in docs) Documents.Add(doc);
            HasDocuments = Documents.Count > 0;
        }

        private string CurrentState()
            => string.Join("|", LastName, FirstMiddle, Rank, Position, ServiceNumber,
                RoomBuilding, RoomNumber, Fitness);

        private void TakeSnapshot()
        {
            _snapshot = CurrentState();
            IsDirty = false;
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
        private void Cancel() => CloseRequested?.Invoke();

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
