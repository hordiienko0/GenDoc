using GenDoc.Services.Documents;
using GenDoc.ViewModels.Staff;

namespace GenDoc.Tests;

public class StaffDocResultRowViewModelTests
{
    [Fact]
    public void CanOpen_SuccessWithDocumentId_IsTrue()
    {
        var row = new StaffDocResultRowViewModel(
            archiveService: null!, documentId: 5, fileName: "report.docx",
            recipientName: "ТЕСТ Т.Т.", templateName: "Рапорт", success: true, errorMessage: null);

        Assert.True(row.CanOpen);
    }

    [Fact]
    public void CanOpen_Failure_IsFalse()
    {
        var row = new StaffDocResultRowViewModel(
            archiveService: null!, documentId: null, fileName: string.Empty,
            recipientName: "ТЕСТ Т.Т.", templateName: "Рапорт", success: false, errorMessage: "Не вдалося згенерувати");

        Assert.False(row.CanOpen);
    }

    [Fact]
    public void CanOpen_SuccessWithoutDocumentId_IsFalse()
    {
        var row = new StaffDocResultRowViewModel(
            archiveService: null!, documentId: null, fileName: string.Empty,
            recipientName: "ТЕСТ Т.Т.", templateName: "Рапорт", success: true, errorMessage: null);

        Assert.False(row.CanOpen);
    }

    [Fact]
    public void Constructor_ExposesAllSuppliedValues()
    {
        var row = new StaffDocResultRowViewModel(
            archiveService: null!, documentId: 7, fileName: "trip.docx",
            recipientName: "ІВАНЕНКО І.І.", templateName: "Посвідчення", success: true, errorMessage: null);

        Assert.Equal("trip.docx", row.FileName);
        Assert.Equal("ІВАНЕНКО І.І.", row.RecipientName);
        Assert.Equal("Посвідчення", row.TemplateName);
        Assert.True(row.Success);
        Assert.Null(row.ErrorMessage);
    }
}
