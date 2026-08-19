using GenDoc.Models.TemplateBuilder;
using GenDoc.Services.Templates;

namespace GenDoc.Tests.Templates;

public class BlockStyleDefaultsTests
{
    // Типові значення тут - не смак, а те, що раніше було зашите константами в
    // кожному writer'і окремо. Якщо вони поїдуть, поїде і вигляд усіх уже
    // збережених шаблонів, у яких стилю немає взагалі.
    [Theory]
    [InlineData(TemplateBlockKind.Header, BlockAlignment.Right, false)]
    [InlineData(TemplateBlockKind.Title, BlockAlignment.Center, true)]
    [InlineData(TemplateBlockKind.DateAndCity, BlockAlignment.Justify, false)]
    [InlineData(TemplateBlockKind.Paragraph, BlockAlignment.Justify, false)]
    [InlineData(TemplateBlockKind.Signatures, BlockAlignment.Left, false)]
    [InlineData(TemplateBlockKind.Table, BlockAlignment.Left, false)]
    public void Unset_style_gives_the_default_of_the_kind(
        TemplateBlockKind kind, BlockAlignment alignment, bool bold)
    {
        var style = BlockStyleDefaults.Resolve(kind, null);

        Assert.Equal(alignment, style.Alignment);
        Assert.Equal(bold, style.Bold);
        Assert.False(style.Italic);
    }

    // «Нічого не задано» мусить лишитися саме нічим, а не перетворитися на
    // вигаданий шрифт: у .docx це різниця між «Word бере своє з docDefaults» і
    // «writer нав'язав гарнітуру, якої оператор не обирав».
    [Fact]
    public void Unset_font_size_and_colour_stay_null()
    {
        var style = BlockStyleDefaults.Resolve(TemplateBlockKind.Paragraph, null);

        Assert.Null(style.FontFamily);
        Assert.Null(style.FontSize);
        Assert.Null(style.Color);
    }

    [Fact]
    public void Empty_style_object_behaves_like_no_style()
    {
        Assert.Equal(
            BlockStyleDefaults.Resolve(TemplateBlockKind.Title, null),
            BlockStyleDefaults.Resolve(TemplateBlockKind.Title, new BlockStyle()));
    }

    [Fact]
    public void Partial_style_overrides_only_what_it_sets()
    {
        var style = BlockStyleDefaults.Resolve(
            TemplateBlockKind.Title, new BlockStyle(FontSize: 16));

        Assert.Equal(16, style.FontSize);
        // Заголовок лишається жирним і центрованим - задали ж лише кегль.
        Assert.True(style.Bold);
        Assert.Equal(BlockAlignment.Center, style.Alignment);
    }

    // Без явного false зняти жирність із заголовка було б неможливо: типове
    // значення для нього - true.
    [Fact]
    public void Explicit_false_removes_the_default_bold()
    {
        var style = BlockStyleDefaults.Resolve(
            TemplateBlockKind.Title, new BlockStyle(Bold: false));

        Assert.False(style.Bold);
    }

    [Fact]
    public void Table_header_stays_bold_even_when_the_block_is_not()
    {
        var block = BlockStyleDefaults.Resolve(
            TemplateBlockKind.Table, new BlockStyle(Bold: false, FontFamily: "Arial"));

        var header = BlockStyleDefaults.ForTableHeader(block, BlockAlignment.Center);

        Assert.True(header.Bold);
        Assert.Equal(BlockAlignment.Center, header.Alignment);
        // Гарнітуру шапка все ж успадковує - структурні тут лише жирність
        // і вирівнювання.
        Assert.Equal("Arial", header.FontFamily);
    }

    [Fact]
    public void Empty_style_is_recognised_as_empty()
    {
        Assert.True(new BlockStyle().IsEmpty);
        Assert.False(new BlockStyle(Bold: false).IsEmpty);
        Assert.False(new BlockStyle(FontSize: 14).IsEmpty);
    }
}
