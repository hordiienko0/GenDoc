using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GenDoc.Services;
using GenDoc.Services.Completeness;
using GenDoc.Services.Documents;
using GenDoc.Services.Generation;
using GenDoc.ViewModels.Archive;
using GenDoc.ViewModels.Generation;

namespace GenDoc.ViewModels.Personnel
{
    public partial class TemplateChoiceItem : ObservableObject
    {
        public TemplateChoiceItem(int id, string name, string group, bool isExport = false)
        {
            Id = id;
            Name = name;
            Group = group;
            IsExport = isExport;
        }

        public int Id { get; }
        public string Name { get; }
        public string Group { get; }
        // Excel-відомість (ExportTemplate) - окрема таблиця з власними Id.
        public bool IsExport { get; }

        [ObservableProperty] private bool isChecked;
    }

    // «Згенерувати документ…» з картки особи або для обраних у списку (2.2):
    // обрати шаблони → ручні поля (наявна форма) → генерація в типову теку → підсумок.
    public partial class GenerateDocumentsDialogViewModel : DialogViewModelBase
    {
        private const string ManualTagContextKey = "generate-documents-dialog";

        private readonly IGenerationService _generationService;
        private readonly ICompletenessService _completenessService;
        private readonly IDocumentArchiveService _archiveService;
        private readonly IManualTagFormBuilder _manualTagFormBuilder;
        private readonly IDialogService _dialogService;
        private readonly IOutputFolderService _outputFolderService;
        private readonly IReadOnlyList<(int Id, string DisplayName)> _recipients;

        public GenerateDocumentsDialogViewModel(
            IGenerationService generationService, ICompletenessService completenessService,
            IDocumentArchiveService archiveService, IManualTagFormBuilder manualTagFormBuilder,
            IDialogService dialogService, IOutputFolderService outputFolderService,
            IReadOnlyList<(int Id, string DisplayName)> recipients)
        {
            _generationService = generationService;
            _completenessService = completenessService;
            _archiveService = archiveService;
            _manualTagFormBuilder = manualTagFormBuilder;
            _dialogService = dialogService;
            _outputFolderService = outputFolderService;
            _recipients = recipients;
            RecipientsSummary = BuildRecipientsSummary(recipients.Select(r => r.DisplayName).ToList());
        }

        public ObservableCollection<TemplateChoiceItem> Templates { get; } = new();
        public string RecipientsSummary { get; }
        public int RecipientCount => _recipients.Count;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(CanGenerate))]
        [NotifyCanExecuteChangedFor(nameof(GenerateCommand))]
        private int selectedCount;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(CanGenerate))]
        [NotifyPropertyChangedFor(nameof(NotBusy))]
        [NotifyCanExecuteChangedFor(nameof(GenerateCommand))]
        private bool isBusy;

        [ObservableProperty] private string progressText = string.Empty;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(HasResult))]
        [NotifyPropertyChangedFor(nameof(IsChoosing))]
        private GenerationResultViewModel? result;

        public bool NotBusy => !IsBusy;
        public bool HasResult => Result is not null;
        public bool IsChoosing => Result is null;
        public bool CanGenerate => SelectedCount > 0 && !IsBusy;
        public string GenerateButtonText => RecipientCount == 1 ? "Згенерувати" : $"Згенерувати для {RecipientCount} осіб";

        public async Task InitializeAsync()
        {
            var all = _generationService.GetPerRecipientTemplates(Models.Enums.TemplateAudience.Intake);
            var packageId = await _completenessService.GetDefaultPackageIdAsync();
            var packageLinks = packageId is int pid
                ? (await _completenessService.GetPackageLinksAsync(pid)).Where(l => !l.IsGroup).ToList()
                : new List<MatrixTemplateInfo>();
            var packageTemplateIds = packageLinks.Select(l => l.TemplateId).ToList();
            // Шаблон типового пакета - у списку завжди, навіть якщо його аудиторія інша:
            // він у пакеті, отже стосується цих людей.
            foreach (var link in packageLinks.Where(l => all.All(t => t.Id != l.TemplateId)))
                all = all.Append((link.TemplateId, link.Name)).ToList();

            // Excel-відомості з тегами - такі ж шаблони для курсового, лише на обраних
            // людей формується один аркуш, а не документ на кожного.
            var exports = _generationService.GetAllExportTemplates();

            Templates.Clear();
            foreach (var item in OrderTemplates(all, packageTemplateIds, exports))
            {
                item.PropertyChanged += OnTemplateChanged;
                Templates.Add(item);
            }
        }

        // Спочатку шаблони типового пакета (у його порядку), далі решта Word за назвою,
        // далі Excel-відомості; групових Word тут нема - GetPerRecipientTemplates
        // повертає лише персональні.
        internal static List<TemplateChoiceItem> OrderTemplates(
            IReadOnlyList<(int Id, string Name)> all, IReadOnlyList<int> defaultPackageTemplateIds,
            IReadOnlyList<(int Id, string Name)>? exportTemplates = null)
        {
            var byId = all.ToDictionary(t => t.Id);
            var result = new List<TemplateChoiceItem>();
            foreach (var id in defaultPackageTemplateIds)
                if (byId.TryGetValue(id, out var t)) result.Add(new TemplateChoiceItem(t.Id, t.Name, "Типовий пакет"));
            var rest = all.Where(t => !defaultPackageTemplateIds.Contains(t.Id))
                .OrderBy(t => t.Name, UkrainianCollation.Surname);
            foreach (var t in rest) result.Add(new TemplateChoiceItem(t.Id, t.Name, "Інші шаблони"));
            foreach (var t in (exportTemplates ?? Array.Empty<(int, string)>()).OrderBy(t => t.Name, UkrainianCollation.Surname))
                result.Add(new TemplateChoiceItem(t.Id, t.Name, "Відомості (Excel) - один аркуш на обраних", isExport: true));
            return result;
        }

        internal static string BuildRecipientsSummary(IReadOnlyList<string> names)
            => names.Count <= 1 ? (names.Count == 1 ? names[0] : "") : $"{names[0]} та ще {names.Count - 1}";

        private void OnTemplateChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(TemplateChoiceItem.IsChecked))
                SelectedCount = Templates.Count(t => t.IsChecked);
        }

        [RelayCommand(CanExecute = nameof(CanGenerate))]
        private async Task GenerateAsync()
        {
            var templateIds = Templates.Where(t => t.IsChecked && !t.IsExport).Select(t => t.Id).ToList();
            var exportTemplateIds = Templates.Where(t => t.IsChecked && t.IsExport).Select(t => t.Id).ToList();
            var manualTags = _generationService.GetManualTagsForTemplates(templateIds, exportTemplateIds);
            var needsCourseOfficer = _generationService.ExportTemplatesNeedCourseOfficer(exportTemplateIds);
            var manualValues = new Dictionary<string, string>();
            int? courseOfficerId = null;
            if (manualTags.Count > 0 || needsCourseOfficer)
            {
                var form = await _manualTagFormBuilder.BuildAsync(manualTags, ManualTagContextKey, needsCourseOfficer);
                var dialog = new ManualValuesDialogViewModel(form);
                if (_dialogService.ShowDialog(dialog, Application.Current.MainWindow) != true) return;
                await _manualTagFormBuilder.SaveAsync(ManualTagContextKey, form);
                manualValues = dialog.GetValues();
                courseOfficerId = form.CourseOfficer?.Selected?.RecipientId;
            }

            var folder = await _outputFolderService.GetDefaultAsync();
            var recipientIds = _recipients.Select(r => r.Id).ToList();
            var progress = new Progress<string>(m => ProgressText = m);

            IsBusy = true;
            try
            {
                var runResult = await Task.Run(() =>
                    _generationService.GenerateTemplatesForRecipients(
                        templateIds, exportTemplateIds, recipientIds, folder, manualValues, progress, courseOfficerId));
                Result = new GenerationResultViewModel(runResult, folder);
            }
            finally
            {
                IsBusy = false;
                ProgressText = string.Empty;
            }
        }

        // Діалог модальний: спершу закриваємо, потім переходимо в архів.
        [RelayCommand]
        private void ShowInArchiveAndClose()
        {
            var result = Result;
            CloseDialog(true);
            result?.ShowInArchiveCommand.Execute(null);
        }

        [RelayCommand] private void Cancel() => CloseDialog(false);
        [RelayCommand] private void Done() => CloseDialog(true);
    }
}
