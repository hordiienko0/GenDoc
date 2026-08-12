namespace GenDoc.Models.TemplateBuilder
{
    public enum TemplateBlockKind
    {
        Header,
        Title,
        DateAndCity,
        Paragraph,
        Table,
        Signatures
    }

    /// <summary>Один рядок підпису. Посадова особа береться з постійного складу
    /// (Recipient.IntakeId == null), а не вписується руками — тому тут ідентифікатор,
    /// а не текст.</summary>
    public record SignatureLine(string Caption, int? RecipientId);

    public record TableColumn(string Title, string Cell);

    public record TableSpec(IReadOnlyList<TableColumn> Columns, bool RepeatPerPerson);

    public enum BlockAlignment { Left, Center, Right, Justify }

    /// <summary>Оформлення блока. Кожне поле nullable і означає «успадкувати
    /// типове для цього типу блока» — див. BlockStyleDefaults. Не косметика, а
    /// сумісність: у збереженому раніше BuilderJson цього об'єкта немає взагалі,
    /// тож старий шаблон мусить розв'язатися в точно ту саму поведінку, що й до
    /// появи форматування.</summary>
    public record BlockStyle(
        string? FontFamily = null,
        double? FontSize = null,
        bool? Bold = null,
        bool? Italic = null,
        // RRGGBB без «#» — так його беруть і OpenXML, і ClosedXML.
        string? Color = null,
        BlockAlignment? Alignment = null)
    {
        /// <summary>Порожній стиль не варто серіалізувати: він нічого не змінює,
        /// а в JSON лишає сміття.</summary>
        public bool IsEmpty => FontFamily is null && FontSize is null && Bold is null
            && Italic is null && Color is null && Alignment is null;
    }

    /// <summary>Блок конструктора. Заповнені лише поля, доречні для свого Kind —
    /// решта null, щоб серіалізація не тягла порожнечу.</summary>
    public record TemplateBlock(
        TemplateBlockKind Kind,
        string? Text = null,
        TableSpec? Table = null,
        IReadOnlyList<SignatureLine>? Signatures = null,
        // Номер аркуша книги (лише для відомості); 0 — перший. У Word-документі
        // аркушів немає, і в збереженому раніше JSON поля теж немає — обидва
        // випадки дають 0, тобто «єдиний аркуш».
        int SheetIndex = 0,
        // Хвостовим, щоб позиційні виклики в наявному коді й тестах лишилися цілі.
        BlockStyle? Style = null);

    /// <summary>Word — документ на людину (Template + .docx); Excel — відомість на
    /// весь список (ExportTemplate + .xlsx із рядком-шаблоном).</summary>
    public enum TemplateBuilderMode { Word, Excel }

    /// <summary>Те, що лягає в BuilderJson. Version — щоб потім не гадати,
    /// яким кодом це писалося. Mode і RepeatSheetPerDate додані другим кроком
    /// (Excel-режим): у збереженого раніше JSON їх немає, і замовчування Word/false
    /// саме й описує ті шаблони.</summary>
    public record TemplateBuilderDocument(
        IReadOnlyList<TemplateBlock> Blocks,
        int Version = TemplateBuilderDocument.CurrentVersion,
        TemplateBuilderMode Mode = TemplateBuilderMode.Word,
        bool RepeatSheetPerDate = false,
        // Назви аркушів книги; порожньо — один аркуш із типовою назвою.
        IReadOnlyList<string>? SheetNames = null)
    {
        public const int CurrentVersion = 1;

        public const string DefaultSheetName = "Відомість";

        /// <summary>Назви аркушів, доповнені до фактичної кількості, використаної
        /// блоками: JSON міг зберегти менше назв, ніж є аркушів.</summary>
        public IReadOnlyList<string> ResolvedSheetNames()
        {
            var used = Blocks.Count == 0 ? 0 : Blocks.Max(b => b.SheetIndex) + 1;
            var count = Math.Max(Math.Max(used, SheetNames?.Count ?? 0), 1);

            return Enumerable.Range(0, count)
                .Select(i => SheetNames is not null && i < SheetNames.Count && !string.IsNullOrWhiteSpace(SheetNames[i])
                    ? SheetNames[i]
                    : i == 0 ? DefaultSheetName : $"{DefaultSheetName} {i + 1}")
                .ToList();
        }
    }
}
