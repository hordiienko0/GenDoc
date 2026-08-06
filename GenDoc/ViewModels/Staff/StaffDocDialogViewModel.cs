using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GenDoc.Models.Enums;
using GenDoc.Services.Documents;
using GenDoc.Services.Generation;
using GenDoc.Services.Staff;
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
        private readonly IStaffService _staffService;

        public StaffDocDialogViewModel(
            IGenerationService generationService, IDocumentArchiveService archiveService,
            IManualTagFormBuilder manualTagFormBuilder, IStaffService staffService)
        {
            _generationService = generationService;
            _archiveService = archiveService;
            _manualTagFormBuilder = manualTagFormBuilder;
            _staffService = staffService;
        }

        public StaffEventKind? Kind { get; private set; }
        private IReadOnlyList<(int Id, string FullName)> _people = Array.Empty<(int, string)>();

        [ObservableProperty] private string headerText = string.Empty;
        [ObservableProperty] private string peopleText = string.Empty;
        [ObservableProperty] private bool showDateRange = true;

        public ObservableCollection<TemplateCheckOptionViewModel> Templates { get; } = new();

        [ObservableProperty]
        private ManualTagFormViewModel? manualTagForm;

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(ConfirmCommand))]
        private DateTime? dateStart;

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(ConfirmCommand))]
        private DateTime? dateEnd;

        [ObservableProperty] private string? note;
        [ObservableProperty] private string? errorText;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(IsFormStep))]
        private bool showResults;

        public bool IsFormStep => !ShowResults;

        [ObservableProperty] private bool isBusy;
        [ObservableProperty] private string resultsSummaryText = string.Empty;

        public ObservableCollection<StaffDocResultRowViewModel> Results { get; } = new();

        public void Initialize(StaffEventKind? kind, IReadOnlyList<(int Id, string FullName)> people)
        {
            Kind = kind;
            _people = people;
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
            ShowResults = false;
            Results.Clear();
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
        private async Task ConfirmAsync()
        {
            IsBusy = true;
            try
            {
                ErrorText = null;
                var templateIds = Templates.Where(t => t.IsChecked).Select(t => t.Id).ToList();
                var manualValues = ManualTagForm?.GetValues() ?? new Dictionary<string, string>();
                var recipientIds = _people.Select(p => p.Id).ToList();

                var generationStartedAt = DateTime.Now;

                if (Kind is null)
                {
                    await _staffService.GenerateDocumentsAsync(recipientIds, templateIds, manualValues);
                }
                else
                {
                    await _staffService.IssueDocumentsAsync(
                        Kind.Value, recipientIds, templateIds,
                        DateOnly.FromDateTime(DateStart!.Value), DateOnly.FromDateTime(DateEnd!.Value),
                        Note, manualValues);
                }

                await SaveManualValuesAsync();

                Results.Clear();
                foreach (var person in _people)
                {
                    foreach (var templateId in templateIds)
                    {
                        var templateName = Templates.First(t => t.Id == templateId).Name;
                        var row = await _archiveService.GetCurrentRowAsync(person.Id, templateId);
                        var succeeded = row is not null && row.CreatedAt >= generationStartedAt;
                        Results.Add(new StaffDocResultRowViewModel(
                            _archiveService, row?.Id, row?.HasContent ?? false, row?.FileName ?? string.Empty,
                            person.FullName, templateName,
                            success: succeeded,
                            errorMessage: succeeded ? null : "Не вдалося згенерувати"));
                    }
                }

                var okCount = Results.Count(r => r.Success);
                var errCount = Results.Count - okCount;
                ResultsSummaryText = $"Згенеровано: {okCount} · помилок: {errCount}";

                ShowResults = true;
            }
            catch (Exception ex)
            {
                ErrorText = ex.Message;
            }
            finally
            {
                IsBusy = false;
            }
        }

        [RelayCommand]
        private void GenerateAnother() => ShowResults = false;

        [RelayCommand]
        private void Finish() => CloseDialog(true);

        [RelayCommand]
        private void Cancel() => CloseDialog(false);

        public async Task SaveManualValuesAsync()
        {
            if (ManualTagForm is null) return;
            var checkedTemplateIds = Templates.Where(t => t.IsChecked).Select(t => t.Id).ToList();
            if (checkedTemplateIds.Count == 0) return;
            await _manualTagFormBuilder.SaveAsync($"tpl:{checkedTemplateIds[0]}", ManualTagForm);
        }
    }
}
