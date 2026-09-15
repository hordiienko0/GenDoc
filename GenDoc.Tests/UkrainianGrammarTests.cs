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
// ---------------------------------------------------------------------
    // Давальний відмінок: «надати капітану БУГАЮ Сергію Михайловичу»
    // ---------------------------------------------------------------------

    [Theory]
    [InlineData("капітан", "капітану")]
    [InlineData("майор", "майору")]
    [InlineData("підполковник", "підполковнику")]
    [InlineData("старший лейтенант", "старшому лейтенанту")]
    [InlineData("молодший лейтенант", "молодшому лейтенанту")]
    public void Dative_Ranks(string nominative, string expected)
        => Assert.Equal(expected, UkrainianGrammar.Dative(nominative, GrammaticalKind.Rank, Gender.Male));

    [Theory]
    [InlineData("Бугай", "Бугаю")]
    [InlineData("Веремеєнко", "Веремеєнку")]
    [InlineData("Степанов", "Степанову")]
    [InlineData("Ковальський", "Ковальському")]
    public void Dative_MaleSurnames(string nominative, string expected)
        => Assert.Equal(expected, UkrainianGrammar.Dative(nominative, GrammaticalKind.Surname, Gender.Male));

    [Theory]
    [InlineData("Сергій", "Сергію")]
    [InlineData("Володимир", "Володимиру")]
    [InlineData("Ігор", "Ігорю")]
    public void Dative_MaleGivenNames(string nominative, string expected)
        => Assert.Equal(expected, UkrainianGrammar.Dative(nominative, GrammaticalKind.GivenName, Gender.Male));

    [Theory]
    [InlineData("Михайлович", "Михайловичу")]
    [InlineData("Олександрович", "Олександровичу")]
    public void Dative_MalePatronymics(string nominative, string expected)
        => Assert.Equal(expected, UkrainianGrammar.Dative(nominative, GrammaticalKind.Patronymic, Gender.Male));

    [Fact]
    public void Dative_FemaleSurnameEndingInKo_DoesNotChange()
        => Assert.Equal("Веремеєнко", UkrainianGrammar.Dative("Веремеєнко", GrammaticalKind.Surname, Gender.Female));
[Theory]
    [InlineData("Ковальська", "Ковальській")]
    [InlineData("Шевченко", "Шевченко")]
    public void Dative_FemaleSurnames(string nominative, string expected)
        => Assert.Equal(expected, UkrainianGrammar.Dative(nominative, GrammaticalKind.Surname, Gender.Female));

    [Theory]
    [InlineData("Олена", "Олені")]
    [InlineData("Ольга", "Ользі")]
    [InlineData("Марія", "Марії")]
    public void Dative_FemaleGivenNames(string nominative, string expected)
        => Assert.Equal(expected, UkrainianGrammar.Dative(nominative, GrammaticalKind.GivenName, Gender.Female));

    [Fact]
    public void Dative_FemalePatronymic()
        => Assert.Equal("Іванівні", UkrainianGrammar.Dative("Іванівна", GrammaticalKind.Patronymic, Gender.Female));

    [Fact]
    public void Dative_MaleSurnameEndingInA()
        => Assert.Equal("Лиштві", UkrainianGrammar.Dative("Лиштва", GrammaticalKind.Surname, Gender.Male));

    // ---------------------------------------------------------------------
    // Посада: відмінюється головне слово, залежні слова лишаються
    // ---------------------------------------------------------------------

    [Theory]
    [InlineData("курсовий офіцер курсів перепідготовки та підвищення кваліфікації",
                "курсового офіцера курсів перепідготовки та підвищення кваліфікації")]
    [InlineData("начальник служби радіаційного, хімічного, біологічного захисту військової частини А4457",
                "начальника служби радіаційного, хімічного, біологічного захисту військової частини А4457")]
    [InlineData("заступник командира 6 зенітної кулеметної роти",
                "заступника командира 6 зенітної кулеметної роти")]
    [InlineData("начальник медичної служби - начальник медичного пункту",
                "начальника медичної служби - начальника медичного пункту")]
    public void PositionAccusative_DeclinesOnlyTheHead(string nominative, string expected)
        => Assert.Equal(expected, UkrainianGrammar.PositionAccusative(nominative, Gender.Male));

    [Theory]
    [InlineData("курсовий офіцер курсів перепідготовки та підвищення кваліфікації",
                "курсовому офіцеру курсів перепідготовки та підвищення кваліфікації")]
    [InlineData("начальник курсів перепідготовки та підвищення кваліфікації",
                "начальнику курсів перепідготовки та підвищення кваліфікації")]
    [InlineData("командир взводу перехоплювачів безпілотних літальних апаратів",
                "командиру взводу перехоплювачів безпілотних літальних апаратів")]
    public void PositionDative_DeclinesOnlyTheHead(string nominative, string expected)
        => Assert.Equal(expected, UkrainianGrammar.PositionDative(nominative, Gender.Male));

    [Fact]
    public void PositionDeclension_EmptyStaysEmpty()
    {
        Assert.Equal(string.Empty, UkrainianGrammar.PositionAccusative(string.Empty, Gender.Male));
        Assert.Equal(string.Empty, UkrainianGrammar.PositionDative(string.Empty, Gender.Male));
    }

    [Theory]
    [InlineData(Gender.Male, "приступив")]
    [InlineData(Gender.Female, "приступила")]
    public void ResumedDutyVerb_AgreesWithGender(Gender gender, string expected)
        => Assert.Equal(expected, UkrainianGrammar.ResumedDutyVerb(gender));

    // ---------------------------------------------------------------------
    // Прізвища великими літерами, як їх зберігає картка: «КОВАЛЬСЬКА», «ЗАЯЦЬ»
    // ---------------------------------------------------------------------

    [Theory]
    [InlineData("КОВАЛЬСЬКА", Gender.Female, "КОВАЛЬСЬКУ")]
    [InlineData("ЛИШТВА", Gender.Male, "ЛИШТВУ")]
    [InlineData("ЗАЯЦЬ", Gender.Male, "ЗАЯЦЯ")]
    [InlineData("СТЕПАНОВ", Gender.Male, "СТЕПАНОВА")]
    [InlineData("ШЕВЧЕНКО", Gender.Female, "ШЕВЧЕНКО")]
    public void Accusative_UppercaseSurnames_DeclineLikeMixedCase(string nominative, Gender gender, string expected)
        => Assert.Equal(expected, UkrainianGrammar.Accusative(nominative, GrammaticalKind.Surname, gender));

    [Theory]
    [InlineData("ЛИШТВА", Gender.Male, "ЛИШТВІ")]
    [InlineData("КОВАЛЬСЬКА", Gender.Female, "КОВАЛЬСЬКІЙ")]
    [InlineData("ЗАЯЦЬ", Gender.Male, "ЗАЯЦЮ")]
    public void Dative_UppercaseSurnames_DeclineLikeMixedCase(string nominative, Gender gender, string expected)
        => Assert.Equal(expected, UkrainianGrammar.Dative(nominative, GrammaticalKind.Surname, gender));
}
