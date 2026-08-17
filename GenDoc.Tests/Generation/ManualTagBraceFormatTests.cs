using GenDoc.ViewModels.Generation;
using GenDoc.Services.Generation;

namespace GenDoc.Tests.Generation;

// Теги приходять із БД у полі PlaceholderTag — а там вони записані З ДУЖКАМИ
// ({{звання_підписанта}}), бо сканер кладе match.Value регексу цілком. Константи
// ж і префіли писалися без дужок, тому жодне порівняння не збігалося:
// підписант ставав парою звичайних текстових полів, а дата — рядком без пікера.
//
// Мовчазна вада: форма показувалась, значення підставлялись, просто вручну — і
// саме тому її не було видно ні з коду, ні з тестів.
public class ManualTagBraceFormatTests
{
    [Fact]
    public void HasSignerPair_RecognisesTagsInTheFormTheDatabaseStoresThem()
    {
        var tags = new List<string> { "{{звання_підписанта}}", "{{піб_підписанта}}", "{{калібр}}" };

        Assert.True(ManualTagClassifier.HasSignerPair(tags));
    }

    [Fact]
    public void HasSignerPair_StillWorksForBareTags()
    {
        var tags = new List<string> { "звання_підписанта", "піб_підписанта" };

        Assert.True(ManualTagClassifier.HasSignerPair(tags));
    }

    [Fact]
    public void HasSignerPair_FalseWhenOnlyOneHalfOfThePairIsPresent()
    {
        var tags = new List<string> { "{{звання_підписанта}}", "{{калібр}}" };

        Assert.False(ManualTagClassifier.HasSignerPair(tags));
    }

    [Theory]
    [InlineData("{{дата_прибуття}}")]
    [InlineData("{{дата_зарахування}}")]
    [InlineData("{{дата_рапорту}}")]
    public void Classify_TreatsBracedDateTagsAsDates(string tag)
    {
        Assert.Equal(ManualTagKind.Date, ManualTagClassifier.Classify(tag));
    }

    [Fact]
    public void Classify_LeavesOrdinaryTagsAsText()
    {
        Assert.Equal(ManualTagKind.Text, ManualTagClassifier.Classify("{{калібр}}"));
    }

    [Fact]
    public void PrefillDate_AcceptsBracedTags()
    {
        var arrival = new DateOnly(2026, 8, 6);

        Assert.Equal(arrival, ManualTagClassifier.PrefillDate("{{дата_прибуття}}", arrival));
        Assert.Equal(arrival.AddDays(1), ManualTagClassifier.PrefillDate("{{дата_зарахування}}", arrival));
    }

    // Найтихіша половина вади: навіть обраний підписант не доїхав би до
    // документа, бо форма кладе значення під ключем БЕЗ дужок, а генерація
    // шукає його за PlaceholderTag — тобто з дужками.
    [Fact]
    public void GetValues_ReturnsSignerUnderTheSameKeyTheTemplateUses()
    {
        var picker = new SignerPickerViewModel(
            new[] { new StaffPickerOption(1, "капітан", "Василь КОВАЛЬЧУК", "капітан Василь КОВАЛЬЧУК") },
            new StaffPickerOption(1, "капітан", "Василь КОВАЛЬЧУК", "капітан Василь КОВАЛЬЧУК"));

        var form = new ManualTagFormViewModel(
            new System.Collections.ObjectModel.ObservableCollection<ManualTagRowViewModel>(),
            picker,
            courseOfficer: null,
            signerRankTag: "{{звання_підписанта}}",
            signerNameTag: "{{піб_підписанта}}");

        var values = form.GetValues();

        Assert.Equal("капітан", values["{{звання_підписанта}}"]);
        Assert.Equal("Василь КОВАЛЬЧУК", values["{{піб_підписанта}}"]);
    }
}
