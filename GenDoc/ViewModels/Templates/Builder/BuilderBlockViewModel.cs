using System.Collections.ObjectModel;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GenDoc.Models.TemplateBuilder;
using GenDoc.Services.Templates;

namespace GenDoc.ViewModels.Templates.Builder;

/// <summary>Пункт списку гарнітур. Value == null — «типовий»: writer тоді не пише
/// шрифт узагалі й документ бере його зі своїх типових.</summary>
public record FontOption(string? Value, string Label);

public record FontSizeOption(double? Value, string Label);

/// <summary>Готовий колір із палітри. Ручного HEX немає навмисно — оператор
/// обирає зі списку.</summary>
public record ColorOption(string? Value, string Label, Brush Swatch);

/// <summary>Рядок блоку «Підписи». Посада береться зі списку постійного складу —
/// у документ підуть звання і ПІБ на момент збирання .docx.</summary>
public partial class SignatureLineViewModel : ObservableObject
{
    public SignatureLineViewModel(
        string caption, BuilderSignatory? signatory, IReadOnlyList<BuilderSignatory> options)
    {
        this.caption = caption;
        this.signatory = signatory;
        Options = options;
    }

    public IReadOnlyList<BuilderSignatory> Options { get; }

    [ObservableProperty]
    private string caption;

    [ObservableProperty]
    private BuilderSignatory? signatory;
}

/// <summary>Колонка відомості: заголовок шапки і вміст рядка-шаблону (звідси
/// {{теги}} потрапляють у клоновані рядки на генерації).</summary>
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

    /// <summary>Літера колонки в Excel (A, B, C…) — щоб оператор бачив клітинку
    /// так само, як побачить її у відкритій книзі. Проставляє в'ю-модель за
    /// спільною розкладкою.</summary>
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

    /// <summary>У документі Word повторення — вибір оператора (маркери {{#особи}}
    /// роблять документ груповим). У відомості рядок-шаблон повторюється завжди,
    /// тож там прапорця немає.</summary>
    [ObservableProperty]
    private bool repeatPerPerson;

    public bool IsRepeatToggleVisible => IsTable && Mode == TemplateBuilderMode.Word;

    /// <summary>У відомості повторення не вимикається, тому замість прапорця —
    /// пояснення, що рядок і так клонується на кожну особу.</summary>
    public bool IsRepeatHintVisible => IsTable && Mode == TemplateBuilderMode.Excel;

    /// <summary>Аркуш книги, на якому лежить блок (лише для відомості).</summary>
    public int SheetIndex { get; set; }

    /// <summary>«рядок 3» / «рядки 4–6» — які клітинки аркуша займе цей блок.
    /// Рахується спільною розкладкою TemplateSheetLayout, тією самою, за якою
    /// writer кладе дані.</summary>
    [ObservableProperty]
    private string? layoutCaption;

    public IReadOnlyList<BuilderSignatory> SignatoryOptions { get; }

    public ObservableCollection<SignatureLineViewModel> Signatures { get; }

    public string KindTitle => Titles.TryGetValue(Kind, out var title) ? title : Kind.ToString();

    /// <summary>«Абзац · редагується» з макета — підпис картки під час правки.</summary>
    public string HeaderTitle => IsEditing ? $"{KindTitle} · редагується" : KindTitle;

    public bool IsTextBlock => Kind is TemplateBlockKind.Header or TemplateBlockKind.Title
        or TemplateBlockKind.DateAndCity or TemplateBlockKind.Paragraph;

    /// <summary>Гриф і абзац — багаторядкові; заголовок і рядок дати — ні.</summary>
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
    private string text;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HeaderTitle))]
    private bool isEditing;

    // ─── Оформлення блока ────────────────────────────────────────────────────
    //
    // У моделі лежить BlockStyle із nullable полями («успадкувати типове»), а
    // назовні віддаються вже розв'язані значення: перемикачі в поповері мусять
    // показувати те, що справді потрапить у документ. Щойно оператор чіпає
    // перемикач, значення стає явним — інакше зняти жирність із заголовка, який
    // жирний за замовчуванням, було б неможливо.

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

    /// <summary>Оформлення як воно лежить у моделі (nullable-поля). Сетер потрібен,
    /// щоб FromBlock підняв збережений стиль.</summary>
    public BlockStyle Style
    {
        get => style;
        set => Apply(value ?? new BlockStyle());
    }

    /// <summary>Стиль після накладання типових для типу блока — те, що покаже
    /// прев'ю і покладе writer.</summary>
    public ResolvedBlockStyle ResolvedStyle => BlockStyleDefaults.Resolve(Kind, style);

    /// <summary>Той самий стиль у величинах WPF: картка блока в редакторі малює
    /// текст так, як він ляже в документ, а не типовим шрифтом оболонки.</summary>
    public PreviewStyleViewModel DisplayStyle => new(ResolvedStyle);

    /// <summary>Поповер оформлення відкритий. Не впливає на документ, тому
    /// TemplateBuilderViewModel не перебудовує через нього прев'ю.</summary>
    [ObservableProperty]
    private bool isStyleOpen;

    /// <summary>Для таблиці жирність і вирівнювання стосуються рядка даних:
    /// шапка структурно лишається жирною і центрованою в книзі.</summary>
    public string StyleScopeHint => IsTable
        ? "Жирність і вирівнювання стосуються рядка даних — шапка таблиці лишається жирною."
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

    // Чотири прапорці замість enum'а: RadioButton'и в поповері прив'язуються
    // до них напряму, без конвертера. Скидання в false ігнорується — вимкнути
    // вирівнювання не можна, можна лише обрати інше.
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

    /// <summary>Повернути блок до типового оформлення його типу.</summary>
    [RelayCommand]
    private void ResetStyle() => Apply(new BlockStyle());

    /// <summary>Властивості, від яких сам документ не змінюється: підсвітка картки,
    /// підписи розкладки і дзеркальні властивості поповера. Останні важливі —
    /// одна зміна стилю сповіщає про десяток похідних, і без цього переліку
    /// прев'ю перебудовувалося б десять разів поспіль замість одного (сам стиль
    /// приїжджає окремим сповіщенням ResolvedStyle).</summary>
    public static IReadOnlySet<string> NonDocumentProperties { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        nameof(IsEditing), nameof(HeaderTitle), nameof(LayoutCaption), nameof(IsStyleOpen),
        nameof(DisplayStyle),
        nameof(SelectedFont), nameof(SelectedFontSize), nameof(SelectedColor),
        nameof(IsBold), nameof(IsItalic),
        nameof(IsAlignLeft), nameof(IsAlignCenter), nameof(IsAlignRight), nameof(IsAlignJustify)
    };

    private void Apply(BlockStyle updated)
    {
        style = updated;

        // Одне сповіщення на всі похідні: змінити гарнітуру означає перемалювати
        // і поповер, і картку блока, і прев'ю.
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
        // Порожній стиль не серіалізуємо: він нічого не міняє, а в JSON лишався
        // б назавжди.
        Style: style.IsEmpty ? null : style,
        // У відомості повторення не вимикається: рядок-шаблон за побудовою
        // клонується по одному на людину.
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
            // У старому BuilderJson поля Style немає — тоді лишається порожній
            // стиль, тобто сьогоднішнє типове оформлення.
            Style = block.Style ?? new BlockStyle()
        };
    }

    /// <summary>Заготовки за макетом: свіжий блок одразу виглядає як у документі,
    /// а не як порожня картка, яку ще треба вгадати, чим заповнити.</summary>
    public static BuilderBlockViewModel CreateNew(
        TemplateBlockKind kind, IReadOnlyList<BuilderSignatory> signatoryOptions,
        TemplateBuilderMode mode = TemplateBuilderMode.Word)
    {
        var text = kind switch
        {
            TemplateBlockKind.Header => "ЗАТВЕРДЖУЮ\nНачальник курсу\n{{звання_командира}} {{піб_командира}}",
            // Пробіли, а не табуляція: у .docx рядок пишеться одним Run зі
            // Space="preserve", а символ табуляції там не дає відступу.
            TemplateBlockKind.DateAndCity => "м. {{місто}}                              \"___\" ________ 20__ р.",
            _ => string.Empty
        };

        var signatures = kind == TemplateBlockKind.Signatures
            ? new[] { new SignatureLineViewModel(string.Empty, null, signatoryOptions) }
            : null;

        // Заготовка відомості: нумерація, звання, ПІБ — колонки, з яких на практиці
        // починається будь-яка з наявних відомостей.
        var columns = kind == TemplateBlockKind.Table
            ? new[]
            {
                new TableColumnViewModel("№ з/п", "{{номер}}"),
                new TableColumnViewModel("Військове звання", "{{звання}}"),
                new TableColumnViewModel("ПІБ", "{{піб}}")
            }
            : null;

        return new BuilderBlockViewModel(
            kind, text, signatures, signatoryOptions, columns, repeatPerPerson: true, mode);
    }
}
