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

    /// <summary>Блок конструктора. Заповнені лише поля, доречні для свого Kind —
    /// решта null, щоб серіалізація не тягла порожнечу.</summary>
    public record TemplateBlock(
        TemplateBlockKind Kind,
        string? Text = null,
        TableSpec? Table = null,
        IReadOnlyList<SignatureLine>? Signatures = null);

    /// <summary>Те, що лягає в Template.BuilderJson. Version — щоб потім не гадати,
    /// яким кодом це писалося.</summary>
    public record TemplateBuilderDocument(
        IReadOnlyList<TemplateBlock> Blocks,
        int Version = TemplateBuilderDocument.CurrentVersion)
    {
        public const int CurrentVersion = 1;
    }
}
