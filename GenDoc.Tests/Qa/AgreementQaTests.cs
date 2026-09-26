using GenDoc.Models.Enums;
using GenDoc.Services;
using GenDoc.Tests.Infrastructure;
using static GenDoc.Tests.Qa.QaKomplekt;

namespace GenDoc.Tests.Qa;

/// <summary>
/// QA 2026-09-26, блок А6: відмінки й узгодження за родом у справжніх персональних рапортах
/// комплектів «Котлове забезпечення» і «Відпустки та лікування».
/// </summary>
public class AgreementQaTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), $"gqg-{Guid.NewGuid():N}"[..14]);

    public void Dispose()
    {
        if (Directory.Exists(_folder)) Directory.Delete(_folder, recursive: true);
    }

    /// <summary>Персональні рапорти для ШЕВЧЕНКА і ПЕТРОВОЇ; повертає текст файлу (шаблон, прізвище).</summary>
    private async Task<Func<string, string, string>> GenerateReports()
    {
        var db = new TestDb();
        try
        {
            var seeded = SeedPeople(db, _folder);
            Importer(db).ImportFolder(SetFolder("Котлове забезпечення"));
            Importer(db).ImportFolder(SetFolder("Відпустки та лікування"));

            List<int> templateIds;
            using (var ctx = db.NewContext())
                templateIds = ctx.Templates.Where(t => t.Kind == TemplateKind.PerRecipient).Select(t => t.Id).ToList();

            var generation = TestServices.Generation(db);
            var values = await FormValues(db, generation.GetManualTagsForTemplates(templateIds, Array.Empty<int>()), "04.08.2026");
            var result = generation.GenerateTemplatesForRecipients(templateIds, Array.Empty<int>(),
                new[] { seeded.IdBySurname["ШЕВЧЕНКО"], seeded.IdBySurname[FemaleSurname] }, _folder, values, NoProgress);
            Assert.Equal(0, result.Errors);
        }
        finally
        {
            db.Dispose();
        }

        var files = Files(_folder, ".docx");
        return (template, surname) => DocxText(Assert.Single(files,
            f => f.Contains(template, StringComparison.Ordinal)
                 && Path.GetFileName(f).Contains(surname, StringComparison.Ordinal)));
    }

    [Fact]
    public async Task A6_Enrollment_Accusative_And_ArrivedSuch_AgreeWithGender()
    {
        var text = await GenerateReports();
        const string report = "зарахування на котлове забезпечення (індивідуальний)";

        var male = text(report, "ШЕВЧЕНКО");
        Assert.Contains("майора ШЕВЧЕНКА Андрія Петровича", male);
        Assert.Contains("прибув", male);
        Assert.Contains("таким", male);

        var female = text(report, FemaleSurname);
        Assert.Contains("лейтенанта ПЕТРОВУ Олену Іванівну", female);
        Assert.Contains("прибула", female);
        Assert.Contains("такою", female);
        Assert.DoesNotContain("ПЕТРОВА", female);
        Assert.DoesNotContain("таким", female);
    }

    [Fact]
    public async Task A6_ResumedDuty_Verb_AgreesWithGender()
    {
        var text = await GenerateReports();
        const string report = "після стаціонарного лікування";

        Assert.Contains("приступив", text(report, "ШЕВЧЕНКО"));
        var female = text(report, FemaleSurname);
        Assert.Contains("приступила", female);
        Assert.Contains("ПЕТРОВУ Олену Іванівну", female);
    }

    [Fact]
    public async Task A6_SickLeave_Dative_AndAcquainted_AgreeWithGender()
    {
        var text = await GenerateReports();
        const string report = "Рапорт на відпустку для лікування";

        var male = text(report, "ШЕВЧЕНКО");
        Assert.Contains("майору ШЕВЧЕНКУ Андрію Петровичу", male);
        Assert.Contains("ознайомлений", male);
        Assert.Contains("Андрій ШЕВЧЕНКО", male);

        var female = text(report, FemaleSurname);
        Assert.Contains("ознайомлена", female);
        Assert.Contains("Олена ПЕТРОВА", female);
        // Було (виправлено 2026-09-26): прізвище на -ова відмінюється як іменник («ПЕТРОВІ»), а не як прикметник («ПЕТРОВІЙ»).
        var addressee = System.Text.RegularExpressions.Regex.Match(female, @"лейтенанту \S+ Олені Іванівні").Value;
        Assert.Equal("лейтенанту ПЕТРОВІЙ Олені Іванівні", addressee);
    }

    // Прізвища прикметникового типу на -ова/-єва/-іна/-їна в давальному: -ій.
    [Theory]
    [InlineData("Петрова", "Петровій")]
    [InlineData("Соловйова", "Соловйовій")]
    [InlineData("Кузьміна", "Кузьміній")]
    [InlineData("ІВАНОВА", "ІВАНОВІЙ")]
    public void A6_Dative_FemaleSurnames_OnOvaIna_DeclineAsAdjectives(string nominative, string expected)
    {
        // Було (виправлено 2026-09-26): DeclineWordDative знає лише -ська/-цька/-зька, решта жіночих на -а йде «-і» (Петрові).
        Assert.Equal(expected, UkrainianGrammar.Dative(nominative, GrammaticalKind.Surname, Gender.Female));
    }

    // Прізвища на -ець зі змінним «е»: Кравець → Кравця/Кравцю, Горобець → Горобця/Горобцю.
    [Theory]
    [InlineData("Кравець", "Кравця", "Кравцю")]
    [InlineData("КРАВЕЦЬ", "КРАВЦЯ", "КРАВЦЮ")]
    [InlineData("Горобець", "Горобця", "Горобцю")]
    [InlineData("Стрілець", "Стрільця", "Стрільцю")]
    [InlineData("Швець", "Шевця", "Шевцю")]
    public void A6_MaleSurnames_OnEts_DropTheVowel(string nominative, string accusative, string dative)
    {
        // Було (виправлено 2026-09-26): закінчення -ь просто міняється на -я/-ю без випадання «е»: «Кравеця», «Кравецю».
        Assert.Equal(accusative, UkrainianGrammar.Accusative(nominative, GrammaticalKind.Surname, Gender.Male));
        Assert.Equal(dative, UkrainianGrammar.Dative(nominative, GrammaticalKind.Surname, Gender.Male));
    }

    [Fact]
    public void A6_Gender_FromPatronymic_BeatsCardField()
    {
        var woman = new GenDoc.Models.Recipient { LastName = "Коваль", FirstName = "Олена", MiddleName = "Іванівна", Gender = null };
        var man = new GenDoc.Models.Recipient { LastName = "Коваль", FirstName = "Юрій", MiddleName = "Іванович", Gender = Gender.Female };

        Assert.Equal(Gender.Female, UkrainianGrammar.Detect(woman));
        Assert.Equal(Gender.Male, UkrainianGrammar.Detect(man));
    }
}
