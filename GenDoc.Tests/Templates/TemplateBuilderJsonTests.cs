using GenDoc.Models.TemplateBuilder;
using GenDoc.Services.Templates;

namespace GenDoc.Tests.Templates;

// BuilderJson живе в базі роками: саме він, а не .docx, дає змогу відкрити шаблон
// у конструкторі повторно. Тому перевіряємо і зворотний розбір, і живучість формату.
public class TemplateBuilderJsonTests
{
    [Fact]
    public void Round_trip_keeps_blocks_text_and_signatories()
    {
        var document = new TemplateBuilderDocument(new[]
        {
            new TemplateBlock(TemplateBlockKind.Header, "ЗАТВЕРДЖУЮ\nНачальник курсу"),
            new TemplateBlock(TemplateBlockKind.Title, "АКТ"),
            new TemplateBlock(TemplateBlockKind.Signatures, Signatures: new[]
            {
                new SignatureLine("Прийняв", 7),
                new SignatureLine("Здав", null)
            })
        });

        var restored = TemplateBuilderJson.Deserialize(TemplateBuilderJson.Serialize(document));

        Assert.NotNull(restored);
        Assert.Equal(TemplateBuilderDocument.CurrentVersion, restored!.Version);
        Assert.Equal(3, restored.Blocks.Count);
        Assert.Equal("ЗАТВЕРДЖУЮ\nНачальник курсу", restored.Blocks[0].Text);
        Assert.Equal(TemplateBlockKind.Title, restored.Blocks[1].Kind);

        var signatures = restored.Blocks[2].Signatures!;
        Assert.Equal(7, signatures[0].RecipientId);
        Assert.Null(signatures[1].RecipientId);
        Assert.Equal("Здав", signatures[1].Caption);
    }

    // Enum'и пишемо рядками: інакше будь-яка зміна порядку TemplateBlockKind мовчки
    // перетворила б збережені шаблони на інші блоки.
    [Fact]
    public void Block_kind_is_written_as_a_name_not_a_number()
    {
        var json = TemplateBuilderJson.Serialize(new TemplateBuilderDocument(new[]
        {
            new TemplateBlock(TemplateBlockKind.Signatures)
        }));

        Assert.Contains("\"Signatures\"", json);
    }

    [Fact]
    public void Round_trip_keeps_block_style()
    {
        var document = new TemplateBuilderDocument(new[]
        {
            new TemplateBlock(TemplateBlockKind.Paragraph, "Текст", Style: new BlockStyle(
                FontFamily: "Arial", FontSize: 14, Bold: true, Italic: false,
                Color: "C00000", Alignment: BlockAlignment.Center))
        });

        var restored = TemplateBuilderJson.Deserialize(TemplateBuilderJson.Serialize(document));

        var style = restored!.Blocks[0].Style!;
        Assert.Equal("Arial", style.FontFamily);
        Assert.Equal(14, style.FontSize);
        Assert.True(style.Bold);
        // Явний false мусить пережити round-trip: він відрізняється від «не задано».
        Assert.False(style.Italic);
        Assert.Equal("C00000", style.Color);
        Assert.Equal(BlockAlignment.Center, style.Alignment);
    }

    // Шаблони, збережені до появи форматування, лежать у базі без поля Style.
    // Вони мусять читатися й давати рівно те оформлення, що й раніше.
    [Fact]
    public void Json_saved_before_formatting_existed_still_loads()
    {
        const string legacy = """
            {"Blocks":[{"Kind":"Title","Text":"АКТ","SheetIndex":0}],"Version":1,"Mode":"Word"}
            """;

        var restored = TemplateBuilderJson.Deserialize(legacy);

        var block = Assert.Single(restored!.Blocks);
        Assert.Null(block.Style);
        Assert.Equal(
            BlockStyleDefaults.Resolve(TemplateBlockKind.Title, null),
            BlockStyleDefaults.Resolve(block.Kind, block.Style));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("{зіпсований json")]
    public void Broken_or_missing_source_gives_null_instead_of_throwing(string? json)
    {
        // Порожній BuilderJson - звичайний випадок: шаблон завантажений файлом.
        // Зіпсований - аварійний, але й він не має валити екран шаблонів.
        Assert.Null(TemplateBuilderJson.Deserialize(json));
    }
}
