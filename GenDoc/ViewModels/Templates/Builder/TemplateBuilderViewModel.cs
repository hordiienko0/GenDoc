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
    public SheetCellViewModel(
        IReadOnlyList<PreviewRun> runs, double width, PreviewStyleViewModel style, bool isConflict = false)
    {
        Runs = runs;
        Width = width;
        Style = style;
        IsConflict = isConflict;
    }

    public IReadOnlyList<PreviewRun> Runs { get; }
    public double Width { get; }
    public PreviewStyleViewModel Style { get; }
    public bool IsConflict { get; }
}

public class SheetColumnViewModel
{
    public SheetColumnViewModel(string letter, double width)
    {
        Letter = letter;
        Width = width;
    }

    public string Letter { get; }
    public double Width { get; }
}

public class SheetRowViewModel
{
    private static readonly IReadOnlyList<PreviewRun> NoRuns = Array.Empty<PreviewRun>();

    private static readonly PreviewStyleViewModel FillerStyle =
        new(BlockStyleDefaults.Resolve(TemplateBlockKind.Paragraph, null));

    public SheetRowViewModel(int number, IReadOnlyList<SheetPreviewRow> segments, IReadOnlyList<double> widths)
    {
        Number = number;
        IsMerged = segments.Any(s => s.IsMerged);
        IsTableHeader = segments.Any(s => s.IsTableHeader);
        IsTemplateRow = segments.Any(s => s.IsTemplateRow);
        Style = segments.Count > 0 ? new PreviewStyleViewModel(segments[0].Style) : FillerStyle;

        var slots = widths.Select(width => new SheetCellViewModel(NoRuns, width, FillerStyle)).ToArray();

        foreach (var segment in segments)
        {
            var style = new PreviewStyleViewModel(segment.Style);
            var first = segment.FirstColumn - 1;
            if (first < 0 || first >= slots.Length) continue;

            if (segment.IsMerged)
            {
                var span = Math.Clamp(segment.SpanColumns, 1, slots.Length - first);
                var merged = new SheetCellViewModel(
                    segment.Cells.Count > 0 ? segment.Cells[0] : NoRuns,
                    widths.Skip(first).Take(span).Sum(), style, segment.IsConflicting);

                for (var i = first; i < first + span; i++)
                {
                    Release(slots, i, widths);
                    slots[i] = merged;
                }

                continue;
            }

            for (var i = 0; i < segment.Cells.Count && first + i < slots.Length; i++)
            {
                Release(slots, first + i, widths);
                slots[first + i] = new SheetCellViewModel(segment.Cells[i], widths[first + i], style, segment.IsConflicting);
            }
        }

        var cells = new List<SheetCellViewModel>(slots.Length);
        foreach (var slot in slots)
        {
            if (cells.Count > 0 && ReferenceEquals(cells[^1], slot)) continue;
            cells.Add(slot);
        }

        Cells = cells;
    }

    public SheetRowViewModel(int number, IReadOnlyList<double> widths)
        : this(number, Array.Empty<SheetPreviewRow>(), widths)
    {
    }

    private static void Release(SheetCellViewModel[] slots, int index, IReadOnlyList<double> widths)
    {
        var occupant = slots[index];
        var owned = Enumerable.Range(0, slots.Length).Where(i => ReferenceEquals(slots[i], occupant)).ToList();
        if (owned.Count < 2) return;

        var keepRuns = true;
        foreach (var i in owned)
        {
            slots[i] = new SheetCellViewModel(keepRuns ? occupant.Runs : NoRuns, widths[i], occupant.Style, occupant.IsConflict);
            keepRuns = false;
        }
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

    public ObservableCollection<SheetColumnViewModel> SheetColumns { get; } = new();

    public const double SheetCellWidth = 64;
    public const double SheetRowHeight = 26;
    public const double SheetRowHeaderWidth = 32;
    public const double ScrollBarSize = 17;

    private const double ExcelCharPixels = 7;
    private const double ExcelCellPadding = 5;

    public static double ExcelColumnWidthPx(double chars) => Math.Round(chars * ExcelCharPixels + ExcelCellPadding);

    private SheetPreview? _sheet;
    private IReadOnlyDictionary<int, double> _sheetColumnWidthChars = new Dictionary<int, double>();
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
        SheetColumns.Clear();

        if (_sheet is null) return;

        var widths = new List<double>();
        for (var i = 0; i < _sheet.ColumnLetters.Count; i++)
            widths.Add(_sheetColumnWidthChars.TryGetValue(i + 1, out var chars) ? ExcelColumnWidthPx(chars) : SheetCellWidth);

        var availableWidth = _sheetViewportWidth - SheetRowHeaderWidth - ScrollBarSize;
        while (widths.Sum() + SheetCellWidth <= availableWidth) widths.Add(SheetCellWidth);

        for (var i = 0; i < widths.Count; i++)
            SheetColumns.Add(new SheetColumnViewModel(TemplateSheetLayout.ColumnLetter(i + 1), widths[i]));

        var segments = _sheet.Rows
            .GroupBy(r => r.Number)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<SheetPreviewRow>)g.ToList());

        var lastNumber = segments.Count == 0 ? 0 : segments.Keys.Max();
        var visibleRows = (int)Math.Floor(Math.Max(_sheetViewportHeight - SheetRowHeight - ScrollBarSize, 0) / SheetRowHeight);

        for (var number = 1; number <= Math.Max(lastNumber, visibleRows); number++)
        {
            SheetRows.Add(segments.TryGetValue(number, out var rows)
                ? new SheetRowViewModel(number, rows, widths)
                : new SheetRowViewModel(number, widths));
        }
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

    [ObservableProperty]
    private bool isStatusSuccess;

    public void ShowStatus(string message, bool success)
    {
        IsStatusSuccess = success;
        StatusMessage = message;
    }

    public void ShowHint(string message) => ShowStatus(message, false);

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

        if (Blocks.Any(BlockHasContent))
        {
            var confirm = MessageBox.Show(
                "Набір блоків у документі Word і у відомості Excel різний, тому складання почнеться заново. Продовжити?",
                "Змінити режим", MessageBoxButton.YesNo, MessageBoxImage.Question);

            if (confirm != MessageBoxResult.Yes) return;
        }

        var name = TemplateName;
        StartNew(target);
        TemplateName = name;
        TakeSnapshot();
    }

    public static bool BlockHasContent(BuilderBlockViewModel block)
    {
        if (block.IsTable) return block.Columns.Count > 0;
        if (block.IsSignatures)
            return block.Signatures.Any(s => !string.IsNullOrWhiteSpace(s.Caption) || s.Signatory is not null);
        return !string.IsNullOrWhiteSpace(block.Text);
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
    private void RemoveCurrentSheet()
    {
        if (Sheets.Count < 2) return;

        var index = CurrentSheetIndex;
        var doomed = Blocks.Count(b => b.SheetIndex == index);

        if (doomed > 0)
        {
            var confirm = MessageBox.Show(
                $"Аркуш «{Sheets[index].Name}» містить {doomed} блок(ів). Видалити разом з ними?",
                "Видалити аркуш", MessageBoxButton.YesNo, MessageBoxImage.Warning);

            if (confirm != MessageBoxResult.Yes) return;
        }

        RemoveCurrentSheetCore();
    }

    public void RemoveCurrentSheetCore()
    {
        if (Sheets.Count < 2) return;

        var index = CurrentSheetIndex;

        foreach (var block in Blocks.Where(b => b.SheetIndex == index).ToList()) Blocks.Remove(block);
        foreach (var block in Blocks.Where(b => b.SheetIndex > index)) block.SheetIndex--;

        var names = Sheets.Where(s => s.Index != index).Select(s => s.Name).ToList();
        Sheets.Clear();
        for (var i = 0; i < names.Count; i++) Sheets.Add(new SheetTabViewModel(i, names[i]));

        CurrentSheetIndex = Math.Min(index, Sheets.Count - 1);
        foreach (var sheet in Sheets) sheet.IsCurrent = sheet.Index == CurrentSheetIndex;
        OnPropertyChanged(nameof(CurrentSheetName));
        RefreshVisibleBlocks();
        RefreshPreview();
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

        if (BlockHasContent(block))
        {
            var confirm = MessageBox.Show(
                $"Блок «{block.KindTitle}» не порожній. Видалити його? Повернути видалений блок буде неможливо.",
                "Видалити блок", MessageBoxButton.YesNo, MessageBoxImage.Warning);

            if (confirm != MessageBoxResult.Yes) return;
        }

        RemoveBlockCore(block);
    }

    public void RemoveBlockCore(BuilderBlockViewModel block) => Blocks.Remove(block);

    [RelayCommand]
    private void MoveBlockUp(BuilderBlockViewModel? block)
    {
        if (block is null) return;
        var index = Blocks.IndexOf(block);
        if (index < 0) return;

        for (var target = index - 1; target >= 0; target--)
        {
            if (Blocks[target].SheetIndex != block.SheetIndex) continue;
            Blocks.Move(index, target);
            return;
        }
    }

    [RelayCommand]
    private void MoveBlockDown(BuilderBlockViewModel? block)
    {
        if (block is null) return;
        var index = Blocks.IndexOf(block);
        if (index < 0) return;

        for (var target = index + 1; target < Blocks.Count; target++)
        {
            if (Blocks[target].SheetIndex != block.SheetIndex) continue;
            Blocks.Move(index, target);
            return;
        }
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
            var id = _builderService.Save(EditingTemplateId, TemplateName.Trim(), ToDocument());
            EditingTemplateId = id;
            TemplateName = _builderService.Load(id, Mode)?.Name ?? TemplateName.Trim();
            TakeSnapshot();
            ShowStatus($"Шаблон збережено ({DateTime.Now:HH:mm}).", success: true);
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
            ShowStatus($"Вивантажено: {Path.GetFileName(dialog.FileName)}", success: true);
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

            ShowHint("Відкрито для перегляду; правки у файлі назад у шаблон не повертаються.");
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
        var layout = RefreshSheetLayout();

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
        _sheetColumnWidthChars = layout is not null
            ? TemplateBlockXlsxWriter.ColumnWidths(document.Blocks, layout)
            : new Dictionary<int, double>();
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

    private SheetLayout? RefreshSheetLayout()
    {
        if (!IsExcelMode)
        {
            SheetRangeCaption = null;
            foreach (var block in Blocks)
            {
                block.LayoutCaption = null;
                block.IsConflicting = false;
            }
            return null;
        }

        var blocks = VisibleBlocks.ToList();
        var layout = TemplateSheetLayout.Compute(blocks.Select(b => b.ToBlock()).ToList());

        foreach (var placement in layout.Placements)
        {
            var block = blocks[placement.BlockIndex];

            block.EffectiveRow = placement.FirstRow;
            block.EffectiveColumn = placement.FirstColumn;
            block.EffectiveSpan = placement.LastColumn - placement.FirstColumn + 1;
            block.IsConflicting = false;

            var columns = placement.FirstColumn == placement.LastColumn
                ? TemplateSheetLayout.ColumnLetter(placement.FirstColumn)
                : $"{TemplateSheetLayout.ColumnLetter(placement.FirstColumn)}–{TemplateSheetLayout.ColumnLetter(placement.LastColumn)}";

            if (!block.IsTable)
            {
                var rows = placement.FirstRow == placement.LastRow
                    ? $"рядок {placement.FirstRow}"
                    : $"рядки {placement.FirstRow}–{placement.LastRow}";
                block.LayoutCaption = $"{rows} · {columns}";
                continue;
            }

            for (var i = 0; i < block.Columns.Count; i++)
                block.Columns[i].Letter = TemplateSheetLayout.ColumnLetter(placement.FirstColumn + i);

            block.LayoutCaption = $"шапка {placement.FirstRow} · шаблон {placement.FirstRow + 1} · {columns}";
        }

        foreach (var conflict in layout.Conflicts)
        {
            var later = blocks[conflict.BlockIndex];
            var earlier = blocks[conflict.OtherBlockIndex];
            var text = $"Блок «{later.KindTitle}» перекриває блок «{earlier.KindTitle}» у {conflict.Cell}";

            foreach (var block in new[] { later, earlier })
            {
                if (block.IsConflicting) continue;
                block.IsConflicting = true;
                block.LayoutCaption = text;
            }
        }

        var sheetName = CurrentSheetIndex < Sheets.Count ? Sheets[CurrentSheetIndex].Name : string.Empty;
        var range = TemplateSheetLayout.Range(layout);

        SheetRangeCaption = layout.TemplateRowIndex > 0
            ? $"Аркуш «{sheetName}» · {range} · рядок-шаблон {layout.TemplateRowIndex}"
            : $"Аркуш «{sheetName}» · {range}";

        return layout;
    }
}
