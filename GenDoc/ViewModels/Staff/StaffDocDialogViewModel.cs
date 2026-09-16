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

        public const string NoTemplatesHint =
            "Шаблонів для постійного складу ще немає. Завантажте шаблон у розділі «Шаблони» " +
            "і вкажіть призначення «Постійний склад» - він з'явиться тут.";

        public static string HintFor(StaffEventKind? kind) => kind switch
        {
            null => NoTemplatesHint,
            _ => $"Шаблонів під подію «{StaffEventNaming.For(kind.Value)}» немає. У розділі «Шаблони» "
                 + $"вкажіть шаблону призначення «Постійний склад» і подію «{StaffEventNaming.For(kind.Value)}»."
        };

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(HasNoTemplates))]
        private bool hasTemplates;

        /// <summary>Підказка «шаблонів немає» - своя для кожної події.</summary>
        [ObservableProperty] private string noTemplatesText = NoTemplatesHint;

        public bool HasNoTemplates => !HasTemplates;

        [ObservableProperty]
        private ManualTagFormViewModel? manualTagForm;

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(ConfirmCommand))]
        private DateTime? dateStart;

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(ConfirmCommand))]
        private DateTime? dateEnd;

        /// <summary>Дати вписуються руками; календаря немає.</summary>
        public string DateMask => Common.DateText.Mask;

        [ObservableProperty] private string dateStartText = string.Empty;
        [ObservableProperty] private string dateEndText = string.Empty;

        partial void OnDateStartTextChanged(string value) => DateStart = Common.DateText.Parse(value);

        partial void OnDateEndTextChanged(string value) => DateEnd = Common.DateText.Parse(value);

        [ObservableProperty] private string? note;
        [ObservableProperty] private string? errorText;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(IsFormStep))]
        private bool showResults;

        public bool IsFormStep => !ShowResults;

        [ObservableProperty] private bool isBusy;
        [ObservableProperty] private string resultsSummaryText = string.Empty;

        public ObservableCollection<StaffDocResultRowViewModel> Results { get; } = new();

        public static string HeaderFor(StaffEventKind? kind) =>
            kind is null ? "Згенерувати документи" : StaffEventNaming.ActionFor(kind.Value);

        public void Initialize(
            StaffEventKind? kind, IReadOnlyList<(int Id, string FullName)> people, int? preselectTemplateId = null)
        {
            Kind = kind;
            _people = people;
            ShowDateRange = kind is not null;
            HeaderText = HeaderFor(kind);
            PeopleText = string.Join(", ", people.Select(p => p.FullName));

            Templates.Clear();
            const Models.Enums.TemplateAudience audience = Models.Enums.TemplateAudience.PermanentStaff;

            foreach (var (id, name, _) in _generationService.GetPerRecipientTemplates(audience, kind))
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
            HasTemplates = Templates.Count > 0;
            NoTemplatesText = HintFor(kind);

            DateStartText = Common.DateText.Format(DateTime.Today);
            DateEndText = Common.DateText.Format(DateTime.Today);
            Note = null;
            ErrorText = null;
            ManualTagForm = null;
            ShowResults = false;
            Results.Clear();

            // Позначаємо після скидання: галка сама підтягне форму ручних тегів,
            // а скидання вище її б затерло.
            if (kind is not null)
            {
                // Шаблони події вже відібрані - усі вони й потрібні, вибирати нема з чого.
                foreach (var template in Templates) template.IsChecked = true;
            }
            else if (preselectTemplateId is int preselectId)
            {
                var chosen = Templates.FirstOrDefault(t => t.Id == preselectId);
                if (chosen is not null) chosen.IsChecked = true;
            }
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
                var generatedCount = 0;

                if (Kind is null)
                {
                    generatedCount = await _staffService.GenerateDocumentsAsync(recipientIds, templateIds, manualValues);
                }
                else
                {
                    generatedCount = await _staffService.IssueDocumentsAsync(
                        Kind.Value, recipientIds, templateIds,
                        DateOnly.FromDateTime(DateStart!.Value), DateOnly.FromDateTime(DateEnd!.Value),
                        Note, manualValues);
                    if (generatedCount == 0)
                        ErrorText = "Жодного документа не сформовано - стан «у відрядженні/відпустці» не записано.";
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
