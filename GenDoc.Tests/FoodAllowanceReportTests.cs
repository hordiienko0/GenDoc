using GenDoc.Services;
using GenDoc.Services.Generation;
using GenDoc.ViewModels.Generation;

namespace GenDoc.Tests;

public class UkrainianDateTests
{
    [Theory]
    [InlineData(2026, 7, 15, "15 липня 2026 року")]
    [InlineData(2026, 1, 1, "1 січня 2026 року")]
    [InlineData(2026, 12, 31, "31 грудня 2026 року")]
    [InlineData(2026, 3, 9, "9 березня 2026 року")]
    public void Long_FormatsGenitiveUkrainianDate(int year, int month, int day, string expected)
        => Assert.Equal(expected, UkrainianDate.Long(new DateOnly(year, month, day)));
}

public class SignatureNameTests
{
    [Fact]
    public void SignatureName_GivenNameFirstSurnameUppercased()
        => Assert.Equal("Сергій ПОНОМАРЕНКО", NameFormatter.SignatureName("Пономаренко", "Сергій"));

    [Fact]
    public void SignatureName_DiffersFromFullNameFormat()
    {
        // ПІБ-формат (FullNameFormatted) — прізвище першим; SignatureName — ім'я першим.
        var signature = NameFormatter.SignatureName("Іваненко", "Олег");
        Assert.StartsWith("Олег", signature);
        Assert.EndsWith("ІВАНЕНКО", signature);
    }
}

public class ManualTagClassifierTests
{
    [Theory]
    [InlineData("дата_прибуття", ManualTagKind.Date)]
    [InlineData("дата_зарахування", ManualTagKind.Date)]
    [InlineData("дата_рапорту", ManualTagKind.Date)]
    [InlineData("дата_посвідчення", ManualTagKind.Text)] // не в manual-формі — це Recipient-джерело, а не ручна дата
    [InlineData("номер_посвідчення", ManualTagKind.Text)]
    [InlineData("звання_підписанта", ManualTagKind.Text)]
    public void Classify_ReturnsExpectedKind(string tag, ManualTagKind expected)
        => Assert.Equal(expected, ManualTagClassifier.Classify(tag));

    [Fact]
    public void HasSignerPair_TrueOnlyWhenBothTagsPresent()
    {
        Assert.True(ManualTagClassifier.HasSignerPair(new[] { "звання_підписанта", "піб_підписанта", "дата_рапорту" }));
        Assert.False(ManualTagClassifier.HasSignerPair(new[] { "звання_підписанта" }));
        Assert.False(ManualTagClassifier.HasSignerPair(new[] { "піб_підписанта" }));
        Assert.False(ManualTagClassifier.HasSignerPair(Array.Empty<string>()));
    }

    [Fact]
    public void PrefillDate_ArrivalAndEnrollmentAndReport()
    {
        var arrival = new DateOnly(2026, 7, 15);

        Assert.Equal(arrival, ManualTagClassifier.PrefillDate("дата_прибуття", arrival));
        Assert.Equal(arrival.AddDays(1), ManualTagClassifier.PrefillDate("дата_зарахування", arrival));
        Assert.Equal(DateOnly.FromDateTime(DateTime.Today), ManualTagClassifier.PrefillDate("дата_рапорту", arrival));
    }

    [Fact]
    public void PrefillDate_UnknownTag_Throws()
        => Assert.Throws<ArgumentException>(() => ManualTagClassifier.PrefillDate("щось_інше", new DateOnly(2026, 1, 1)));

    [Fact]
    public void FormatDate_ReportDateIsShortForm_OthersAreLongForm()
    {
        var date = new DateOnly(2026, 7, 15);

        Assert.Equal("15.07.2026", ManualTagClassifier.FormatDate("дата_рапорту", date));
        Assert.Equal("15 липня 2026 року", ManualTagClassifier.FormatDate("дата_прибуття", date));
        Assert.Equal("15 липня 2026 року", ManualTagClassifier.FormatDate("дата_зарахування", date));
    }
}

public class ManualTagFormViewModelTests
{
    [Fact]
    public void GetValues_IncludesTextAndDateRows()
    {
        var rows = new System.Collections.ObjectModel.ObservableCollection<ManualTagRowViewModel>
        {
            new("дата_рапорту", new DateOnly(2026, 7, 15), d => ManualTagClassifier.FormatDate("дата_рапорту", d)),
            new("довільний_текст", "значення")
        };
        var form = new ManualTagFormViewModel(rows, null);

        var values = form.GetValues();

        Assert.Equal("15.07.2026", values["дата_рапорту"]);
        Assert.Equal("значення", values["довільний_текст"]);
    }

    [Fact]
    public void GetValues_ExpandsSelectedSignerIntoTwoTags()
    {
        var options = new[] { new StaffPickerOption(1, "майор", "Іван ІВАНЕНКО", "майор Іван ІВАНЕНКО") };
        var signer = new SignerPickerViewModel(options, options[0]);
        var form = new ManualTagFormViewModel(new System.Collections.ObjectModel.ObservableCollection<ManualTagRowViewModel>(), signer);

        var values = form.GetValues();

        Assert.Equal("майор", values[ManualTagFormViewModel.SignerRankTag]);
        Assert.Equal("Іван ІВАНЕНКО", values[ManualTagFormViewModel.SignerNameTag]);
    }

    [Fact]
    public void GetValues_NoSignerSelected_DoesNotAddSignerTags()
    {
        var signer = new SignerPickerViewModel(Array.Empty<StaffPickerOption>(), null);
        var form = new ManualTagFormViewModel(new System.Collections.ObjectModel.ObservableCollection<ManualTagRowViewModel>(), signer);

        var values = form.GetValues();

        Assert.False(values.ContainsKey(ManualTagFormViewModel.SignerRankTag));
        Assert.False(values.ContainsKey(ManualTagFormViewModel.SignerNameTag));
    }

    [Fact]
    public void HasContent_FalseWhenEmpty_TrueWithRowsOrSigner()
    {
        var empty = new ManualTagFormViewModel(new System.Collections.ObjectModel.ObservableCollection<ManualTagRowViewModel>(), null);
        Assert.False(empty.HasContent);

        var withRow = new ManualTagFormViewModel(
            new System.Collections.ObjectModel.ObservableCollection<ManualTagRowViewModel> { new("тег", "значення") }, null);
        Assert.True(withRow.HasContent);

        var withSigner = new ManualTagFormViewModel(
            new System.Collections.ObjectModel.ObservableCollection<ManualTagRowViewModel>(),
            new SignerPickerViewModel(Array.Empty<StaffPickerOption>(), null));
        Assert.True(withSigner.HasContent);
    }
}
