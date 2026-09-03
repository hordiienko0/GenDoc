using GenDoc.Models;
using GenDoc.Models.Enums;
using GenDoc.Services;

namespace GenDoc.Tests;

public class UkrainianGrammarTests
{
    [Theory]
    [InlineData("Олександрович", Gender.Male)]
    [InlineData("Ігорович", Gender.Male)]
    [InlineData("Андрійович", Gender.Male)]
    [InlineData("Олександрівна", Gender.Female)]
    [InlineData("Іванівна", Gender.Female)]
    [InlineData("Andriyivna", Gender.Male)]
    public void Detect_FromPatronymicSuffix(string middleName, Gender expected)
    {
        var recipient = new Recipient { LastName = "Тест", FirstName = "Тест", MiddleName = middleName };
        Assert.Equal(expected, UkrainianGrammar.Detect(recipient));
    }

    [Fact]
    public void Detect_FallsBackToExplicitGender_WhenPatronymicMissing()
    {
        var recipient = new Recipient { LastName = "Тест", FirstName = "Тест", MiddleName = null, Gender = Gender.Female };
        Assert.Equal(Gender.Female, UkrainianGrammar.Detect(recipient));
    }

    [Fact]
    public void Detect_DefaultsToMale_WhenNothingKnown()
    {
        var recipient = new Recipient { LastName = "Тест", FirstName = "Тест" };
        Assert.Equal(Gender.Male, UkrainianGrammar.Detect(recipient));
    }

    [Theory]
    [InlineData("Шевченко", GrammaticalKind.Surname, Gender.Male, "Шевченка")]
    [InlineData("Шевченко", GrammaticalKind.Surname, Gender.Female, "Шевченко")]
    [InlineData("Ковальський", GrammaticalKind.Surname, Gender.Male, "Ковальського")]
    [InlineData("Ковальська", GrammaticalKind.Surname, Gender.Female, "Ковальську")]
    [InlineData("Кравчук", GrammaticalKind.Surname, Gender.Male, "Кравчука")]
    [InlineData("Кравчук", GrammaticalKind.Surname, Gender.Female, "Кравчук")]
    [InlineData("Мельник", GrammaticalKind.Surname, Gender.Male, "Мельника")]
    public void Accusative_Surname(string nominative, GrammaticalKind kind, Gender gender, string expected)
        => Assert.Equal(expected, UkrainianGrammar.Accusative(nominative, kind, gender));

    [Theory]
    [InlineData("Олександр", Gender.Male, "Олександра")]
    [InlineData("Юрій", Gender.Male, "Юрія")]
    [InlineData("Андрій", Gender.Male, "Андрія")]
    [InlineData("Павло", Gender.Male, "Павла")]
    [InlineData("Ольга", Gender.Female, "Ольгу")]
    [InlineData("Марія", Gender.Female, "Марію")]
    public void Accusative_GivenName(string nominative, Gender gender, string expected)
        => Assert.Equal(expected, UkrainianGrammar.Accusative(nominative, GrammaticalKind.GivenName, gender));

    [Theory]
    [InlineData("майор", Gender.Male, "майора")]
    [InlineData("капітан", Gender.Male, "капітана")]
    [InlineData("лейтенант", Gender.Male, "лейтенанта")]
    [InlineData("старший лейтенант", Gender.Male, "старшого лейтенанта")]
    [InlineData("молодший лейтенант", Gender.Male, "молодшого лейтенанта")]
    [InlineData("сержант", Gender.Male, "сержанта")]
    [InlineData("солдат", Gender.Male, "солдата")]
    public void Accusative_Rank(string nominative, Gender gender, string expected)
        => Assert.Equal(expected, UkrainianGrammar.Accusative(nominative, GrammaticalKind.Rank, gender));

    [Fact]
    public void ArrivedVerb_AgreesWithGender()
    {
        Assert.Equal("прибув", UkrainianGrammar.ArrivedVerb(Gender.Male));
        Assert.Equal("прибула", UkrainianGrammar.ArrivedVerb(Gender.Female));
    }

    [Fact]
    public void SuchPronoun_AgreesWithGender()
    {
        Assert.Equal("таким", UkrainianGrammar.SuchPronoun(Gender.Male));
        Assert.Equal("такою", UkrainianGrammar.SuchPronoun(Gender.Female));
    }

    public static IEnumerable<object[]> KoSurnames() => new[]
    {
        "Іваненко", "Петренко", "Бондаренко", "Кравченко", "Шевченко", "Ткаченко",
        "Гончаренко", "Захарченко", "Марченко", "Романенко", "Савченко", "Тимошенко",
        "Литвиненко", "Дяченко", "Клименко", "Науменко", "Панченко", "Руденко",
        "Сидоренко", "Степаненко", "Юрченко", "Яременко", "Бойко", "Гриценко",
        "Демченко", "Іщенко", "Коваленко", "Лисенко", "Онищенко", "Прокопенко",
    }.Select(s => new object[] { s });

    [Theory]
    [MemberData(nameof(KoSurnames))]
    public void Accusative_KoSuffixSurnames_DeclineForMale(string surname)
        => Assert.Equal(surname[..^1] + "а",
            UkrainianGrammar.Accusative(surname, GrammaticalKind.Surname, Gender.Male));

    [Theory]
    [MemberData(nameof(KoSurnames))]
    public void Accusative_KoSuffixSurnames_StayInvariantForFemale(string surname)
        => Assert.Equal(surname, UkrainianGrammar.Accusative(surname, GrammaticalKind.Surname, Gender.Female));

    [Theory]
    [InlineData("майор", "майора")]
    [InlineData("капітан", "капітана")]
    [InlineData("старший лейтенант", "старшого лейтенанта")]
    [InlineData("молодший сержант", "молодшого сержанта")]
    public void Accusative_Rank_DeclinesForFemaleToo(string nominative, string expected)
        => Assert.Equal(expected, UkrainianGrammar.Accusative(nominative, GrammaticalKind.Rank, Gender.Female));

    [Theory]
    [InlineData("капітан 1 рангу", "капітана 1 рангу")]
    [InlineData("капітан 2 рангу", "капітана 2 рангу")]
    [InlineData("капітан 3 рангу", "капітана 3 рангу")]
    public void Accusative_NavalRanks_DeclineTheHeadWord(string nominative, string expected)
        => Assert.Equal(expected, UkrainianGrammar.Accusative(nominative, GrammaticalKind.Rank, Gender.Male));

    [Theory]
    [InlineData("Ігор", "Ігоря")]
    [InlineData("Лазар", "Лазаря")]
    public void Accusative_SoftStemGivenNames(string nominative, string expected)
        => Assert.Equal(expected, UkrainianGrammar.Accusative(nominative, GrammaticalKind.GivenName, Gender.Male));

    [Theory]
    [InlineData("Ковальчук", "Ковальчука")]
    [InlineData("Мельник", "Мельника")]
    [InlineData("Кравчук", "Кравчука")]
    public void Accusative_ConsonantEndingSurnames_DeclineForMale(string surname, string expected)
        => Assert.Equal(expected, UkrainianGrammar.Accusative(surname, GrammaticalKind.Surname, Gender.Male));
}
