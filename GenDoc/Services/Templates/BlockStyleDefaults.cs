using GenDoc.Models.TemplateBuilder;

namespace GenDoc.Services.Templates
{
    public record ResolvedBlockStyle(
        string? FontFamily,
        double? FontSize,
        bool Bold,
        bool Italic,
        string? Color,
        BlockAlignment Alignment);

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

        public static ResolvedBlockStyle ForTableHeader(ResolvedBlockStyle blockStyle, BlockAlignment alignment)
            => blockStyle with { Bold = true, Alignment = alignment };

        public static ResolvedBlockStyle Marker { get; } =
            new(null, null, Bold: false, Italic: false, null, BlockAlignment.Left);
    }
}
