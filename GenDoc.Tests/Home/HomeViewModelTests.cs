using GenDoc.Models;
using GenDoc.Services;
using GenDoc.ViewModels.Home;

namespace GenDoc.Tests.Home;

public class HomeViewModelTests
{
    private static Intake Intake4() => new()
    {
        Id = 4, Number = 4, DisplayNumber = "Набір №4",
        DateStart = new DateOnly(2026, 8, 6), DateEnd = new DateOnly(2026, 10, 1)
    };

    [Fact]
    public void DayOfTotal_CountsInclusive() =>
        Assert.Equal((19, 57), ActiveIntakeState.DayOfTotal(Intake4(), new DateOnly(2026, 8, 24)));

    [Fact]
    public void BuildIntakeTitle_ReadsAsStatusLine() =>
        Assert.Equal("Набір №4 · день 19 з 57",
            HomeViewModel.BuildIntakeTitle(Intake4(), new DateOnly(2026, 8, 24)));

    [Fact]
    public void BuildLastRunText_ShowsDatePackageCount() =>
        Assert.Equal("20.08.2026 10:38 · пакет «Зброя» · згенеровано 4",
            HomeViewModel.BuildLastRunText(new DateTime(2026, 8, 20, 10, 38, 0), "Зброя", 4));

    [Fact]
    public void BuildLastRunText_WithoutPackage_SaysSelective() =>
        Assert.Equal("20.08.2026 10:38 · Вибірково · згенеровано 2",
            HomeViewModel.BuildLastRunText(new DateTime(2026, 8, 20, 10, 38, 0), null, 2));
}
