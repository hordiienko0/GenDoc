using GenDoc.ViewModels.Archive;

namespace GenDoc.Tests.Archive;

public class ArchiveIntakeLabelTests
{
    [Fact]
    public void ShortIntakeLabel_UsesTheDisplayNumber_NotTheInternalOne()
    {
        // Набір із внутрішнім номером 7 скрізь підписаний «Набір №30» - в архіві теж має бути №30.
        Assert.Equal("№30", ArchiveRowViewModel.ShortIntakeLabel(7, "Набір №30"));
    }

    [Fact]
    public void ShortIntakeLabel_WithoutDisplayNumber_FallsBackToTheNumber()
    {
        Assert.Equal("№7", ArchiveRowViewModel.ShortIntakeLabel(7, null));
    }

    [Fact]
    public void ShortIntakeLabel_CustomName_StaysAsIs()
    {
        Assert.Equal("Обмежено придатні", ArchiveRowViewModel.ShortIntakeLabel(2, "Обмежено придатні"));
    }
}
