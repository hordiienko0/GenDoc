using GenDoc.Models.TemplateBuilder;

namespace GenDoc.Services.Templates
{
    /// <summary>Оформлення блока після накладання на типове для його типу.
    /// FontFamily/FontSize/Color лишаються nullable і після розв'язання — див.
    /// коментар до BlockStyleDefaults.</summary>
    public record ResolvedBlockStyle(
        string? FontFamily,
        double? FontSize,
        bool Bold,
        bool Italic,
        string? Color,
        BlockAlignment Alignment);

    /// <summary>
    /// Один розрахунок оформлення на docx-writer, xlsx-writer і прев'ю — з тієї
    /// самої причини, що й TemplateSheetLayout: три незалежні копії правил
    /// неминуче розійдуться.
    ///
    /// Типові значення тут кодують те, що раніше було зашите в кожен writer
    /// окремо (гриф — праворуч, заголовок — центр і жирний, абзац — по ширині).
    /// Шрифт, розмір і колір типово null, і це навмисно: «нічого не задано»
    /// мусить дати рівно сьогоднішній результат — у .docx не писати RunFonts,
    /// FontSize і Color взагалі (Word візьме своє з docDefaults), у .xlsx —
    /// лишити ті Times New Roman 11, які writer ставив завжди. Розв'язувач не
    /// вигадує шрифт за оператора.
    /// </summary>
    public static class BlockStyleDefaults
    {
        public static ResolvedBlockStyle Resolve(TemplateBlockKind kind, BlockStyle? style)
        {
            var (alignment, bold) = DefaultsFor(kind);

            return new ResolvedBlockStyle(
                style?.FontFamily,
                style?.FontSize,
                style?.Bold ?? bold,
                style?.Italic ?? false,
                style?.Color,
                style?.Alignment ?? alignment);
        }

        /// <summary>Типові вирівнювання і жирність за типом блока. Для таблиці —
        /// це про РЯДОК ДАНИХ: шапка таблиці належить структурі, а не оформленню,
        /// і лишається жирною незалежно від стилю блока.</summary>
        private static (BlockAlignment Alignment, bool Bold) DefaultsFor(TemplateBlockKind kind) => kind switch
        {
            TemplateBlockKind.Header => (BlockAlignment.Right, false),
            TemplateBlockKind.Title => (BlockAlignment.Center, true),
            TemplateBlockKind.DateAndCity => (BlockAlignment.Justify, false),
            TemplateBlockKind.Paragraph => (BlockAlignment.Justify, false),
            TemplateBlockKind.Signatures => (BlockAlignment.Left, false),
            TemplateBlockKind.Table => (BlockAlignment.Left, false),
            _ => (BlockAlignment.Left, false)
        };

        /// <summary>Стиль шапки таблиці: гарнітура, розмір, колір і курсив від
        /// блока, а жирність — своя, структурна. Вирівнювання передає writer:
        /// у книзі шапка центрована, у документі — ліворуч, і так було до появи
        /// форматування.</summary>
        public static ResolvedBlockStyle ForTableHeader(ResolvedBlockStyle blockStyle, BlockAlignment alignment)
            => blockStyle with { Bold = true, Alignment = alignment };

        /// <summary>Маркерні рядки {{#особи}}/{{/особи}} — службові, оформлення
        /// оператора на них не поширюється.</summary>
        public static ResolvedBlockStyle Marker { get; } =
            new(null, null, Bold: false, Italic: false, null, BlockAlignment.Left);
    }
}
