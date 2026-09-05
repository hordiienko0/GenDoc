using System.Collections.ObjectModel;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GenDoc.Models.TemplateBuilder;
using GenDoc.Services.Templates;

namespace GenDoc.ViewModels.Templates.Builder;

public record FontOption(string? Value, string Label);

public record FontSizeOption(double? Value, string Label);

public record ColorOption(string? Value, string Label, Brush Swatch);

public partial class SignatureLineViewModel : ObservableObject
{
    public static BuilderSignatory NoSignatory { get; } = new(0, "(без підписанта)", string.Empty, string.Empty);

    public SignatureLineViewModel(
        string caption, BuilderSignatory? signatory, IReadOnlyList<BuilderSignatory> options)
    {
        this.caption = caption;
        this.signatory = signatory;
        Options = options;
        SignatoryChoices = new[] { NoSignatory }.Concat(options).ToList();
    }

    public IReadOnlyList<BuilderSignatory> Options { get; }

    public IReadOnlyList<BuilderSignatory> SignatoryChoices { get; }

    [ObservableProperty]
    private string caption;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SelectedChoice))]
    private BuilderSignatory? signatory;

    public BuilderSignatory SelectedChoice
    {
        get => Signatory ?? NoSignatory;
        set => Signatory = value is null || ReferenceEquals(value, NoSignatory) ? null : value;
    }
}

public partial class TableColumnViewModel : ObservableObject
{
    public TableColumnViewModel(string title, string cell)
    {
        this.title = title;
        this.cell = cell;
    }

    [ObservableProperty]
    private string title;

    [ObservableProperty]
    private string cell;

    [ObservableProperty]
    private string letter = string.Empty;
}

public partial class BuilderBlockViewModel : ObservableObject
{
    private static readonly IReadOnlyDictionary<TemplateBlockKind, string> Titles =
        new Dictionary<TemplateBlockKind, string>
        {
            [TemplateBlockKind.Header] = "Шапка (гриф)",
            [TemplateBlockKind.Title] = "Заголовок",
            [TemplateBlockKind.DateAndCity] = "Дата і місто",
            [TemplateBlockKind.Paragraph] = "Абзац",
            [TemplateBlockKind.Table] = "Таблиця",
            [TemplateBlockKind.Signatures] = "Підписи"
        };

    public BuilderBlockViewModel(
        TemplateBlockKind kind, string? text, IEnumerable<SignatureLineViewModel>? signatures,
        IReadOnlyList<BuilderSignatory> signatoryOptions,
        IEnumerable<TableColumnViewModel>? columns = null,
        bool repeatPerPerson = true,
        TemplateBuilderMode mode = TemplateBuilderMode.Word)
    {
        Kind = kind;
        this.text = text ?? string.Empty;
        this.repeatPerPerson = repeatPerPerson;
        Mode = mode;
        SignatoryOptions = signatoryOptions;
        Signatures = new ObservableCollection<SignatureLineViewModel>(
            signatures ?? Enumerable.Empty<SignatureLineViewModel>());
        Columns = new ObservableCollection<TableColumnViewModel>(
            columns ?? Enumerable.Empty<TableColumnViewModel>());
    }

    public TemplateBlockKind Kind { get; }

    public TemplateBuilderMode Mode { get; }

    public ObservableCollection<TableColumnViewModel> Columns { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RepeatHintText))]
    private bool repeatPerPerson;

    public bool IsRepeatToggleVisible => IsTable && Mode == TemplateBuilderMode.Word;

    public string RepeatHintText => RepeatPerPerson
        ? "Увімкнено: рядок таблиці повторюється на кожну особу списку, тож шаблон стає груповим - "
          + "один документ на весь список, а не на кожного."
        : "Вимкнено: таблиця з одним рядком, окремий документ на кожну особу.";

    public bool IsRepeatHintVisible => IsTable && Mode == TemplateBuilderMode.Excel;

    public int SheetIndex { get; set; }

    private int? anchorRow;

    private int? anchorColumn;

    public int? AnchorRow
    {
        get => anchorRow;
        set => SetAnchor(value, anchorColumn);
    }

    public int? AnchorColumn
    {
        get => anchorColumn;
        set => SetAnchor(anchorRow, value);
    }

    [ObservableProperty]
    private int? spanColumns;

    public int EffectiveRow { get; set; } = 1;

    public int EffectiveColumn { get; set; } = 1;

    public int EffectiveSpan { get; set; } = 1;

    [ObservableProperty]
    private bool isConflicting;

    [ObservableProperty]
    private string? layoutCaption;

    public IReadOnlyList<BuilderSignatory> SignatoryOptions { get; }

    public ObservableCollection<SignatureLineViewModel> Signatures { get; }

    public string KindTitle => Titles.TryGetValue(Kind, out var title) ? title : Kind.ToString();

    public string HeaderTitle => IsEditing ? $"{KindTitle} · редагується" : KindTitle;

    public bool IsTextBlock => Kind is TemplateBlockKind.Header or TemplateBlockKind.Title
        or TemplateBlockKind.DateAndCity or TemplateBlockKind.Paragraph;

    public bool IsMultiline => Kind is TemplateBlockKind.Header or TemplateBlockKind.Paragraph;

    public bool IsSignatures => Kind == TemplateBlockKind.Signatures;

    public bool IsTable => Kind == TemplateBlockKind.Table;

    public string Watermark => Kind switch
    {
        TemplateBlockKind.Header => "ЗАТВЕРДЖУЮ…",
        TemplateBlockKind.Title => "Назва документа",
        TemplateBlockKind.DateAndCity => "м. {{місто}}",
        _ => "Текст абзацу; поля вставляються кліком у лівій панелі"
    };

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CollapsedSummary))]
    private string text;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HeaderTitle))]
    private bool isEditing;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsExpanded))]
    [NotifyPropertyChangedFor(nameof(CollapseIcon))]
    [NotifyPropertyChangedFor(nameof(CollapseTooltip))]
    private bool isCollapsed;

    public bool IsExpanded => !IsCollapsed;

    public string CollapseIcon => IsCollapsed ? "⌄" : "⌃";

    public string CollapseTooltip => IsCollapsed ? "Розгорнути блок" : "Згорнути блок";

    public string CollapsedSummary
    {
        get
        {
            if (IsTable)
                return $"колонок: {Columns.Count}";

            if (IsSignatures)
                return $"рядків підпису: {Signatures.Count}";

            var firstLine = (Text ?? string.Empty)
                .Replace("\r\n", "\n")
                .Split('\n')
                .FirstOrDefault(l => !string.IsNullOrWhiteSpace(l))?
                .Trim() ?? string.Empty;

            return firstLine.Length > 60 ? firstLine[..60] + "…" : firstLine;
        }
    }

    public static IReadOnlyList<FontOption> FontOptions { get; } = new[]
    {
        new FontOption(null, "(типовий)"),
        new FontOption("Times New Roman", "Times New Roman"),
        new FontOption("Arial", "Arial"),
        new FontOption("Calibri", "Calibri"),
        new FontOption("Verdana", "Verdana"),
        new FontOption("Courier New", "Courier New")
    };

    public static IReadOnlyList<FontSizeOption> FontSizeOptions { get; } =
        new FontSizeOption?[] { new(null, "(типовий)") }
            .Concat(new double[] { 8, 9, 10, 11, 12, 14, 16, 18, 20, 24 }
                .Select(s => new FontSizeOption(s, s.ToString("0.#"))))
            .Select(o => o!)
            .ToList();

    public static IReadOnlyList<ColorOption> ColorOptions { get; } = new[]
    {
        new ColorOption(null, "(типовий)", Swatch("111111")),
        new ColorOption("000000", "Чорний", Swatch("000000")),
        new ColorOption("595959", "Темно-сірий", Swatch("595959")),
        new ColorOption("1F4E79", "Синій", Swatch("1F4E79")),
        new ColorOption("C00000", "Червоний", Swatch("C00000")),
        new ColorOption("2E7D32", "Зелений", Swatch("2E7D32"))
    };

    private static Brush Swatch(string hex)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString($"#{hex}"));
        brush.Freeze();
        return brush;
    }

    private BlockStyle style = new();

    public BlockStyle Style
    {
        get => style;
        set => Apply(value ?? new BlockStyle());
    }

    public ResolvedBlockStyle ResolvedStyle => BlockStyleDefaults.Resolve(Kind, style);

    public PreviewStyleViewModel DisplayStyle => new(ResolvedStyle);

    [ObservableProperty]
    private bool isStyleOpen;

    public string StyleScopeHint => IsTable
        ? "Жирність і вирівнювання стосуються рядка даних - шапка таблиці лишається жирною."
        : string.Empty;

    public bool IsStyleScopeHintVisible => IsTable;

    public FontOption SelectedFont
    {
        get => FontOptions.FirstOrDefault(o => o.Value == style.FontFamily) ?? FontOptions[0];
        set => Apply(style with { FontFamily = value?.Value });
    }

    public FontSizeOption SelectedFontSize
    {
        get => FontSizeOptions.FirstOrDefault(o => o.Value == style.FontSize) ?? FontSizeOptions[0];
        set => Apply(style with { FontSize = value?.Value });
    }

    public ColorOption SelectedColor
    {
        get => ColorOptions.FirstOrDefault(o => o.Value == style.Color) ?? ColorOptions[0];
        set => Apply(style with { Color = value?.Value });
    }

    public bool IsBold
    {
        get => ResolvedStyle.Bold;
        set => Apply(style with { Bold = value });
    }

    public bool IsItalic
    {
        get => ResolvedStyle.Italic;
        set => Apply(style with { Italic = value });
    }

    public bool IsAlignLeft
    {
        get => ResolvedStyle.Alignment == BlockAlignment.Left;
        set { if (value) Apply(style with { Alignment = BlockAlignment.Left }); }
    }

    public bool IsAlignCenter
    {
        get => ResolvedStyle.Alignment == BlockAlignment.Center;
        set { if (value) Apply(style with { Alignment = BlockAlignment.Center }); }
    }

    public bool IsAlignRight
    {
        get => ResolvedStyle.Alignment == BlockAlignment.Right;
        set { if (value) Apply(style with { Alignment = BlockAlignment.Right }); }
    }

    public bool IsAlignJustify
    {
        get => ResolvedStyle.Alignment == BlockAlignment.Justify;
        set { if (value) Apply(style with { Alignment = BlockAlignment.Justify }); }
    }

    [RelayCommand]
    private void ResetStyle() => Apply(new BlockStyle());

    [RelayCommand]
    private void ToggleCollapsed() => IsCollapsed = !IsCollapsed;

    public event Action<string>? HintRequested;

    public const string CellAddressHint =
        "Адреса клітинки має вигляд «C4» - стовпець і рядок; порожньо - одразу під попереднім блоком.";

    public const string SpanHint =
        "Ширина блока - кількість клітинок, напр. 3; порожньо - на всю ширину таблиці.";

    public bool IsCellControlVisible => Mode == TemplateBuilderMode.Excel;

    public bool IsSpanControlVisible => Mode == TemplateBuilderMode.Excel && !IsTable;

    public string CellAddress
    {
        get => TemplateSheetLayout.FormatCellAddress(AnchorRow, AnchorColumn);
        set
        {
            if (!TemplateSheetLayout.TryParseCellAddress(value, out var row, out var column))
            {
                HintRequested?.Invoke(CellAddressHint);
                OnPropertyChanged(nameof(CellAddress));
                return;
            }

            SetAnchor(row, column);
        }
    }

    public string SpanText
    {
        get => SpanColumns?.ToString() ?? string.Empty;
        set
        {
            var cleaned = (value ?? string.Empty).Trim();

            if (cleaned.Length == 0)
            {
                SpanColumns = null;
                return;
            }

            if (!int.TryParse(cleaned, out var span) || span < 1 || span > TemplateSheetLayout.MaxColumn)
            {
                HintRequested?.Invoke(SpanHint);
                OnPropertyChanged(nameof(SpanText));
                return;
            }

            SpanColumns = span;
        }
    }

    [RelayCommand]
    private void MoveBlockCell(string? direction)
    {
        if (!IsCellControlVisible) return;

        var row = AnchorRow ?? EffectiveRow;
        var column = AnchorColumn ?? EffectiveColumn;

        switch (direction)
        {
            case "Up": row--; break;
            case "Down": row++; break;
            case "Left": column--; break;
            case "Right": column++; break;
            default: return;
        }

        SetAnchor(
            Math.Clamp(row, 1, TemplateSheetLayout.MaxRow),
            Math.Clamp(column, 1, TemplateSheetLayout.MaxColumn));
    }

    [RelayCommand]
    private void ChangeSpan(string? delta)
    {
        if (!IsSpanControlVisible || !int.TryParse(delta, out var step)) return;

        SpanColumns = Math.Clamp((SpanColumns ?? EffectiveSpan) + step, 1, TemplateSheetLayout.MaxColumn);
    }

    private void SetAnchor(int? row, int? column)
    {
        if (row == anchorRow && column == anchorColumn)
        {
            OnPropertyChanged(nameof(CellAddress));
            return;
        }

        var rowChanged = row != anchorRow;
        var columnChanged = column != anchorColumn;
        anchorRow = row;
        anchorColumn = column;

        if (rowChanged) OnPropertyChanged(nameof(AnchorRow));
        if (columnChanged) OnPropertyChanged(nameof(AnchorColumn));
        OnPropertyChanged(nameof(CellAddress));
    }

    partial void OnSpanColumnsChanged(int? value) => OnPropertyChanged(nameof(SpanText));

    public static IReadOnlySet<string> NonDocumentProperties { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        nameof(IsEditing), nameof(HeaderTitle), nameof(LayoutCaption), nameof(IsConflicting), nameof(IsStyleOpen),
        nameof(DisplayStyle), nameof(RepeatHintText), nameof(CellAddress), nameof(SpanText),
        nameof(IsCollapsed), nameof(IsExpanded), nameof(CollapseIcon),
        nameof(CollapseTooltip), nameof(CollapsedSummary),
        nameof(SelectedFont), nameof(SelectedFontSize), nameof(SelectedColor),
        nameof(IsBold), nameof(IsItalic),
        nameof(IsAlignLeft), nameof(IsAlignCenter), nameof(IsAlignRight), nameof(IsAlignJustify)
    };

    private void Apply(BlockStyle updated)
    {
        style = updated;

        OnPropertyChanged(nameof(ResolvedStyle));
        OnPropertyChanged(nameof(DisplayStyle));
        OnPropertyChanged(nameof(SelectedFont));
        OnPropertyChanged(nameof(SelectedFontSize));
        OnPropertyChanged(nameof(SelectedColor));
        OnPropertyChanged(nameof(IsBold));
        OnPropertyChanged(nameof(IsItalic));
        OnPropertyChanged(nameof(IsAlignLeft));
        OnPropertyChanged(nameof(IsAlignCenter));
        OnPropertyChanged(nameof(IsAlignRight));
        OnPropertyChanged(nameof(IsAlignJustify));
    }

    public TemplateBlock ToBlock() => new(
        Kind,
        IsTextBlock ? Text : null,
        SheetIndex: SheetIndex,
        Style: style.IsEmpty ? null : style,
        AnchorRow: AnchorRow,
        AnchorColumn: AnchorColumn,
        SpanColumns: IsTable ? null : SpanColumns,
        Table: IsTable
            ? new TableSpec(
                Columns.Select(c => new TableColumn(c.Title, c.Cell)).ToList(),
                RepeatPerPerson: Mode == TemplateBuilderMode.Excel || RepeatPerPerson)
            : null,
        Signatures: IsSignatures
            ? Signatures.Select(s => new SignatureLine(s.Caption, s.Signatory?.Id)).ToList()
            : null);

    public static BuilderBlockViewModel FromBlock(
        TemplateBlock block, IReadOnlyList<BuilderSignatory> signatoryOptions,
        TemplateBuilderMode mode = TemplateBuilderMode.Word)
    {
        var signatures = (block.Signatures ?? Array.Empty<SignatureLine>())
            .Select(s => new SignatureLineViewModel(
                s.Caption,
                s.RecipientId is int id ? signatoryOptions.FirstOrDefault(o => o.Id == id) : null,
                signatoryOptions));

        var columns = (block.Table?.Columns ?? Array.Empty<TableColumn>())
            .Select(c => new TableColumnViewModel(c.Title, c.Cell));

        return new BuilderBlockViewModel(
            block.Kind, block.Text, signatures, signatoryOptions, columns,
            block.Table?.RepeatPerPerson ?? true, mode)
        {
            SheetIndex = block.SheetIndex,
            AnchorRow = block.AnchorRow,
            AnchorColumn = block.AnchorColumn,
            SpanColumns = block.SpanColumns,
            Style = block.Style ?? new BlockStyle()
        };
    }

    public static BuilderBlockViewModel CreateNew(
        TemplateBlockKind kind, IReadOnlyList<BuilderSignatory> signatoryOptions,
        TemplateBuilderMode mode = TemplateBuilderMode.Word)
    {
        var text = kind switch
        {
            TemplateBlockKind.Header => "ЗАТВЕРДЖУЮ\nНачальник курсу\n{{звання_командира}} {{піб_командира}}",
            TemplateBlockKind.DateAndCity => "м. {{місто}}                              \"___\" ________ 20__ р.",
            _ => string.Empty
        };

        var signatures = kind == TemplateBlockKind.Signatures
            ? new[] { new SignatureLineViewModel(string.Empty, null, signatoryOptions) }
            : null;

        var columns = kind == TemplateBlockKind.Table
            ? new[]
            {
                new TableColumnViewModel("№ з/п", "{{номер}}"),
                new TableColumnViewModel("Військове звання", "{{звання}}"),
                new TableColumnViewModel("ПІБ", "{{піб}}")
            }
            : null;

        return new BuilderBlockViewModel(
            kind, text, signatures, signatoryOptions, columns,
            repeatPerPerson: mode == TemplateBuilderMode.Excel, mode);
    }
}
