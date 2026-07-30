using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GenDoc.Models.Enums;
using GenDoc.Services.Generation;
using GenDoc.ViewModels.Personnel;

namespace GenDoc.ViewModels.Staff
{
    public partial class TemplateCheckOptionViewModel : ObservableObject
    {
        public TemplateCheckOptionViewModel(int id, string name)
        {
            Id = id;
            Name = name;
        }

        public int Id { get; }
        public string Name { get; }

        [ObservableProperty] private bool isChecked;
    }

    public partial class StaffDocDialogViewModel : DialogViewModelBase
    {
        private readonly IGenerationService _generationService;

        public StaffDocDialogViewModel(IGenerationService generationService)
        {
            _generationService = generationService;
        }

        public StaffEventKind Kind { get; private set; }
        private IReadOnlyList<int> _recipientIds = Array.Empty<int>();

        [ObservableProperty] private string headerText = string.Empty;
        [ObservableProperty] private string peopleText = string.Empty;

        public ObservableCollection<TemplateCheckOptionViewModel> Templates { get; } = new();

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(ConfirmCommand))]
        private DateTime? dateStart;

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(ConfirmCommand))]
        private DateTime? dateEnd;

        [ObservableProperty] private string? note;
        [ObservableProperty] private string? errorText;

        public void Initialize(StaffEventKind kind, IReadOnlyList<(int Id, string FullName)> people)
        {
            Kind = kind;
            _recipientIds = people.Select(p => p.Id).ToList();
            HeaderText = kind == StaffEventKind.BusinessTrip ? "Оформити відрядження" : "Оформити відпустку";
            PeopleText = string.Join(", ", people.Select(p => p.FullName));

            Templates.Clear();
            foreach (var (id, name) in _generationService.GetAllTemplates())
            {
                var item = new TemplateCheckOptionViewModel(id, name);
                item.PropertyChanged += (_, e) =>
                {
                    if (e.PropertyName == nameof(TemplateCheckOptionViewModel.IsChecked))
                        ConfirmCommand.NotifyCanExecuteChanged();
                };
                Templates.Add(item);
            }

            DateStart = DateTime.Today;
            DateEnd = DateTime.Today;
            Note = null;
            ErrorText = null;
        }

        private bool CanConfirm =>
            DateStart is not null && DateEnd is not null && DateEnd >= DateStart
            && Templates.Any(t => t.IsChecked);

        [RelayCommand(CanExecute = nameof(CanConfirm))]
        private void Confirm() => CloseDialog(true);

        [RelayCommand]
        private void Cancel() => CloseDialog(false);

        public (IReadOnlyList<int> RecipientIds, IReadOnlyList<int> TemplateIds, DateOnly DateStart, DateOnly DateEnd, string? Note) BuildRequest()
            => (_recipientIds,
                Templates.Where(t => t.IsChecked).Select(t => t.Id).ToList(),
                DateOnly.FromDateTime(DateStart!.Value),
                DateOnly.FromDateTime(DateEnd!.Value),
                Note);
    }
}
