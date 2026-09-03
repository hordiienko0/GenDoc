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

    public record SignatureLine(string Caption, int? RecipientId);

    public record TableColumn(string Title, string Cell);

    public record TableSpec(IReadOnlyList<TableColumn> Columns, bool RepeatPerPerson);

    public enum BlockAlignment { Left, Center, Right, Justify }

    public record BlockStyle(
        string? FontFamily = null,
        double? FontSize = null,
        bool? Bold = null,
        bool? Italic = null,
        string? Color = null,
        BlockAlignment? Alignment = null)
    {
        public bool IsEmpty => FontFamily is null && FontSize is null && Bold is null
            && Italic is null && Color is null && Alignment is null;
    }

    public record TemplateBlock(
        TemplateBlockKind Kind,
        string? Text = null,
        TableSpec? Table = null,
        IReadOnlyList<SignatureLine>? Signatures = null,
        int SheetIndex = 0,
        BlockStyle? Style = null);

    public enum TemplateBuilderMode { Word, Excel }

    public record TemplateBuilderDocument(
        IReadOnlyList<TemplateBlock> Blocks,
        int Version = TemplateBuilderDocument.CurrentVersion,
        TemplateBuilderMode Mode = TemplateBuilderMode.Word,
        bool RepeatSheetPerDate = false,
        IReadOnlyList<string>? SheetNames = null)
    {
        public const int CurrentVersion = 1;

        public const string DefaultSheetName = "Відомість";

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
