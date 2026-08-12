using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
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

/// <summary>Кнопка «Додати блок» у лівій палітрі.</summary>
public record BlockPaletteItem(TemplateBlockKind Kind, string Icon, string Label);

/// <summary>Вкладка аркуша книги.</summary>
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

/// <summary>Розв'язаний стиль блока у величинах WPF — щоб XAML не тримав власних
/// конвертерів. Незадані гарнітура й кегль малюються як Times New Roman 11: це те,
/// що writer кладе в книгу, і те, на що схожий документ; типовий Segoe UI оболонки
/// робив би прев'ю документа несхожим на документ.</summary>
public class PreviewStyleViewModel
{
    public const string FallbackFont = "Times New Roman";
    public const double FallbackSize = 11;

    /// <summary>Пункт — 1/72 дюйма, а FontSize у WPF — 1/96. Без перерахунку
    /// 14-й кегль на екрані виглядав би дрібнішим за свої 14 пунктів, і різниця
    /// між розмірами читалася б слабше, ніж вона є в документі.</summary>
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

    /// <summary>Колір лежить у моделі як RRGGBB. Зіпсоване значення не має валити
    /// екран — тоді просто типовий колір тексту.</summary>
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

    // Заморожена кисть: прев'ю перебудовується на кожне натискання клавіші,
    // і незаморожені кисті тут накопичуються сотнями.
    private static SolidColorBrush CreateFrozen(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
}

/// <summary>Рядок попереднього перегляду: набір ділянок тексту з різними заливками
/// (з бази / вручну при генерації) — саме так їх розрізняє легенда макета.</summary>
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

/// <summary>Рядок аркуша в попередньому перегляді відомості.</summary>
public class SheetRowViewModel
{
    public SheetRowViewModel(SheetPreviewRow row)
    {
        Number = row.Number;
        IsMerged = row.IsMerged;
        IsTableHeader = row.IsTableHeader;
        IsTemplateRow = row.IsTemplateRow;
        Cells = row.Cells;
        Style = new PreviewStyleViewModel(row.Style);
    }

    public int Number { get; }
    public bool IsMerged { get; }
    public bool IsTableHeader { get; }
    public bool IsTemplateRow { get; }
    public IReadOnlyList<IReadOnlyList<PreviewRun>> Cells { get; }
    public PreviewStyleViewModel Style { get; }
}

/// <summary>Сітка відомості в попередньому перегляді: шапка і один рядок даних —
/// той, що на генерації клонується по одному на людину.</summary>
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

    /// <summary>У відомості гриф і рядок дати зайві, а таблиця — головне; у документі
    /// Word набір ширший.</summary>
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

    /// <summary>Для пунктирної картки «+ Додати блок» під переліком.</summary>
    public BlockPaletteItem ParagraphPaletteItem
        => BlockPalette.First(b => b.Kind == TemplateBlockKind.Paragraph);

    public IReadOnlyList<PaletteGroup> FieldGroups { get; }

    public IReadOnlyList<BuilderSignatory> Signatories { get; }

    /// <summary>Усі блоки документа — з усіх аркушів. У центрі показуються лише
    /// блоки поточного аркуша (VisibleBlocks).</summary>
    public ObservableCollection<BuilderBlockViewModel> Blocks { get; } = new();

    public ObservableCollection<BuilderBlockViewModel> VisibleBlocks { get; } = new();

    public ObservableCollection<SheetTabViewModel> Sheets { get; } = new();

    /// <summary>Заголовок над попереднім переглядом відомості: діапазон клітинок і
    /// номер рядка-шаблону — те, що оператор побачить, відкривши книгу в Excel.</summary>
    [ObservableProperty]
    private string? sheetRangeCaption;

    public ObservableCollection<BuilderTestPerson> TestPeople { get; }

    /// <summary>Рядки й таблиці впереміш — розкладку добирає типізований
    /// DataTemplate у XAML. Word-режим.</summary>
    public ObservableCollection<object> PreviewItems { get; } = new();

    /// <summary>Відомість показується сіткою аркуша: літери колонок і номери
    /// рядків — ті самі, що будуть у відкритій книзі.</summary>
    public ObservableCollection<SheetRowViewModel> SheetRows { get; } = new();

    public ObservableCollection<string> SheetColumnLetters { get; } = new();

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

    /// <summary>Аркуш, який редагується. У Word-режимі завжди 0.</summary>
    [ObservableProperty]
    private int currentSheetIndex;

    public bool IsWordMode => Mode == TemplateBuilderMode.Word;

    public bool IsExcelMode => Mode == TemplateBuilderMode.Excel;

    /// <summary>Ширина панелі перегляду. Поки роздільник не чіпали, панель береться
    /// по вмісту (Auto) в межах Min/Max: сітці відомості з багатьма колонками
    /// потрібно більше, ніж сторінці документа. Після перетягування ширина стає
    /// явною й фіксується.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PreviewPanelWidth))]
    private double previewWidth = 340;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PreviewPanelWidth))]
    private bool isPreviewAutoSized = true;

    public GridLength PreviewPanelWidth
        => IsPreviewAutoSized ? GridLength.Auto : new GridLength(PreviewWidth);

    public const double PreviewMinWidth = 260;
    public const double PreviewMaxWidth = 900;

    /// <summary>Режим фіксується назавжди, щойно шаблон збережено: Word живе в
    /// Templates, відомість — в ExportTemplates, і перекинути запис з однієї
    /// таблиці в іншу означало б загубити його зв'язки в пакетах і архіві.</summary>
    public bool CanSwitchMode => EditingTemplateId is null;

    /// <summary>Лише для відомості: перший аркуш клонується на кожну дату з
    /// ручного тега {{період}}.</summary>
    [ObservableProperty]
    private bool repeatSheetPerDate;

    /// <summary>Заповнений — конструктор редагує наявний шаблон, а не створює новий.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSwitchMode))]
    private int? editingTemplateId;

    [ObservableProperty]
    private string? statusMessage;

    /// <summary>Закрити конструктор і повернутись до переліку шаблонів.</summary>
    public event Action? RequestClose;

    /// <summary>Шаблон збережено — перелік у «Шаблонах» треба перечитати.</summary>
    public event Action? Saved;

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
        RefreshPreview();
    }

    /// <summary>false — у шаблону нема джерела блоків (завантажений файлом).</summary>
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
        RefreshPreview();
        return true;
    }

    /// <summary>Перемикання вкладки режиму. Набір блоків у Word і у відомості
    /// різний, тож зібране складання починається спочатку — питаємо, поки є що
    /// втрачати.</summary>
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
        OnPropertyChanged(nameof(SubtitleText));

        // Відомості потрібно ширше — але лише поки оператор не пересунув роздільник.
        if (IsPreviewAutoSized) PreviewWidth = value == TemplateBuilderMode.Excel ? 420 : 340;
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

    /// <summary>Прибирає останній аркуш разом з його блоками. Саме останній:
    /// видалення з середини зсунуло б номери всіх наступних, а SheetIndex лежить
    /// у збереженому JSON.</summary>
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

    /// <summary>Назва поточного аркуша — редагується прямо над переліком блоків,
    /// без окремого діалогу перейменування.</summary>
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

    /// <summary>Клік по блоку робить його «редагованим» — акцентна рамка з макета.</summary>
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
            StatusMessage = $"Шаблон збережено ({DateTime.Now:HH:mm}).";
            Saved?.Invoke();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Не вдалося зберегти шаблон", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    /// <summary>Назва кнопки залежить від режиму — «Експорт у docx» / «Експорт у xlsx».</summary>
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

    [RelayCommand]
    private void Close() => RequestClose?.Invoke();

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

        // Без таблиці у відомості немає рядка-шаблону, а отже й самої відомості:
        // на генерації вийшов би один аркуш із заголовком і без людей.
        var tables = Blocks.Where(b => b.IsTable).ToList();
        if (tables.Count == 0)
        {
            MessageBox.Show(
                "У відомості має бути блок «Таблиця» — саме його рядок заповнюється по одному на людину.",
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

        // Рушій клонує рядок-шаблон за одним номером на всю книгу, а «аркуш на
        // кожну дату» розмножує саме ПЕРШИЙ аркуш. Тому таблиця мусить бути на
        // ньому: інакше решта аркушів мовчки лишилась би без людей.
        if (tables[0].SheetIndex != 0)
        {
            MessageBox.Show(
                $"Таблиця має бути на першому аркуші («{Sheets[0].Name}») — саме його рядок заповнюється по одному на людину. "
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

        // Тег людини в рядку-шаблоні — те, за чим і рушій, і сканер завантаження
        // впізнають рядок, який треба клонувати.
        var hasRecipientTag = tables[0].Columns.Any(c =>
            TemplateBlockPreview.CollectTags(new TemplateBuilderDocument(new[]
                {
                    new Models.TemplateBuilder.TemplateBlock(TemplateBlockKind.Paragraph, c.Cell)
                }))
                .Any(tag => PlaceholderTagMaps.Classify(tag).SourceType == MappingSourceType.Recipient));

        if (!hasRecipientTag)
        {
            MessageBox.Show(
                "Хоча б одна колонка має містити поле з групи «Про людину» — інакше рядок не буде повторено на кожного.",
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
        // IsEditing — лише підсвітка картки, документ від неї не змінюється.
        // LayoutCaption і Letter проставляє сам RefreshPreview: без цієї перевірки
        // перерахунок розкладки запускав би сам себе нескінченно.
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

        // У відомості прев'ю показує поточний аркуш, а не всю книгу: інакше блоки
        // сусідніх аркушів злилися б в одну сторінку.
        if (IsExcelMode)
            document = document with { Blocks = document.Blocks.Where(b => b.SheetIndex == CurrentSheetIndex).ToList() };

        var tags = TemplateBlockPreview.CollectTags(document);

        var values = SelectedTestPerson is { } person
            ? _builderService.ResolveValues(person.Id, tags)
            : new Dictionary<string, string>();

        var signatories = Signatories.ToDictionary(
            s => s.Id, s => new SignatoryInfo(s.Rank, s.ShortName));

        // Відомість показуємо сіткою аркуша, а не сторінкою: оператору потрібно
        // бачити саме клітинки — у якому рядку й під якою літерою що опиниться.
        SheetRows.Clear();
        SheetColumnLetters.Clear();

        if (IsExcelMode)
        {
            var sheet = TemplateBlockPreview.BuildSheet(document.Blocks, values, signatories);

            foreach (var letter in sheet.ColumnLetters) SheetColumnLetters.Add(letter);
            foreach (var row in sheet.Rows) SheetRows.Add(new SheetRowViewModel(row));
            return;
        }

        foreach (var element in TemplateBlockPreview.Build(document, values, signatories))
        {
            // (розкладка вже проставлена вище — тут лише вміст сторінки)
            PreviewItems.Add(element switch
            {
                PreviewTable table => new PreviewTableViewModel(table),
                PreviewLine line => new PreviewLineViewModel(line),
                _ => (object)element
            });
        }
    }

    /// <summary>Підписує, що куди ляже на аркуші: рядки під кожним блоком, літери
    /// колонок таблиці й діапазон аркуша. Числа беруться зі спільної розкладки —
    /// тієї самої, за якою TemplateBlockXlsxWriter будує книгу.</summary>
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
