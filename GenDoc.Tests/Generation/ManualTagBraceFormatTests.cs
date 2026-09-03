using GenDoc.ViewModels.Generation;
using GenDoc.Services.Generation;

namespace GenDoc.Tests.Generation;

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
