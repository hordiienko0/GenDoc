using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GenDoc.Services.Import;
using Microsoft.Win32;

namespace GenDoc.ViewModels.Import;

public partial class ImportViewModel : ObservableObject
{
    private readonly IImportService _importService;
    private ImportParseResult? _parsed;

    public ImportViewModel(IImportService importService)
    {
        _importService = importService;
        Reset();
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NoFile))]
    private bool hasFile;

    public bool NoFile => !HasFile;

    [ObservableProperty]
    private string fileName = string.Empty;

    [ObservableProperty]
    private int totalRows;

    [ObservableProperty]
    private int readyCount;

    [ObservableProperty]
    private int issueCount;

    [ObservableProperty]
    private int currentStep = 1;

    [ObservableProperty]
    private ObservableCollection<ImportColumnViewModel> columns = new();

    [ObservableProperty]
    private ObservableCollection<ImportRowPreview> preview = new();

    [ObservableProperty]
    private string previewHeaderText = string.Empty;

    [ObservableProperty]
    private string importButtonText = "Імпортувати 0 записів";

    [ObservableProperty]
    private string skippedNoteText = string.Empty;

    [ObservableProperty]
    private bool hasSkippedRows;

    [ObservableProperty]
    private bool importAsPermanentStaff;

    [RelayCommand]
    private void PickFile()
    {
        var dialog = new OpenFileDialog { Filter = "Excel файли (*.xlsx)|*.xlsx" };
        if (dialog.ShowDialog() != true) return;

        LoadFile(dialog.FileName);
    }

    [RelayCommand]
    private void PickAnotherFile() => PickFile();

    [RelayCommand(CanExecute = nameof(CanRunImport))]
    private void RunImport()
    {
        if (_parsed is null) return;

        var confirm = MessageBox.Show(
            $"Імпортувати {ReadyCount} записів?", "Підтвердження імпорту",
            MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (confirm != MessageBoxResult.Yes) return;

        var summary = _importService.Import(_parsed, ImportAsPermanentStaff);

        var message = $"Імпортовано {summary.Imported}, пропущено {summary.Skipped}, помилок {summary.Errors}";
        if (summary.ErrorMessages.Count > 0)
        {
            message += "\n\nПричини:\n" + string.Join("\n", summary.ErrorMessages.Take(3));
            if (summary.ErrorMessages.Count > 3)
                message += $"\n…та ще {summary.ErrorMessages.Count - 3}";
        }

        MessageBox.Show(message, "Імпорт завершено", MessageBoxButton.OK,
            summary.Errors > 0 ? MessageBoxImage.Warning : MessageBoxImage.Information);

        Reset();
    }

    private bool CanRunImport() => HasFile && ReadyCount > 0;

    private void LoadFile(string filePath)
    {
        _parsed = _importService.ParseFile(filePath);
        FileName = Path.GetFileName(filePath);
        HasFile = true;
        CurrentStep = 2;

        foreach (var existing in Columns)
            existing.MappingChanged -= OnColumnMappingChanged;

        var newColumns = new ObservableCollection<ImportColumnViewModel>();
        foreach (var column in _parsed.Columns)
        {
            var vm = new ImportColumnViewModel(column);
            vm.MappingChanged += OnColumnMappingChanged;
            newColumns.Add(vm);
        }
        Columns = newColumns;

        RecalculateValidation();
    }

    private void OnColumnMappingChanged(object? sender, EventArgs e) => RecalculateValidation();

    private void RecalculateValidation()
    {
        if (_parsed is null) return;

        var rows = _importService.Validate(_parsed);
        TotalRows = _parsed.TotalRows;
        ReadyCount = rows.Count(r => r.Status is ImportRowStatus.Ok or ImportRowStatus.Warning);
        IssueCount = rows.Count(r => r.Status is ImportRowStatus.Error or ImportRowStatus.Duplicate);

        Preview = new ObservableCollection<ImportRowPreview>(rows.Take(8));
        PreviewHeaderText = $"Попередній перегляд ({Preview.Count} з {TotalRows} рядків)";

        ImportButtonText = $"Імпортувати {ReadyCount} записів";
        HasSkippedRows = IssueCount > 0;
        SkippedNoteText = IssueCount > 0 ? $"{IssueCount} рядків буде пропущено — причини вказані вище" : string.Empty;

        RunImportCommand.NotifyCanExecuteChanged();
    }

    private void Reset()
    {
        _parsed = null;
        HasFile = false;
        CurrentStep = 1;
        FileName = string.Empty;
        TotalRows = 0;
        ReadyCount = 0;
        IssueCount = 0;
        Columns = new ObservableCollection<ImportColumnViewModel>();
        Preview = new ObservableCollection<ImportRowPreview>();
        PreviewHeaderText = string.Empty;
        ImportButtonText = "Імпортувати 0 записів";
        SkippedNoteText = string.Empty;
        HasSkippedRows = false;
        RunImportCommand.NotifyCanExecuteChanged();
    }
}
