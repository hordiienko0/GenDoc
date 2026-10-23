using GenDoc.Services.Generation;
using GenDoc.ViewModels.Generation;

namespace GenDoc.Tests.Generation;

public class ManualTagOrderTests
{
    [Fact]
    public void NumberedTags_GoInNumericOrder()
    {
        var tags = new List<string>
        {
            "{{набої_вправа_10}}", "{{набої_вправа_2}}", "{{набої_вправа_1}}", "{{калібр}}", "{{набої_вправа_16}}"
        };

        tags.Sort(ManualTagOrder.Instance);

        Assert.Equal(new[]
        {
            "{{калібр}}", "{{набої_вправа_1}}", "{{набої_вправа_2}}", "{{набої_вправа_10}}", "{{набої_вправа_16}}"
        }, tags);
    }

    [Fact]
    public void NumberInTheMiddle_IsComparedAsANumber()
    {
        var tags = new List<string> { "{{начальник_пбп_10_зв}}", "{{начальник_пбп_2_зв}}", "{{начальник_пбп}}" };

        tags.Sort(ManualTagOrder.Instance);

        Assert.Equal(new[] { "{{начальник_пбп}}", "{{начальник_пбп_2_зв}}", "{{начальник_пбп_10_зв}}" }, tags);
    }

    [Fact]
    public void OnlyDatesAndPeriods_GetTheDateFormatHint()
    {
        Assert.Null(new ManualTagRowViewModel("{{калібр}}", null).InputHint);
        Assert.NotNull(ManualTagRowViewModel.Period("{{період}}", null).InputHint);
        Assert.NotNull(new ManualTagRowViewModel("{{дата_прибуття}}", new DateOnly(2026, 10, 12), d => d.ToString()).InputHint);
    }
}
