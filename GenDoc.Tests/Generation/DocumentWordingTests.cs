using ClosedXML.Excel;
using GenDoc.Models;
using GenDoc.Models.Enums;
using GenDoc.Services;
using GenDoc.Services.Generation;
using GenDoc.Tests.Infrastructure;

namespace GenDoc.Tests.Generation;

/// <summary>
/// Вади, знайдені вичиткою згенерованих документів 2026-09-27: посада над підписом, узгодження з числом
/// і родом, «№» посвідчення, формат дати у відомостях.
/// </summary>
public class DocumentWordingTests
{
    private const int UserId = 1;

    private static ManualTagFormBuilder Builder(TestDb db, string? commanderPosition)
    {
        using (var ctx = db.Factory.CreateDbContext())
        {
            ctx.Users.Add(new UserProfile { Id = UserId, FullName = "Курсовий", PasswordHash = "x", CreatedAt = DateTime.Now });
            ctx.OrganizationSettings.Add(new OrganizationSettings
            {
                UnitNumber = "А0000", City = "Харків",
                CommanderRank = "полковник", CommanderFullName = "Іван ПЕТРЕНКО", CommanderPosition = commanderPosition ?? string.Empty
            });
            ctx.Recipients.Add(new Recipient
            {
                LastName = "КОВАЛЬЧУК", FirstName = "Василь", Rank = "капітан", Position = "курсовий офіцер", IsCourseOfficer = true
            });
            ctx.SaveChanges();
        }
        return TestServices.ManualTagForm(db, TestServices.Staff(db, UserId), new FakeCurrentUser(), new FakeIntakeAccessor(), UserId);
    }

    private static readonly string[] SignerTags = { "{{звання_підписанта}}", "{{піб_підписанта}}", "{{посада_підписанта}}" };

    [Fact]
    public async Task SignerPosition_FollowsTheChosenSigner()
    {
        using var db = new TestDb();
        var form = await Builder(db, commanderPosition: null).BuildAsync(SignerTags, "pkg:1");

        // Посада - не окреме поле: її дає вибір підписанта.
        Assert.Empty(form.Rows);

        var head = form.GetValues();
        Assert.Equal("полковник", head["{{звання_підписанта}}"]);
        Assert.Equal(HeadOfCourses.DefaultFormalPosition, head["{{посада_підписанта}}"]);

        form.Signer!.Selected = form.Signer.Options.Single(o => o.RecipientId != HeadOfCourses.PickerId);
        var officer = form.GetValues();
        Assert.Equal("капітан", officer["{{звання_підписанта}}"]);
        Assert.Equal(
            "Курсовий офіцер курсів перепідготовки та підвищення кваліфікації Харківського національного університету Повітряних Сил",
            officer["{{посада_підписанта}}"]);
    }

    [Fact]
    public async Task SignerPosition_ComesFromSettings_AndTheOfficerOneIsDerivedFromIt()
    {
        using var db = new TestDb();
        var form = await Builder(db, "Начальник курсів підвищення кваліфікації ХНУПС").BuildAsync(SignerTags, "pkg:1");

        Assert.Equal("Начальник курсів підвищення кваліфікації ХНУПС", form.GetValues()["{{посада_підписанта}}"]);

        form.Signer!.Selected = form.Signer.Options.Single(o => o.RecipientId != HeadOfCourses.PickerId);
        Assert.Equal("Курсовий офіцер курсів підвищення кваліфікації ХНУПС", form.GetValues()["{{посада_підписанта}}"]);
    }

    [Theory]
    [InlineData(1, "учасника")]
    [InlineData(3, "учасників")]
    [InlineData(11, "учасників")]
    [InlineData(21, "учасника")]
    [InlineData(28, "учасників")]
    public void ParticipantsWord_AgreesWithTheNumber(int count, string expected)
    {
        Assert.Equal(expected, UkrainianGrammar.ParticipantsWord(count));
    }

    [Fact]
    public void LeftVerb_AgreesWithGender()
    {
        Assert.Equal("вибув", UkrainianGrammar.LeftVerb(Gender.Male));
        Assert.Equal("вибула", UkrainianGrammar.LeftVerb(Gender.Female));
    }

    [Fact]
    public void DateWrittenIntoLedger_UsesDayMonthYear_WhateverTheTemplateCellFormat()
    {
        using var workbook = new XLWorkbook();
        var cell = workbook.AddWorksheet("Аркуш").Cell(1, 1);
        cell.Style.DateFormat.Format = "[$-409]m/d/yyyy";

        XlsxGenerationService.AssignTypedOrString(cell, "04.08.2026");

        Assert.Equal(new DateTime(2026, 8, 4), cell.GetDateTime());
        Assert.Equal("04.08.2026", cell.GetFormattedString());
    }
}
