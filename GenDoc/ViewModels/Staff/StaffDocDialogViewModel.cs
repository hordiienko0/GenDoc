using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GenDoc.Models.Enums;
using GenDoc.Services.Documents;
using GenDoc.Services.Generation;
using GenDoc.ViewModels.Generation;
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

    // Kind == null — генерація «в догонку» (без дат, без StaffEvent, лише шаблони PerRecipient).
    public partial class StaffDocDialogViewModel : DialogViewModelBase
    {
        private readonly IGenerationService _generationService;
        private readonly IDocumentArchiveService _archiveService;
        private readonly IManualTagFormBuilder _manualTagFormBuilder;

        public StaffDocDialogViewModel(
            IGenerationService generationService, IDocumentArchiveService archiveService,
            IManualTagFormBuilder manualTagFormBuilder)
        {
            _generationService = generationService;
            _archiveService = archiveService;
            _manualTagFormBuilder = manualTagFormBuilder;
        }

        public StaffEventKind? Kind { get; private set; }
        private IReadOnlyList<int> _recipientIds = Array.Empty<int>();

        [ObservableProperty] private string headerText = string.Empty;
        [ObservableProperty] private string peopleText = string.Empty;
        [ObservableProperty] private bool showDateRange = true;

        public ObservableCollection<TemplateCheckOptionViewModel> Templates { get; } = new();

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(HasManualTags))]
        private ManualTagFormViewModel? manualTagForm;

        public bool HasManualTags => ManualTagForm?.HasContent == true;

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(ConfirmCommand))]
        private DateTime? dateStart;

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(ConfirmCommand))]
        private DateTime? dateEnd;

        [ObservableProperty] private string? note;
        [ObservableProperty] private string? errorText;

        public void Initialize(StaffEventKind? kind, IReadOnlyList<(int Id, string FullName)> people)
        {
            Kind = kind;
            _recipientIds = people.Select(p => p.Id).ToList();
            ShowDateRange = kind is not null;
            HeaderText = kind switch
            {
                StaffEventKind.BusinessTrip => "Оформити відрядження",
                StaffEventKind.Leave => "Оформити відпустку",
                _ => "Згенерувати документи"
            };
            PeopleText = string.Join(", ", people.Select(p => p.FullName));

            Templates.Clear();
            var source = kind is null ? _generationService.GetPerRecipientTemplates() : _generationService.GetAllTemplates();
            foreach (var (id, name) in source)
            {
                var item = new TemplateCheckOptionViewModel(id, name);
                item.PropertyChanged += async (_, e) =>
                {
                    if (e.PropertyName == nameof(TemplateCheckOptionViewModel.IsChecked))
                    {
                        ConfirmCommand.NotifyCanExecuteChanged();
                        await RefreshManualTagFormAsync();
                    }
                };
                Templates.Add(item);
            }

            DateStart = DateTime.Today;
            DateEnd = DateTime.Today;
            Note = null;
            ErrorText = null;
            ManualTagForm = null;
        }

        private async Task RefreshManualTagFormAsync()
        {
            var checkedTemplateIds = Templates.Where(t => t.IsChecked).Select(t => t.Id).ToList();
            if (checkedTemplateIds.Count == 0)
            {
                ManualTagForm = null;
                return;
            }

            var tags = await _archiveService.GetManualTagsAsync(checkedTemplateIds);
            ManualTagForm = tags.Count > 0
                ? await _manualTagFormBuilder.BuildAsync(tags, $"tpl:{checkedTemplateIds[0]}")
                : null;
        }

        private bool CanConfirm =>
            (Kind is null || (DateStart is not null && DateEnd is not null && DateEnd >= DateStart))
            && Templates.Any(t => t.IsChecked);

        [RelayCommand(CanExecute = nameof(CanConfirm))]
        private void Confirm() => CloseDialog(true);

        [RelayCommand]
        private void Cancel() => CloseDialog(false);

        public async Task SaveManualValuesAsync()
        {
            if (ManualTagForm is null) return;
            var checkedTemplateIds = Templates.Where(t => t.IsChecked).Select(t => t.Id).ToList();
            if (checkedTemplateIds.Count == 0) return;
            await _manualTagFormBuilder.SaveAsync($"tpl:{checkedTemplateIds[0]}", ManualTagForm);
        }

        public (IReadOnlyList<int> RecipientIds, IReadOnlyList<int> TemplateIds, DateOnly DateStart, DateOnly DateEnd, string? Note, Dictionary<string, string> ManualValues) BuildRequest()
            => (_recipientIds,
                Templates.Where(t => t.IsChecked).Select(t => t.Id).ToList(),
                DateOnly.FromDateTime(DateStart!.Value),
                DateOnly.FromDateTime(DateEnd!.Value),
                Note,
                ManualTagForm?.GetValues() ?? new Dictionary<string, string>());
    }
}
