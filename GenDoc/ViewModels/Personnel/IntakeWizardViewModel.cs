using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GenDoc.Models;
using GenDoc.Services.Intakes;

namespace GenDoc.ViewModels.Personnel
{
    public record BaseNodeOption(int Id, string DisplayName);

    /// <summary>Вікно набору: створення нового або редагування наявного (назва, папка-основа, період).</summary>
    public partial class IntakeWizardViewModel : DialogViewModelBase
    {
        private readonly IIntakeService _intakeService;
        private readonly OrgTreeViewModel _tree;
        private Intake? _editing;

        public IntakeWizardViewModel(IIntakeService intakeService, OrgTreeViewModel tree)
        {
            _intakeService = intakeService;
            _tree = tree;
        }

        public ObservableCollection<BaseNodeOption> BaseNodes { get; } = new();

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(CreateCommand))]
        private string displayNumber = string.Empty;

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(CreateCommand))]
        private BaseNodeOption? selectedBaseNode;

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(CreateCommand))]
        private DateTime? dateStart = DateTime.Today;

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(CreateCommand))]
        private DateTime? dateEnd = DateTime.Today.AddMonths(3);

        public bool IsEditMode => _editing is not null;

        public string WindowTitle => IsEditMode ? "Редагувати набір" : "Новий набір";

        public string SubmitText => IsEditMode ? "Зберегти" : "Створити набір";

        public string SummaryCaption => IsEditMode ? "Що оновиться" : "Структура підпапок";

        public string SummaryText => IsEditMode
            ? "Папку набору буде перейменовано (а за нової папки-основи - перенесено разом з підпапками й людьми), "
              + "разом із нею - теку з уже згенерованими документами на диску; статус оновиться за новим періодом "
              + "(закритий набір лишиться закритим). Усі розділи покажуть нові дані."
            : $"Буде створено {Services.Intakes.IntakeFitnessFolders.AllFolderNames.Count + 1} папки: "
              + $"Всі → {string.Join(", ", Services.Intakes.IntakeFitnessFolders.AllFolderNames)}";

        [ObservableProperty]
        private string? errorText;

        public Intake? CreatedIntake { get; private set; }

        public Intake? UpdatedIntake { get; private set; }

        /// <summary>Набір збережено, але з текою документів на диску не все вийшло.</summary>
        public string? SaveWarning { get; private set; }

        public async Task InitializeAsync()
        {
            var number = await _intakeService.GetNextNumberAsync();
            var template = await _intakeService.GetNumberTemplateAsync();
            DisplayNumber = template.Replace("{n}", number.ToString());

            FillBaseNodes();
            SelectedBaseNode = BaseNodes.FirstOrDefault();
        }

        public async Task InitializeForEditAsync(Intake intake)
        {
            _editing = intake;
            OnPropertyChanged(nameof(IsEditMode));
            OnPropertyChanged(nameof(WindowTitle));
            OnPropertyChanged(nameof(SubmitText));
            OnPropertyChanged(nameof(SummaryCaption));
            OnPropertyChanged(nameof(SummaryText));

            DisplayNumber = IntakeLabel.Of(intake.Number, intake.DisplayNumber);
            DateStart = intake.DateStart.ToDateTime(TimeOnly.MinValue);
            DateEnd = intake.DateEnd.ToDateTime(TimeOnly.MinValue);

            FillBaseNodes();
            var baseId = await _intakeService.GetBaseNodeIdAsync(intake.Id);
            SelectedBaseNode = BaseNodes.FirstOrDefault(b => b.Id == baseId);
        }

        private void FillBaseNodes()
        {
            BaseNodes.Clear();
            foreach (var node in Flatten(_tree.RootNodes).Where(n => n.IntakeId is null))
                BaseNodes.Add(new BaseNodeOption(node.Id, $"{new string(' ', node.Depth * 3)}{node.Name}"));
        }

        private static IEnumerable<OrgNodeViewModel> Flatten(IEnumerable<OrgNodeViewModel> nodes)
        {
            foreach (var node in nodes)
            {
                yield return node;
                foreach (var child in Flatten(node.Children))
                    yield return child;
            }
        }

        private bool CanCreate =>
            !string.IsNullOrWhiteSpace(DisplayNumber)
            && SelectedBaseNode is not null
            && DateStart is not null && DateEnd is not null
            && DateEnd >= DateStart;

        [RelayCommand(CanExecute = nameof(CanCreate))]
        private async Task CreateAsync()
        {
            ErrorText = null;

            if (_editing is { } editing)
            {
                try
                {
                    var result = await _intakeService.UpdateAsync(new IntakeUpdateRequest(
                        editing.Id,
                        DisplayNumber,
                        SelectedBaseNode!.Id,
                        DateOnly.FromDateTime(DateStart!.Value),
                        DateOnly.FromDateTime(DateEnd!.Value)));
                    UpdatedIntake = result.Intake;
                    SaveWarning = result.Warning;
                    CloseDialog(true);
                }
                catch (InvalidOperationException ex)
                {
                    ErrorText = ex.Message;
                }
                catch (Exception ex)
                {
                    ErrorText = $"Не вдалося зберегти набір: {ex.Message}";
                }
                return;
            }

            try
            {
                CreatedIntake = await _intakeService.CreateAsync(new IntakeCreateRequest(
                    DisplayNumber,
                    SelectedBaseNode!.Id,
                    DateOnly.FromDateTime(DateStart!.Value),
                    DateOnly.FromDateTime(DateEnd!.Value)));
                CloseDialog(true);
            }
            catch (Exception ex)
            {
                ErrorText = $"Не вдалося створити набір: {ex.Message}";
            }
        }

        [RelayCommand]
        private void Cancel() => CloseDialog(false);
    }
}
