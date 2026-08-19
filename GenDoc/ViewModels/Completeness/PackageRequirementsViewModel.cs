using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GenDoc.Data;
using GenDoc.Models.Enums;
using GenDoc.Services.Completeness;
using GenDoc.Services.Generation;
using GenDoc.ViewModels.Personnel;
using Microsoft.EntityFrameworkCore;

namespace GenDoc.ViewModels.Completeness
{
    // Пункт випадайки «додати шаблон». Саме тип, а не (int Id, string Name):
    // WPF не вміє DisplayMemberPath="Name" по ValueTuple - імена полів кортежу
    // існують лише на етапі компіляції, у рантаймі це Item1/Item2, та й ті поля,
    // а не властивості. Біндинг мовчки віддавав порожні рядки.
    public record TemplateChoice(int Id, string Name);

    public partial class ExportTemplateLinkRowViewModel : ObservableObject
    {
        public ExportTemplateLinkRowViewModel(int? linkId, int exportTemplateId, string name, int sortOrder, FitnessFilter filter)
        {
            LinkId = linkId;
            ExportTemplateId = exportTemplateId;
            Name = name;
            SortOrder = sortOrder;
            this.filter = filter;
        }

        public int? LinkId { get; }
        public int ExportTemplateId { get; }
        public string Name { get; }
        public int SortOrder { get; set; }

        [ObservableProperty]
        private FitnessFilter filter;
    }

    public partial class RequirementTemplateRowViewModel : ObservableObject
    {
        public RequirementTemplateRowViewModel(MatrixTemplateInfo info, bool hasDocuments)
        {
            // MatrixTemplateInfo.LinkId - не-nullable int, тож щойно доданий шаблон
            // приходить із сентинелом 0 (див. AddTemplateAsync). Тут він мусить стати
            // null, інакше збереження візьме гілку «оновити наявний зв'язок» і піде
            // шукати зв'язок з Id = 0, якого не існує.
            LinkId = info.LinkId == 0 ? null : info.LinkId;
            TemplateId = info.TemplateId;
            Name = info.Name;
            SortOrder = info.SortOrder;
            HasDocuments = hasDocuments;
            IsGroup = info.IsGroup;
            // Груповий шаблон формує один документ на весь склад - обов'язковість на особу до нього не застосовна.
            regular = info.IsGroup ? TemplateRequirement.NotApplicable : info.RequirementRegular;
            limited = info.IsGroup ? TemplateRequirement.NotApplicable : info.RequirementLimited;
        }

        public int? LinkId { get; }
        public int TemplateId { get; }
        public string Name { get; }
        public int SortOrder { get; set; }
        public bool HasDocuments { get; }
        public bool IsGroup { get; }

        [ObservableProperty]
        private TemplateRequirement regular;

        [ObservableProperty]
        private TemplateRequirement limited;
    }

    public partial class PackageRequirementsViewModel : DialogViewModelBase
    {
        private readonly ICompletenessService _completenessService;
        private readonly IGenerationService _generationService;
        private readonly IDbContextFactory<AppDbContext> _dbFactory;

        private int _packageId;
        private List<(int TemplateId, TemplateRequirement Regular, TemplateRequirement Limited)> _snapshot = new();

        public PackageRequirementsViewModel(
            ICompletenessService completenessService, IGenerationService generationService, IDbContextFactory<AppDbContext> dbFactory)
        {
            _completenessService = completenessService;
            _generationService = generationService;
            _dbFactory = dbFactory;
        }

        public string PackageName { get; private set; } = string.Empty;
        public ObservableCollection<RequirementTemplateRowViewModel> Rows { get; } = new();
        public ObservableCollection<TemplateChoice> AvailableTemplates { get; } = new();

        public ObservableCollection<ExportTemplateLinkRowViewModel> ExportRows { get; } = new();
        public ObservableCollection<TemplateChoice> AvailableExportTemplates { get; } = new();

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(CanAddTemplate))]
        private TemplateChoice? selectedTemplateToAdd;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(CanAddExportTemplate))]
        private TemplateChoice? selectedExportTemplateToAdd;

        public bool CanAddTemplate => SelectedTemplateToAdd is not null;
        public bool CanAddExportTemplate => SelectedExportTemplateToAdd is not null;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(HasPreview))]
        private string previewText = string.Empty;

        public bool HasPreview => !string.IsNullOrEmpty(PreviewText);

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(CanSave))]
        [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
        private string? validationError;

        public async Task InitializeAsync(int packageId, int? previewIntakeId)
        {
            _packageId = packageId;
            _previewIntakeId = previewIntakeId;

            using var db = _dbFactory.CreateDbContext();
            PackageName = await db.GenerationPackages.Where(p => p.Id == packageId)
                .Select(p => p.Name).FirstOrDefaultAsync() ?? "-";

            var links = await _completenessService.GetPackageLinksAsync(packageId);
            var withDocs = await _completenessService.GetTemplateIdsWithDocumentsAsync(
                links.Select(l => l.TemplateId).ToList());

            Rows.Clear();
            foreach (var link in links)
                AddRow(new RequirementTemplateRowViewModel(link, withDocs.Contains(link.TemplateId)));

            _snapshot = links.Select(l => (l.TemplateId, l.RequirementRegular, l.RequirementLimited)).ToList();

            ExportRows.Clear();
            foreach (var link in _generationService.GetPackageExportTemplates(packageId))
                ExportRows.Add(new ExportTemplateLinkRowViewModel(link.LinkId, link.ExportTemplateId, link.Name, link.SortOrder, link.FitnessFilter));

            await ReloadAvailableTemplatesAsync();
            await ReloadAvailableExportTemplatesAsync();
            RefreshPreview();
        }

        private int? _previewIntakeId;

        private async Task ReloadAvailableTemplatesAsync()
        {
            var available = await _completenessService.GetTemplatesNotInPackageAsync(_packageId);
            AvailableTemplates.Clear();
            foreach (var t in available) AvailableTemplates.Add(new TemplateChoice(t.Id, t.Name));
            SelectedTemplateToAdd = AvailableTemplates.FirstOrDefault();
        }

        private Task ReloadAvailableExportTemplatesAsync()
        {
            var available = _generationService.GetExportTemplatesNotInPackage(_packageId);
            AvailableExportTemplates.Clear();
            foreach (var t in available) AvailableExportTemplates.Add(new TemplateChoice(t.Id, t.Name));
            SelectedExportTemplateToAdd = AvailableExportTemplates.FirstOrDefault();
            return Task.CompletedTask;
        }

        [RelayCommand]
        private void AddExportTemplate()
        {
            if (SelectedExportTemplateToAdd is not (int id, string name)) return;

            ExportRows.Add(new ExportTemplateLinkRowViewModel(null, id, name, ExportRows.Count, FitnessFilter.All));

            _ = ReloadAvailableExportTemplatesAsync();
            Validate();
            SaveCommand.NotifyCanExecuteChanged();
        }

        [RelayCommand]
        private void RemoveExportTemplate(ExportTemplateLinkRowViewModel? row)
        {
            if (row is null) return;

            ExportRows.Remove(row);
            _ = ReloadAvailableExportTemplatesAsync();
            Validate();
            SaveCommand.NotifyCanExecuteChanged();
        }

        [RelayCommand]
        private void SetFitnessAll(ExportTemplateLinkRowViewModel? row)
        {
            if (row is not null) row.Filter = FitnessFilter.All;
        }

        [RelayCommand]
        private void SetFitnessRegular(ExportTemplateLinkRowViewModel? row)
        {
            if (row is not null) row.Filter = FitnessFilter.RegularOnly;
        }

        [RelayCommand]
        private void SetFitnessLimited(ExportTemplateLinkRowViewModel? row)
        {
            if (row is not null) row.Filter = FitnessFilter.LimitedOnly;
        }

        private void AddRow(RequirementTemplateRowViewModel row)
        {
            row.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName is nameof(RequirementTemplateRowViewModel.Regular)
                    or nameof(RequirementTemplateRowViewModel.Limited))
                {
                    RefreshPreview();
                    Validate();
                }
            };
            Rows.Add(row);
        }

        [RelayCommand]
        private async Task AddTemplateAsync()
        {
            if (SelectedTemplateToAdd is not (int id, string name)) return;

            AddRow(new RequirementTemplateRowViewModel(
                new MatrixTemplateInfo(0, id, name, null, Rows.Count, TemplateRequirement.Required, TemplateRequirement.Required),
                false));

            await ReloadAvailableTemplatesAsync();
            RefreshPreview();
            Validate();
            SaveCommand.NotifyCanExecuteChanged();
        }

        [RelayCommand]
        private void RemoveTemplate(RequirementTemplateRowViewModel? row)
        {
            if (row is null) return;

            if (row.HasDocuments)
            {
                var confirm = System.Windows.MessageBox.Show(
                    "Документи залишаться в архіві, але зникнуть з матриці. Прибрати шаблон з пакета?",
                    "Прибрати шаблон", System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Warning);
                if (confirm != System.Windows.MessageBoxResult.Yes) return;
            }

            Rows.Remove(row);
            _ = ReloadAvailableTemplatesAsync();
            RefreshPreview();
            Validate();
            SaveCommand.NotifyCanExecuteChanged();
        }

        [RelayCommand]
        private void SetRegular(RequirementTemplateRowViewModel? row)
        {
            if (row is not null) row.Regular = TemplateRequirement.Required;
        }

        [RelayCommand]
        private void SetRegularOptional(RequirementTemplateRowViewModel? row)
        {
            if (row is not null) row.Regular = TemplateRequirement.Optional;
        }

        [RelayCommand]
        private void SetRegularNa(RequirementTemplateRowViewModel? row)
        {
            if (row is not null) row.Regular = TemplateRequirement.NotApplicable;
        }

        [RelayCommand]
        private void SetLimitedRequired(RequirementTemplateRowViewModel? row)
        {
            if (row is not null) row.Limited = TemplateRequirement.Required;
        }

        [RelayCommand]
        private void SetLimitedOptional(RequirementTemplateRowViewModel? row)
        {
            if (row is not null) row.Limited = TemplateRequirement.Optional;
        }

        [RelayCommand]
        private void SetLimitedNa(RequirementTemplateRowViewModel? row)
        {
            if (row is not null) row.Limited = TemplateRequirement.NotApplicable;
        }

        [RelayCommand]
        private void MoveUp(RequirementTemplateRowViewModel? row)
        {
            if (row is null) return;
            var index = Rows.IndexOf(row);
            if (index <= 0) return;
            Rows.Move(index, index - 1);
            RenumberSortOrder();
        }

        [RelayCommand]
        private void MoveDown(RequirementTemplateRowViewModel? row)
        {
            if (row is null) return;
            var index = Rows.IndexOf(row);
            if (index < 0 || index >= Rows.Count - 1) return;
            Rows.Move(index, index + 1);
            RenumberSortOrder();
        }

        private void RenumberSortOrder()
        {
            for (var i = 0; i < Rows.Count; i++) Rows[i].SortOrder = i;
        }

        private void RefreshPreview()
        {
            if (_previewIntakeId is null)
            {
                PreviewText = string.Empty;
                return;
            }

            var personal = Rows.Where(r => !r.IsGroup).ToList();
            var requiredRegular = personal.Count(r => r.Regular == TemplateRequirement.Required);
            var requiredLimited = personal.Count(r => r.Limited == TemplateRequirement.Required);
            var optionalRegular = personal.Count(r => r.Regular == TemplateRequirement.Optional);
            var optionalLimited = personal.Count(r => r.Limited == TemplateRequirement.Optional);

            PreviewText = BuildPreviewText(_previewIntakeId.Value, requiredRegular, optionalRegular, requiredLimited, optionalLimited);
        }

        internal static string BuildPreviewText(int intakeNumber, int requiredRegular, int optionalRegular, int requiredLimited, int optionalLimited)
            => $"Для набору №{intakeNumber}: звичайні - {requiredRegular} {Plural(requiredRegular, "обов'язковий", "обов'язкових")}, " +
               $"{optionalRegular} {Plural(optionalRegular, "опційний", "опційних")} · " +
               $"обмежено придатні - {requiredLimited} {Plural(requiredLimited, "обов'язковий", "обов'язкових")}, " +
               $"{optionalLimited} {Plural(optionalLimited, "опційний", "опційних")}";

        private static string Plural(int n, string one, string many) => n == 1 ? one : many;

        private void Validate()
        {
            // Групові рядки (1.4/1.5) вимог на особу не мають - у перевірці не беруть участі.
            var personal = Rows.Where(r => !r.IsGroup).ToList();
            if (personal.Count == 0)
            {
                // Пакет без персональних docx-шаблонів (лише групові відомості) не бере участі
                // в матриці комплектності - вимоги нема до чого застосовувати.
                ValidationError = Rows.Count == 0 && ExportRows.Count == 0
                    ? "Пакет повинен мати хоча б один шаблон"
                    : null;
                return;
            }

            var hasAnyRegular = personal.Any(r => r.Regular != TemplateRequirement.NotApplicable);
            var hasAnyLimited = personal.Any(r => r.Limited != TemplateRequirement.NotApplicable);

            ValidationError = !hasAnyRegular || !hasAnyLimited
                ? "Пакет повинен мати хоча б один шаблон, застосовний до кожної категорії"
                : null;
        }

        public bool CanSave => ValidationError is null && (Rows.Count > 0 || ExportRows.Count > 0);

        [RelayCommand(CanExecute = nameof(CanSave))]
        private async Task SaveAsync()
        {
            Validate();
            if (ValidationError is not null) return;

            var rows = Rows.Select(r => new RequirementRow(
                r.LinkId, r.TemplateId, r.Regular, r.Limited, r.SortOrder)).ToList();
            await _completenessService.SaveRequirementsAsync(_packageId, rows);

            var exportRows = ExportRows.Select((r, i) => (r.LinkId, r.ExportTemplateId, i, r.Filter)).ToList();
            _generationService.SaveExportTemplates(_packageId, exportRows);

            CloseDialog(true);
        }

        [RelayCommand]
        private void Cancel() => CloseDialog(false);
    }
}
