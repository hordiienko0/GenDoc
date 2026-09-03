using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GenDoc.Models.Enums;
using GenDoc.Models.TemplateBuilder;
using GenDoc.Services.Templates;
using Microsoft.Win32;

namespace GenDoc.ViewModels.Templates.Builder;

public record BlockPaletteItem(TemplateBlockKind Kind, string Icon, string Label);

public partial class SheetTabViewModel : ObservableObject
{
    public SheetTabViewModel(int index, string name)
    {
        Index = index;
        this.name = name;
    }

    public int Index { get; }

    [ObservableProperty]
    private string name;

    [ObservableProperty]
    private bool isCurrent;
}

public class PreviewStyleViewModel
{
    public const string FallbackFont = "Times New Roman";
    public const double FallbackSize = 11;

    private const double PointsToPixels = 96.0 / 72.0;

    private static readonly SolidColorBrush DefaultForeground = CreateFrozen(Color.FromRgb(0x11, 0x11, 0x11));

    public PreviewStyleViewModel(ResolvedBlockStyle style)
    {
        Family = new FontFamily(style.FontFamily is { Length: > 0 } font ? font : FallbackFont);
        Size = (style.FontSize ?? FallbackSize) * PointsToPixels;
        Weight = style.Bold ? FontWeights.Bold : FontWeights.Normal;
        Slant = style.Italic ? FontStyles.Italic : FontStyles.Normal;
        Foreground = Parse(style.Color);
        Alignment = style.Alignment switch
        {
            BlockAlignment.Center => TextAlignment.Center,
            BlockAlignment.Right => TextAlignment.Right,
            BlockAlignment.Justify => TextAlignment.Justify,
            _ => TextAlignment.Left
        };
    }

    public FontFamily Family { get; }
    public double Size { get; }
    public FontWeight Weight { get; }
    public FontStyle Slant { get; }
    public Brush Foreground { get; }
    public TextAlignment Alignment { get; }

    private static Brush Parse(string? hex)
    {
        if (string.IsNullOrWhiteSpace(hex)) return DefaultForeground;

        try
        {
            return CreateFrozen((Color)ColorConverter.ConvertFromString($"#{hex}"));
        }
        catch (FormatException)
        {
            return DefaultForeground;
        }
    }

    private static SolidColorBrush CreateFrozen(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
}

public class PreviewLineViewModel
{
    public PreviewLineViewModel(PreviewLine line)
    {
        Runs = line.Runs;
        Style = new PreviewStyleViewModel(line.Style);
    }

    public IReadOnlyList<PreviewRun> Runs { get; }
    public PreviewStyleViewModel Style { get; }
}

public class SheetCellViewModel
{
    public SheetCellViewModel(IReadOnlyList<PreviewRun> runs, double width)
    {
        Runs = runs;
        Width = width;
    }

    public IReadOnlyList<PreviewRun> Runs { get; }
    public double Width { get; }
}

public class SheetRowViewModel
{
    private static readonly IReadOnlyList<PreviewRun> NoRuns = Array.Empty<PreviewRun>();

    private static readonly PreviewStyleViewModel FillerStyle =
        new(BlockStyleDefaults.Resolve(TemplateBlockKind.Paragraph, null));

    public SheetRowViewModel(SheetPreviewRow row, int dataColumnCount, int columnCount, double cellWidth)
    {
        Number = row.Number;
        IsMerged = row.IsMerged;
        IsTableHeader = row.IsTableHeader;
        IsTemplateRow = row.IsTemplateRow;
        Style = new PreviewStyleViewModel(row.Style);

        var cells = new List<SheetCellViewModel>(columnCount);

        if (row.IsMerged)
        {
            var span = Math.Max(dataColumnCount, 1);
            cells.Add(new SheetCellViewModel(row.Cells.Count > 0 ? row.Cells[0] : NoRuns, span * cellWidth));
            for (var i = span; i < columnCount; i++) cells.Add(new SheetCellViewModel(NoRuns, cellWidth));
        }
        else
        {
            foreach (var cell in row.Cells) cells.Add(new SheetCellViewModel(cell, cellWidth));
            for (var i = row.Cells.Count; i < columnCount; i++) cells.Add(new SheetCellViewModel(NoRuns, cellWidth));
        }

        Cells = cells;
    }

    public SheetRowViewModel(int number, int columnCount, double cellWidth)
    {
        Number = number;
        Style = FillerStyle;
        Cells = Enumerable.Range(0, columnCount)
            .Select(_ => new SheetCellViewModel(NoRuns, cellWidth))
            .ToList();
    }

    public int Number { get; }
    public bool IsMerged { get; }
    public bool IsTableHeader { get; }
    public bool IsTemplateRow { get; }
    public IReadOnlyList<SheetCellViewModel> Cells { get; }
    public PreviewStyleViewModel Style { get; }
}

public class PreviewTableViewModel
{
    public PreviewTableViewModel(PreviewTable table)
    {
        Headers = table.Headers;
        Cells = table.Cells;
        Style = new PreviewStyleViewModel(table.Style);
        HeaderStyle = new PreviewStyleViewModel(
            BlockStyleDefaults.ForTableHeader(table.Style, BlockAlignment.Left));
    }

    public IReadOnlyList<string> Headers { get; }
    public IReadOnlyList<IReadOnlyList<PreviewRun>> Cells { get; }
    public PreviewStyleViewModel Style { get; }
    public PreviewStyleViewModel HeaderStyle { get; }
}

public partial class TemplateBuilderViewModel : ObservableObject
{
    private readonly ITemplateBuilderService _builderService;

    public TemplateBuilderViewModel(ITemplateBuilderService builderService)
    {
        _builderService = builderService;

        Signatories = _builderService.GetSignatories();
        FieldGroups = TemplateFieldPalette.Build();
        TestPeople = new ObservableCollection<BuilderTestPerson>(_builderService.GetTestPeople());
        selectedTestPerson = TestPeople.FirstOrDefault();

        Blocks.CollectionChanged += OnBlocksChanged;
    }

    private static readonly IReadOnlyList<BlockPaletteItem> WordPalette = new[]
    {
        new BlockPaletteItem(TemplateBlockKind.Header, "▤", "Шапка (гриф)"),
        new BlockPaletteItem(TemplateBlockKind.Title, "H", "Заголовок"),
        new BlockPaletteItem(TemplateBlockKind.Paragraph, "¶", "Абзац"),
        new BlockPaletteItem(TemplateBlockKind.Table, "▦", "Таблиця"),
        new BlockPaletteItem(TemplateBlockKind.Signatures, "≡", "Підписи"),
        new BlockPaletteItem(TemplateBlockKind.DateAndCity, "◫", "Дата і місто")
    };

    private static readonly IReadOnlyList<BlockPaletteItem> ExcelPalette = new[]
    {
        new BlockPaletteItem(TemplateBlockKind.Title, "H", "Заголовок"),
        new BlockPaletteItem(TemplateBlockKind.Paragraph, "¶", "Рядок тексту"),
        new BlockPaletteItem(TemplateBlockKind.Table, "▦", "Таблиця"),
        new BlockPaletteItem(TemplateBlockKind.Signatures, "≡", "Підписи")
    };

    public IReadOnlyList<BlockPaletteItem> BlockPalette
        => Mode == TemplateBuilderMode.Excel ? ExcelPalette : WordPalette;

    public BlockPaletteItem ParagraphPaletteItem
        => BlockPalette.First(b => b.Kind == TemplateBlockKind.Paragraph);

    public IReadOnlyList<PaletteGroup> FieldGroups { get; }

    public IReadOnlyList<BuilderSignatory> Signatories { get; }

    public ObservableCollection<BuilderBlockViewModel> Blocks { get; } = new();

    public ObservableCollection<BuilderBlockViewModel> VisibleBlocks { get; } = new();

    public ObservableCollection<SheetTabViewModel> Sheets { get; } = new();

    [ObservableProperty]
    private string? sheetRangeCaption;

    public ObservableCollection<BuilderTestPerson> TestPeople { get; }

    public ObservableCollection<object> PreviewItems { get; } = new();

    public ObservableCollection<SheetRowViewModel> SheetRows { get; } = new();

    public ObservableCollection<string> SheetColumnLetters { get; } = new();

    public const double SheetCellWidth = 96;
    public const double SheetRowHeight = 26;
    public const double SheetRowHeaderWidth = 32;

    private SheetPreview? _sheet;
    private double _sheetViewportWidth;
    private double _sheetViewportHeight;

    public void SetSheetViewport(double width, double height)
    {
        if (Math.Abs(width - _sheetViewportWidth) < 0.5 && Math.Abs(height - _sheetViewportHeight) < 0.5) return;

        _sheetViewportWidth = width;
        _sheetViewportHeight = height;
        RebuildSheetGrid();
    }

    private void RebuildSheetGrid()
    {
        SheetRows.Clear();
        SheetColumnLetters.Clear();

        if (_sheet is null) return;

        var dataColumnCount = _sheet.ColumnLetters.Count;
        var visibleColumns = (int)Math.Ceiling(Math.Max(_sheetViewportWidth - SheetRowHeaderWidth, 0) / SheetCellWidth);
        var columnCount = Math.Max(dataColumnCount, visibleColumns);

        for (var i = 1; i <= columnCount; i++)
            SheetColumnLetters.Add(TemplateSheetLayout.ColumnLetter(i));

        var lastNumber = 0;
        foreach (var row in _sheet.Rows)
        {
            SheetRows.Add(new SheetRowViewModel(row, dataColumnCount, columnCount, SheetCellWidth));
            lastNumber = Math.Max(lastNumber, row.Number);
        }

        var visibleRows = (int)Math.Ceiling(Math.Max(_sheetViewportHeight - SheetRowHeight, 0) / SheetRowHeight);
        for (var number = lastNumber + 1; number <= visibleRows; number++)
            SheetRows.Add(new SheetRowViewModel(number, columnCount, SheetCellWidth));
    }

    [ObservableProperty]
    private BuilderTestPerson? selectedTestPerson;

    [ObservableProperty]
    private string templateName = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(BlockPalette))]
    [NotifyPropertyChangedFor(nameof(ParagraphPaletteItem))]
    [NotifyPropertyChangedFor(nameof(IsWordMode))]
    [NotifyPropertyChangedFor(nameof(IsExcelMode))]
    [NotifyPropertyChangedFor(nameof(CanSwitchMode))]
    private TemplateBuilderMode mode = TemplateBuilderMode.Word;

    [ObservableProperty]
    private int currentSheetIndex;

    public bool IsWordMode => Mode == TemplateBuilderMode.Word;

    public bool IsExcelMode => Mode == TemplateBuilderMode.Excel;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PreviewPanelWidth))]
    [NotifyPropertyChangedFor(nameof(PreviewPageWidth))]
    [NotifyPropertyChangedFor(nameof(PreviewTextWidth))]
    private double previewWidth = 340;

    public GridLength PreviewPanelWidth => new(PreviewWidth);

    private const double PreviewChrome = 40;

    private const double PreviewPagePadding = 48;

    public double PreviewPageWidth => Math.Max(PreviewWidth - PreviewChrome, 160);

    public double PreviewTextWidth => Math.Max(PreviewPageWidth - PreviewPagePadding, 120);

    public const double PreviewMinWidth = 260;
    public const double PreviewMaxWidth = 900;

    public bool CanSwitchMode => EditingTemplateId is null;

    [ObservableProperty]
    private bool repeatSheetPerDate;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSwitchMode))]
    private int? editingTemplateId;

    [ObservableProperty]
    private string? statusMessage;

    public event Action? RequestClose;

    public event Action? Saved;

    private string _snapshot = string.Empty;

    public bool IsDirty => CurrentSnapshot() != _snapshot;

    private string CurrentSnapshot()
        => TemplateName.Trim() + " " + TemplateBuilderJson.Serialize(ToDocument());

    private void TakeSnapshot() => _snapshot = CurrentSnapshot();

    public void StartNew(TemplateBuilderMode mode = TemplateBuilderMode.Word)
    {
        EditingTemplateId = null;
        TemplateName = string.Empty;
        Mode = mode;
        RepeatSheetPerDate = false;

        Sheets.Clear();
        Sheets.Add(new SheetTabViewModel(0, TemplateBuilderDocument.DefaultSheetName));
        CurrentSheetIndex = 0;

        Blocks.Clear();
        Blocks.Add(BuilderBlockViewModel.CreateNew(TemplateBlockKind.Title, Signatories, mode));
        Blocks.Add(BuilderBlockViewModel.CreateNew(
            mode == TemplateBuilderMode.Excel ? TemplateBlockKind.Table : TemplateBlockKind.Paragraph,
            Signatories, mode));

        StatusMessage = null;
        TakeSnapshot();
        RefreshPreview();
    }

    public bool LoadTemplate(int templateId, TemplateBuilderMode mode)
    {
        var source = _builderService.Load(templateId, mode);
        if (source is null) return false;

        EditingTemplateId = source.Id;
        TemplateName = source.Name;
        Mode = source.Document.Mode;
        RepeatSheetPerDate = source.Document.RepeatSheetPerDate;

        Sheets.Clear();
        var names = source.Document.ResolvedSheetNames();
        for (var i = 0; i < names.Count; i++) Sheets.Add(new SheetTabViewModel(i, names[i]));
        CurrentSheetIndex = 0;

        Blocks.Clear();
        foreach (var block in source.Document.Blocks)
            Blocks.Add(BuilderBlockViewModel.FromBlock(block, Signatories, source.Document.Mode));

        StatusMessage = null;
        TakeSnapshot();
        RefreshPreview();
        return true;
    }

    [RelayCommand]
    private void SwitchMode(string? modeName)
    {
        if (!Enum.TryParse<TemplateBuilderMode>(modeName, out var target) || target == Mode) return;
        if (!CanSwitchMode) return;

        var hasContent = Blocks.Any(b => !string.IsNullOrWhiteSpace(b.Text) || b.Signatures.Count > 0 || b.Columns.Count > 0);
        if (hasContent)
        {
            var confirm = MessageBox.Show(
                "Набір блоків у документі Word і у відомості Excel різний, тому складання почнеться заново. Продовжити?",
                "Змінити режим", MessageBoxButton.YesNo, MessageBoxImage.Question);

            if (confirm != MessageBoxResult.Yes) return;
        }

        var name = TemplateName;
        StartNew(target);
        TemplateName = name;
    }

    partial void OnSelectedTestPersonChanged(BuilderTestPerson? value) => RefreshPreview();

    partial void OnModeChanged(TemplateBuilderMode value)
    {
        OnPropertyChanged(nameof(ExportButtonText));
        OnPropertyChanged(nameof(OpenExternallyButtonText));
        OnPropertyChanged(nameof(SubtitleText));

        PreviewWidth = value == TemplateBuilderMode.Excel ? 420 : 340;
    }

    partial void OnRepeatSheetPerDateChanged(bool value) => RefreshPreview();

    [RelayCommand]
    private void AddBlock(BlockPaletteItem? item)
    {
        if (item is null) return;

        var block = BuilderBlockViewModel.CreateNew(item.Kind, Signatories, Mode);
        block.SheetIndex = CurrentSheetIndex;
        Blocks.Add(block);
        SetEditing(block);
    }

    [RelayCommand]
    private void AddSheet()
    {
        var index = Sheets.Count;
        Sheets.Add(new SheetTabViewModel(index, $"{TemplateBuilderDocument.DefaultSheetName} {index + 1}"));
        CurrentSheetIndex = index;
    }

    [RelayCommand]
    private void SelectSheet(SheetTabViewModel? sheet)
    {
        if (sheet is null) return;
        CurrentSheetIndex = sheet.Index;
    }

    [RelayCommand]
    private void RemoveLastSheet()
    {
        if (Sheets.Count < 2) return;

        var index = Sheets.Count - 1;
        var doomed = Blocks.Where(b => b.SheetIndex == index).ToList();

        if (doomed.Count > 0)
        {
            var confirm = MessageBox.Show(
                $"Аркуш «{Sheets[index].Name}» містить {doomed.Count} блок(ів). Видалити разом з ними?",
                "Видалити аркуш", MessageBoxButton.YesNo, MessageBoxImage.Warning);

            if (confirm != MessageBoxResult.Yes) return;
        }

        foreach (var block in doomed) Blocks.Remove(block);
        Sheets.RemoveAt(index);
        CurrentSheetIndex = Sheets.Count - 1;
    }

    public string CurrentSheetName
    {
        get => CurrentSheetIndex < Sheets.Count ? Sheets[CurrentSheetIndex].Name : string.Empty;
        set
        {
            if (CurrentSheetIndex >= Sheets.Count) return;
            if (Sheets[CurrentSheetIndex].Name == value) return;

            Sheets[CurrentSheetIndex].Name = value;
            OnPropertyChanged();
            RefreshSheetLayout();
        }
    }

    partial void OnCurrentSheetIndexChanged(int value)
    {
        foreach (var sheet in Sheets) sheet.IsCurrent = sheet.Index == value;
        OnPropertyChanged(nameof(CurrentSheetName));
        RefreshVisibleBlocks();
        RefreshPreview();
    }

    private void RefreshVisibleBlocks()
    {
        VisibleBlocks.Clear();
        foreach (var block in Blocks.Where(b => b.SheetIndex == CurrentSheetIndex))
            VisibleBlocks.Add(block);
    }

    [RelayCommand]
    private void RemoveBlock(BuilderBlockViewModel? block)
    {
        if (block is null) return;
        Blocks.Remove(block);
    }

    [RelayCommand]
    private void MoveBlockUp(BuilderBlockViewModel? block)
    {
        if (block is null) return;
        var index = Blocks.IndexOf(block);
        if (index > 0) Blocks.Move(index, index - 1);
    }

    [RelayCommand]
    private void MoveBlockDown(BuilderBlockViewModel? block)
    {
        if (block is null) return;
        var index = Blocks.IndexOf(block);
        if (index >= 0 && index < Blocks.Count - 1) Blocks.Move(index, index + 1);
    }

    [RelayCommand]
    private void AddSignatureLine(BuilderBlockViewModel? block)
    {
        if (block is null) return;
        var line = new SignatureLineViewModel(string.Empty, null, Signatories);
        line.PropertyChanged += OnChildChanged;
        block.Signatures.Add(line);
        RefreshPreview();
    }

    [RelayCommand]
    private void RemoveSignatureLine(SignatureLineViewModel? line)
    {
        if (line is null) return;

        foreach (var block in Blocks)
        {
            if (!block.Signatures.Remove(line)) continue;
            line.PropertyChanged -= OnChildChanged;
            RefreshPreview();
            return;
        }
    }

    [RelayCommand]
    private void AddColumn(BuilderBlockViewModel? block)
    {
        if (block is null) return;
        var column = new TableColumnViewModel(string.Empty, string.Empty);
        column.PropertyChanged += OnChildChanged;
        block.Columns.Add(column);
        RefreshPreview();
    }

    [RelayCommand]
    private void RemoveColumn(TableColumnViewModel? column)
    {
        if (column is null) return;

        foreach (var block in Blocks)
        {
            if (!block.Columns.Remove(column)) continue;
            column.PropertyChanged -= OnChildChanged;
            RefreshPreview();
            return;
        }
    }

    [RelayCommand]
    private void SelectBlock(BuilderBlockViewModel? block)
    {
        if (block is null) return;
        SetEditing(block);
    }

    [RelayCommand]
    private void Save()
    {
        if (!Validate()) return;

        try
        {
            EditingTemplateId = _builderService.Save(EditingTemplateId, TemplateName.Trim(), ToDocument());
            TakeSnapshot();
            StatusMessage = $"Шаблон збережено ({DateTime.Now:HH:mm}).";
            Saved?.Invoke();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Не вдалося зберегти шаблон", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    public string ExportButtonText => IsExcelMode ? "Експорт у xlsx" : "Експорт у docx";

    public string SubtitleText => IsExcelMode
        ? "Складання відомості з блоків без ручного редагування .xlsx"
        : "Складання документа з блоків без ручного редагування .docx";

    [RelayCommand]
    private void Export()
    {
        if (!Validate()) return;

        var extension = IsExcelMode ? ".xlsx" : ".docx";
        var dialog = new SaveFileDialog
        {
            Filter = IsExcelMode ? "Книга Excel (*.xlsx)|*.xlsx" : "Документ Word (*.docx)|*.docx",
            FileName = $"{TemplateName.Trim()}{extension}"
        };
        if (dialog.ShowDialog() != true) return;

        try
        {
            var document = ToDocument();
            var bytes = IsExcelMode
                ? _builderService.BuildXlsx(document).Content
                : _builderService.BuildDocx(document);

            File.WriteAllBytes(dialog.FileName, bytes);
            StatusMessage = $"Вивантажено: {Path.GetFileName(dialog.FileName)}";
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Не вдалося вивантажити файл", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    public string OpenExternallyButtonText => IsExcelMode ? "Перегляд у Excel" : "Перегляд у Word";

    [RelayCommand]
    private void OpenExternally()
    {
        if (!Validate()) return;

        try
        {
            var document = ToDocument();
            var extension = IsExcelMode ? ".xlsx" : ".docx";
            var bytes = IsExcelMode
                ? _builderService.BuildXlsx(document).Content
                : _builderService.BuildDocx(document);

            var name = string.IsNullOrWhiteSpace(TemplateName) ? "Перегляд" : TemplateName.Trim();
            var path = Path.Combine(
                Path.GetTempPath(), $"GenDoc_{SafeFileName(name)}_{DateTime.Now:HHmmss}{extension}");

            File.WriteAllBytes(path, bytes);

            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });

            StatusMessage = "Відкрито для перегляду; правки у файлі назад у шаблон не повертаються.";
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Не вдалося відкрити перегляд", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private static string SafeFileName(string name)
        => new(name.Where(c => !Path.GetInvalidFileNameChars().Contains(c)).ToArray());

    [RelayCommand]
    private void Close()
    {
        if (!TryLeave()) return;
        RequestClose?.Invoke();
    }

    public bool TryLeave()
    {
        if (!IsDirty) return true;

        var result = MessageBox.Show(
            "Зберегти зміни в шаблоні?", "Незбережені зміни",
            MessageBoxButton.YesNoCancel, MessageBoxImage.Question);

        switch (result)
        {
            case MessageBoxResult.Yes:
                Save();
                return !IsDirty;
            case MessageBoxResult.No:
                return true;
            default:
                return false;
        }
    }

    public TemplateBuilderDocument ToDocument()
        => new(Blocks.Select(b => b.ToBlock()).ToList(),
            Mode: Mode,
            RepeatSheetPerDate: IsExcelMode && RepeatSheetPerDate,
            SheetNames: Sheets.Select(s => s.Name).ToList());

    private bool Validate()
    {
        if (string.IsNullOrWhiteSpace(TemplateName))
        {
            MessageBox.Show("Вкажіть назву шаблону.", "Немає назви", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }

        if (Blocks.Count == 0)
        {
            MessageBox.Show("Додайте хоча б один блок.", "Порожній шаблон", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }

        if (!IsExcelMode) return true;

        var tables = Blocks.Where(b => b.IsTable).ToList();
        if (tables.Count == 0)
        {
            MessageBox.Show(
                "У відомості має бути блок «Таблиця» - саме його рядок заповнюється по одному на людину.",
                "Немає таблиці", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }

        if (tables.Count > 1)
        {
            MessageBox.Show(
                "У відомості може бути лише одна таблиця: рядок-шаблон у книзі один на всі аркуші.",
                "Забагато таблиць", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }

        if (tables[0].SheetIndex != 0)
        {
            MessageBox.Show(
                $"Таблиця має бути на першому аркуші («{Sheets[0].Name}») - саме його рядок заповнюється по одному на людину. "
                + "Решта аркушів може містити заголовки, текст і підписи.",
                "Таблиця не на першому аркуші", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }

        if (tables[0].Columns.Count == 0)
        {
            MessageBox.Show("Додайте хоча б одну колонку таблиці.", "Порожня таблиця",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }

        var hasRecipientTag = tables[0].Columns.Any(c =>
            TemplateBlockPreview.CollectTags(new TemplateBuilderDocument(new[]
                {
                    new Models.TemplateBuilder.TemplateBlock(TemplateBlockKind.Paragraph, c.Cell)
                }))
                .Any(tag => PlaceholderTagMaps.Classify(tag).SourceType == MappingSourceType.Recipient));

        if (!hasRecipientTag)
        {
            MessageBox.Show(
                "Хоча б одна колонка має містити поле з групи «Про людину» - інакше рядок не буде повторено на кожного.",
                "Немає полів людини", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }

        return true;
    }

    private void SetEditing(BuilderBlockViewModel block)
    {
        foreach (var other in Blocks) other.IsEditing = ReferenceEquals(other, block);
    }

    private void OnBlocksChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        foreach (var block in e.OldItems?.OfType<BuilderBlockViewModel>() ?? Enumerable.Empty<BuilderBlockViewModel>())
            Detach(block);

        foreach (var block in e.NewItems?.OfType<BuilderBlockViewModel>() ?? Enumerable.Empty<BuilderBlockViewModel>())
            Attach(block);

        RefreshVisibleBlocks();
        RefreshPreview();
    }

    private void Attach(BuilderBlockViewModel block)
    {
        block.PropertyChanged += OnChildChanged;
        foreach (var line in block.Signatures) line.PropertyChanged += OnChildChanged;
        foreach (var column in block.Columns) column.PropertyChanged += OnChildChanged;
    }

    private void Detach(BuilderBlockViewModel block)
    {
        block.PropertyChanged -= OnChildChanged;
        foreach (var line in block.Signatures) line.PropertyChanged -= OnChildChanged;
        foreach (var column in block.Columns) column.PropertyChanged -= OnChildChanged;
    }

    private void OnChildChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is null
            || BuilderBlockViewModel.NonDocumentProperties.Contains(e.PropertyName)
            || e.PropertyName == nameof(TableColumnViewModel.Letter)) return;

        RefreshPreview();
    }

    private void RefreshPreview()
    {
        PreviewItems.Clear();
        RefreshSheetLayout();

        var document = ToDocument();

        if (IsExcelMode)
            document = document with { Blocks = document.Blocks.Where(b => b.SheetIndex == CurrentSheetIndex).ToList() };

        var tags = TemplateBlockPreview.CollectTags(document);

        var values = SelectedTestPerson is { } person
            ? _builderService.ResolveValues(person.Id, tags)
            : new Dictionary<string, string>();

        var signatories = Signatories.ToDictionary(
            s => s.Id, s => new SignatoryInfo(s.Rank, s.ShortName));

        _sheet = IsExcelMode
            ? TemplateBlockPreview.BuildSheet(document.Blocks, values, signatories)
            : null;
        RebuildSheetGrid();

        if (IsExcelMode) return;

        foreach (var element in TemplateBlockPreview.Build(document, values, signatories))
        {
            PreviewItems.Add(element switch
            {
                PreviewTable table => new PreviewTableViewModel(table),
                PreviewLine line => new PreviewLineViewModel(line),
                _ => (object)element
            });
        }
    }

    private void RefreshSheetLayout()
    {
        if (!IsExcelMode)
        {
            SheetRangeCaption = null;
            foreach (var block in Blocks) block.LayoutCaption = null;
            return;
        }

        var blocks = VisibleBlocks.ToList();
        var layout = TemplateSheetLayout.Compute(blocks.Select(b => b.ToBlock()).ToList());

        foreach (var placement in layout.Placements)
        {
            var block = blocks[placement.BlockIndex];

            block.LayoutCaption = placement.FirstRow == placement.LastRow
                ? $"рядок {placement.FirstRow}"
                : $"рядки {placement.FirstRow}–{placement.LastRow}";

            if (!block.IsTable) continue;

            for (var i = 0; i < block.Columns.Count; i++)
                block.Columns[i].Letter = TemplateSheetLayout.ColumnLetter(i + 1);

            block.LayoutCaption =
                $"шапка {layout.HeaderRowIndex} · шаблон {layout.TemplateRowIndex} · "
                + $"{TemplateSheetLayout.ColumnLetter(1)}–{TemplateSheetLayout.ColumnLetter(layout.ColumnCount)}";
        }

        var sheetName = CurrentSheetIndex < Sheets.Count ? Sheets[CurrentSheetIndex].Name : string.Empty;
        var range = TemplateSheetLayout.Range(layout);

        SheetRangeCaption = layout.TemplateRowIndex > 0
            ? $"Аркуш «{sheetName}» · {range} · рядок-шаблон {layout.TemplateRowIndex}"
            : $"Аркуш «{sheetName}» · {range}";
    }
}
