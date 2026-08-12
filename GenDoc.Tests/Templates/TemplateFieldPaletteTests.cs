using System.Text.Json;
using GenDoc.Models.Enums;
using GenDoc.Models.TemplateBuilder;
using GenDoc.Services.Templates;

namespace GenDoc.Tests.Templates;

public class TemplateFieldPaletteTests
{
    // Суть палітри: вона не має власного переліку тегів. Якщо колись хтось додасть
    // тег у мапу, він мусить з'явитися в конструкторі сам.
    [Fact]
    public void Every_offered_field_is_classified_as_its_group_promises()
    {
        foreach (var group in TemplateFieldPalette.Build())
        {
            foreach (var field in group.Fields)
            {
                var (sourceType, _) = PlaceholderTagMaps.Classify(field.Tag);
                Assert.Equal(group.SourceType, sourceType);
            }
        }
    }

    [Fact]
    public void Recipient_group_offers_exactly_the_recipient_map()
    {
        var group = TemplateFieldPalette.Build()
            .Single(g => g.SourceType == MappingSourceType.Recipient);

        var expected = PlaceholderTagMaps.RecipientTagMap.Keys
            .Select(TemplateFieldPalette.Wrap)
            .Distinct()
            .OrderBy(t => t, StringComparer.Ordinal);

        Assert.Equal(expected, group.Fields.Select(f => f.Tag));
    }

    [Fact]
    public void Manual_fields_are_absent_from_both_maps()
    {
        var group = TemplateFieldPalette.Build()
            .Single(g => g.SourceType == MappingSourceType.Manual);

        Assert.NotEmpty(group.Fields);
        foreach (var field in group.Fields)
        {
            var inner = field.Tag.Trim('{', '}');
            Assert.False(PlaceholderTagMaps.RecipientTagMap.ContainsKey(inner));
            Assert.False(PlaceholderTagMaps.OrganizationTagMap.ContainsKey(inner));
        }
    }

    [Fact]
    public void Tags_are_wrapped_in_double_braces()
    {
        Assert.Equal("{{піб}}", TemplateFieldPalette.Wrap("піб"));
    }
}

public class TemplateBuilderDocumentTests
{
    [Fact]
    public void Survives_a_json_round_trip()
    {
        var document = new TemplateBuilderDocument(new[]
        {
            new TemplateBlock(TemplateBlockKind.Title, "АКТ приймання на зберігання"),
            new TemplateBlock(TemplateBlockKind.Paragraph, "Я, {{звання}} {{піб}}, передав."),
            new TemplateBlock(TemplateBlockKind.Signatures, Signatures: new[]
            {
                new SignatureLine("Здав", null),
                new SignatureLine("Прийняв", 42)
            })
        });

        var json = JsonSerializer.Serialize(document);
        var restored = JsonSerializer.Deserialize<TemplateBuilderDocument>(json);

        Assert.NotNull(restored);
        Assert.Equal(TemplateBuilderDocument.CurrentVersion, restored!.Version);
        Assert.Equal(3, restored.Blocks.Count);
        Assert.Equal("Я, {{звання}} {{піб}}, передав.", restored.Blocks[1].Text);
        Assert.Equal(42, restored.Blocks[2].Signatures![1].RecipientId);
    }

    [Fact]
    public void Blocks_carry_only_the_fields_their_kind_needs()
    {
        var title = new TemplateBlock(TemplateBlockKind.Title, "Заголовок");

        Assert.Null(title.Table);
        Assert.Null(title.Signatures);
    }
}
